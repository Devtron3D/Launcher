using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LauncherApp
{
    public enum EngineState { Idle, Busy, Done, Problem }

    // Live state of one step of a running profile (drawn as a segment in the main window).
    public enum StepState { Queued, Starting, Up, Shared, Exited, Failed, Skipped }

    // One line of the launch log. The log lives in memory only (nothing is written to the PC).
    public class LogEntry
    {
        public DateTime Time;
        public string Tag;     // short profile tag, e.g. PRIME, or APP
        public string Kind;    // start, wait, ready, shared, skip, fail, warn, done, stop, info
        public string Text;
    }

    // What the main window shows for a running profile.
    public class RunInfo
    {
        public Profile Profile;
        public TimeSpan Elapsed;
        public int Done, Total;
        public string Activity;              // e.g. "waiting for Core Temp"
        public StepState[] States;
        public List<string> SharedNames = new List<string>();
    }

    public class LaunchEngine
    {
        // One program the launcher started.
        class Tracked
        {
            public Process Proc;              // what Process.Start returned (can be null)
            public string Label;              // step name, for messages
            public string ProcessName;        // exe name without extension, for the stub fallback
            public string ExePath = "";       // resolved program path, to recognise the same program in another profile
            public string Args = "";
            public DateTime Started;
            public bool Shared;               // also used by another profile
            public readonly List<Process> Adopted = new List<Process>();
            // Profiles that launched or reused it (changed on the UI thread only).
            public readonly HashSet<Profile> Owners = new HashSet<Profile>();
            // Waits check programs on a background thread while the window refreshes them, so the list of
            // processes is only read and changed under this lock.
            public readonly object Sync = new object();
            // When the processes it started were last looked up (see AdoptIfStub): at most once a second, because
            // the window's refresh asks several times a second.
            public DateTime LastAdoptScan = DateTime.MinValue;

            public List<Process> All()
            {
                lock (Sync)
                {
                    var list = new List<Process>(Adopted.Count + 1);
                    if (Proc != null) list.Add(Proc);
                    list.AddRange(Adopted);
                    return list;
                }
            }

            public bool Alive
            {
                get { return All().Any(IsAlive); }
            }

            public void Dispose()
            {
                lock (Sync)
                {
                    foreach (var p in All()) p.Dispose();
                    Adopted.Clear();
                    Proc = null;
                }
            }
        }

        // One launch in progress. Several can run at the same time.
        class Run
        {
            public Profile Profile;
            public DateTime Started = DateTime.Now;
            public readonly CancellationTokenSource Cts = new CancellationTokenSource();
            public List<Step> Steps;
            public StepState[] States;        // Failed / Skipped / Shared are set here; the rest is worked out live
            public Tracked[] StepTracked;
            public int Processed = -1;
            public string Activity = "starting";
            public DateTime? WaitUntil;       // during a delay: when the next program starts (for the countdown)
            public string NextLabel;          // ... and which program that is
        }

        readonly List<Tracked> tracked = new List<Tracked>();
        readonly List<Run> runs = new List<Run>();
        readonly List<LogEntry> log = new List<LogEntry>();
        readonly Dictionary<Profile, string> lastResult = new Dictionary<Profile, string>();
        readonly Dictionary<Profile, DateTime> launchedAt = new Dictionary<Profile, DateTime>();

        // When the profile was last launched (for "Running · 01:12"), or null.
        public DateTime? LaunchedAt(Profile p)
        {
            DateTime d;
            return launchedAt.TryGetValue(p, out d) ? d : (DateTime?)null;
        }
        const int MaxLog = 500;

        public bool IsRunning { get { return runs.Count > 0; } }
        public int RunningCount { get { return runs.Count; } }
        public bool IsClosing { get; private set; }

        public bool IsRunningProfile(Profile p)
        {
            return runs.Any(r => r.Profile == p);
        }

        // Raised on the UI thread whenever runs or the log change.
        public event Action Changed;

        void RaiseChanged()
        {
            var h = Changed;
            if (h != null) h();
        }

        // ---- log ----

        public IList<LogEntry> Log { get { return log.AsReadOnly(); } }

        public int WarningCount { get { return log.Count(e => e.Kind == "warn" || e.Kind == "fail"); } }

        public void Note(string tag, string kind, string text)
        {
            log.Add(new LogEntry { Time = DateTime.Now, Tag = tag, Kind = kind, Text = text });
            if (log.Count > MaxLog) log.RemoveRange(0, log.Count - MaxLog);
            RaiseChanged();
        }

        // "Prime95 stress test" -> "PRIME", "Bench setup" -> "BENCH"
        static string BaseTag(Profile p)
        {
            string first = ((p == null ? "" : p.Name) ?? "").Trim().Split(' ').FirstOrDefault() ?? "";
            string letters = new string(first.Where(char.IsLetter).ToArray()).ToUpperInvariant();
            if (letters.Length == 0) letters = "RUN";
            return letters.Length > 6 ? letters.Substring(0, 6) : letters;
        }

        // The log tag, made unique among the profiles: "Bench setup" and "Bench quick" become BENCH1 and BENCH2.
        public static string TagFor(Profile p, IList<Profile> all)
        {
            string tag = BaseTag(p);
            if (all == null) return tag;
            var same = all.Where(o => BaseTag(o) == tag).ToList();
            int i = same.IndexOf(p);
            if (same.Count < 2 || i < 0) return tag;
            string n = (i + 1).ToString();
            return (tag.Length + n.Length > 6 ? tag.Substring(0, 6 - n.Length) : tag) + n;
        }

        // Result of the profile's last launch ("OK", "2 problems", "stopped"), or null.
        public string LastResultFor(Profile p)
        {
            string r;
            return lastResult.TryGetValue(p, out r) ? r : null;
        }

        // The editor saves a changed copy of a profile (or deletes it, replacement null): the last result and the
        // programs it launched move to the copy, so "last: ..." and "N open" stay right.
        public void ProfileReplaced(Profile old, Profile replacement)
        {
            string r;
            if (lastResult.TryGetValue(old, out r))
            {
                lastResult.Remove(old);
                if (replacement != null) lastResult[replacement] = r;
            }
            foreach (var t in tracked)
                if (t.Owners.Remove(old) && replacement != null) t.Owners.Add(replacement);
            DateTime at;
            if (launchedAt.TryGetValue(old, out at))
            {
                launchedAt.Remove(old);
                if (replacement != null) launchedAt[replacement] = at;
            }
        }

        // Number of launched programs that are still open.
        public int OpenCount
        {
            get
            {
                Prune();
                return tracked.Count;
            }
        }

        // Drops launched programs that have closed (OpenCount does this too).
        public void DropClosed()
        {
            Prune();
        }

        // Number of programs this profile launched (or reused) that are still open, as of the last OpenCount
        // (which drops closed ones; the window reads it once per refresh instead of once per profile).
        public int OpenCountFor(Profile p)
        {
            return tracked.Count(t => t.Owners.Contains(p));
        }

        // Programs "close this profile's programs" closes: only the ones no other profile uses too.
        List<Tracked> Closable(Profile only)
        {
            return only == null ? tracked.ToList() : tracked.Where(t => t.Owners.Contains(only) && t.Owners.Count == 1).ToList();
        }

        // Names of open programs this profile shares with another one (left open when only it is closed).
        public List<string> SharedOpenFor(Profile p)
        {
            Prune();
            return tracked.Where(t => t.Owners.Contains(p) && t.Owners.Count > 1).Select(t => t.Label).ToList();
        }

        // ---- running profiles, for the main window ----

        public List<RunInfo> Runs()
        {
            var list = new List<RunInfo>();
            foreach (var r in runs)
            {
                var info = new RunInfo
                {
                    Profile = r.Profile,
                    Elapsed = DateTime.Now - r.Started,
                    Done = r.Processed + 1,
                    Total = r.Steps.Count,
                    Activity = r.WaitUntil.HasValue ? "starting " + r.NextLabel + " in " + Countdown(r.WaitUntil.Value - DateTime.Now) : r.Activity,
                    States = new StepState[r.Steps.Count]
                };
                for (int i = 0; i < r.Steps.Count; i++)
                {
                    var fixedState = r.States[i];
                    var t = r.StepTracked[i];
                    if (fixedState == StepState.Failed || fixedState == StepState.Skipped)
                        info.States[i] = fixedState;
                    else if (t != null)
                    {
                        if (!t.Alive) info.States[i] = StepState.Exited;
                        else if (t.Shared) info.States[i] = StepState.Shared;
                        else info.States[i] = LooksStarting(t) ? StepState.Starting : StepState.Up;
                        if (t.Shared && t.Alive && !info.SharedNames.Contains(t.Label)) info.SharedNames.Add(t.Label);
                    }
                    else info.States[i] = StepState.Queued;
                }
                list.Add(info);
            }
            return list;
        }

        // 09:41, or 1:05:09 from an hour up
        static string Countdown(TimeSpan left)
        {
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            int s = (int)Math.Ceiling(left.TotalSeconds);
            return s >= 3600 ? (s / 3600) + ":" + (s % 3600 / 60).ToString("00") + ":" + (s % 60).ToString("00")
                             : (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        static bool LooksStarting(Tracked t)
        {
            var proc = t.Proc;
            if (proc == null || !IsAlive(proc) || (DateTime.Now - t.Started).TotalSeconds >= 5) return false;
            try
            {
                proc.Refresh();
                return proc.MainWindowHandle == IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        // ---- launching ----

        // Runs the profile's steps in order. Several profiles can run at the same time. Steps in skip are
        // left out (programs you chose not to look for). A program that another launch already started (same
        // program, same arguments, still open) is reused instead of started twice, and marked shared.
        public async Task RunAsync(Profile profile, ICollection<Step> skip = null, string tag = null)
        {
            if (IsClosing || IsRunningProfile(profile)) return;
            Prune();
            var run = new Run
            {
                Profile = profile,
                Steps = profile.Steps.ToList()
            };
            run.States = new StepState[run.Steps.Count];
            run.StepTracked = new Tracked[run.Steps.Count];
            runs.Add(run);
            launchedAt[profile] = run.Started;
            if (tag == null) tag = BaseTag(profile);
            var ct = run.Cts.Token;
            var problems = new List<string>();
            int skipped = 0;
            Tracked prev = null;
            string prevLabel = null;
            bool prevFailed = false;
            Note(tag, "info", "launch " + profile.Name + " (" + run.Steps.Count + " programs)");

            try
            {
                for (int i = 0; i < run.Steps.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var step = run.Steps[i];
                    string label = step.DisplayName;
                    int timeout = step.EffectiveTimeout;

                    if (skip != null && skip.Contains(step))
                    {
                        // You chose to skip it because it wasn't found. Steps that wait for it start right away.
                        run.States[i] = StepState.Skipped;
                        run.Processed = i;
                        skipped++;
                        Note(tag, "skip", label + " skipped (not found)");
                        prevFailed = true;
                        prevLabel = label;
                        continue;
                    }

                    if (step.Start == StartMode.AfterDelay && step.DelaySeconds > 0)
                    {
                        run.Activity = "waiting " + Theme.Seconds(step.DelaySeconds);
                        run.NextLabel = label;
                        run.WaitUntil = DateTime.Now.AddSeconds(step.DelaySeconds);
                        if (step.DelaySeconds >= 60)
                            Note(tag, "wait", label + " starts in " + Theme.Seconds(step.DelaySeconds) + ", at " + run.WaitUntil.Value.ToString("HH:mm"));
                        RaiseChanged();
                        try { await Task.Delay(TimeSpan.FromSeconds(step.DelaySeconds), ct); }
                        finally { run.WaitUntil = null; }
                    }
                    else if (step.WaitsForPrevious && prevFailed)
                    {
                        // The previous step didn't start (that is already listed as a problem), so there is
                        // nothing to wait for. Don't wait on an older program instead.
                    }
                    else if (step.Start == StartMode.WhenPreviousRunning && prev != null)
                    {
                        run.Activity = "waiting for " + prevLabel;
                        Note(tag, "wait", prevLabel + " to be running");
                        bool up = await WaitFor(() => IsUp(prev), timeout, ct);
                        if (up) Note(tag, "ready", prevLabel + " is running");
                        else
                        {
                            problems.Add(prevLabel + " didn't look ready after " + Theme.Seconds(timeout));
                            Note(tag, "warn", problems[problems.Count - 1]);
                        }
                    }
                    else if (step.Start == StartMode.WhenPreviousExits && prev != null)
                    {
                        run.Activity = "waiting for " + prevLabel + " to close";
                        Note(tag, "wait", prevLabel + " to close");
                        bool gone = await WaitFor(() => HasFinished(prev), timeout, ct);
                        if (!gone)
                        {
                            problems.Add(prevLabel + " was still running after " + Theme.Seconds(timeout));
                            Note(tag, "warn", problems[problems.Count - 1]);
                        }
                    }

                    // Already open from another launch? Use that one.
                    var existing = FindOpen(step);
                    if (existing != null)
                    {
                        existing.Shared = true;
                        existing.Owners.Add(profile);
                        run.StepTracked[i] = existing;
                        run.States[i] = StepState.Shared;
                        run.Processed = i;
                        Note(tag, "shared", label + " already open, shared");
                        prev = existing;
                        prevLabel = label;
                        prevFailed = false;
                        continue;
                    }

                    run.Activity = "starting " + label;
                    string error;
                    var t = Start(step, label, out error);
                    run.Processed = i;
                    if (t == null)
                    {
                        run.States[i] = StepState.Failed;
                        problems.Add(label + ": " + error);
                        Note(tag, "fail", label + ": " + error);
                        prevFailed = true;
                        prevLabel = label;
                        continue;
                    }
                    t.Owners.Add(profile);
                    tracked.Add(t);
                    run.StepTracked[i] = t;
                    string pid = "";
                    try { if (t.Proc != null) pid = "  pid " + t.Proc.Id; } catch { }
                    Note(tag, "start", label + pid);
                    prev = t;
                    prevLabel = label;
                    prevFailed = false;
                }

                if (problems.Count == 0)
                {
                    lastResult[profile] = skipped == 0 ? "OK" : skipped + " skipped";
                    Note(tag, "done", skipped == 0 ? "all " + run.Steps.Count + " programs started"
                                                   : (run.Steps.Count - skipped) + " of " + run.Steps.Count + " started, " + skipped + " skipped");
                }
                else
                {
                    lastResult[profile] = problems.Count + (problems.Count == 1 ? " problem" : " problems");
                    Note(tag, "warn", "finished with " + problems.Count + " problem(s)");
                }
            }
            catch (OperationCanceledException)
            {
                lastResult[profile] = "stopped";
                Note(tag, "stop", "launch stopped");
            }
            finally
            {
                if (profile.StartOnly) LetGo(run, tag);
                runs.Remove(run);
                run.Cts.Dispose();
                RaiseChanged();
            }
        }

        // Start only: the launch is over, so stop tracking the programs it started. They keep running on their own;
        // ones another profile uses too stay tracked for that profile.
        void LetGo(Run run, string tag)
        {
            int released = 0;
            foreach (var t in run.StepTracked.Where(x => x != null).Distinct().ToList())
            {
                t.Owners.Remove(run.Profile);
                if (t.Owners.Count == 0 && tracked.Remove(t))
                {
                    t.Dispose();
                    released++;
                }
            }
            if (released > 0)
                Note(tag, "info", "start only: " + released + (released == 1 ? " program runs" : " programs run") + " on its own now (not tracked)");
        }

        // A launched program that is still open and is the same program with the same arguments.
        Tracked FindOpen(Step step)
        {
            string exe = PathUtil.ResolveProgram(step.ProgramPath);
            if (exe.Length == 0 || !PathUtil.IsRooted(exe)) return null;   // e.g. cmd.exe: separate windows on purpose
            string args = PathUtil.Expand(step.Arguments);
            return tracked.FirstOrDefault(t =>
                string.Equals(t.ExePath, exe, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.Args, args, StringComparison.Ordinal) &&
                t.Alive);
        }

        static Tracked Start(Step step, string label, out string error)
        {
            error = null;
            try
            {
                string exe = PathUtil.ResolveProgram(step.ProgramPath);
                if (exe.Length == 0) { error = "no program set"; return null; }
                bool rooted = PathUtil.IsRooted(exe);
                bool isFolder = rooted && Directory.Exists(exe);
                if (rooted && !isFolder && !File.Exists(exe))
                {
                    error = "not found at " + exe;
                    return null;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = PathUtil.Expand(step.Arguments),
                    UseShellExecute = true
                };
                if (step.RunAsAdmin && !isFolder) psi.Verb = "runas";
                if (step.StartMinimized) psi.WindowStyle = ProcessWindowStyle.Minimized;
                string dir = rooted && !isFolder ? Path.GetDirectoryName(exe) : null;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) psi.WorkingDirectory = dir;

                var t = new Tracked
                {
                    Label = label,
                    ProcessName = isFolder ? "" : Path.GetFileNameWithoutExtension(exe),
                    ExePath = exe,
                    Args = psi.Arguments ?? "",
                    Started = DateTime.Now
                };
                t.Proc = Process.Start(psi);
                return t;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                error = "the administrator prompt was cancelled";
                return null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        static bool IsAlive(Process p)
        {
            try { return !p.HasExited; }
            catch { return false; }
        }

        // ---- child processes ----

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode)]
        static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);

        [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode)]
        static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);

        // (process id, parent process id) of every running process.
        static List<KeyValuePair<int, int>> ParentTable()
        {
            var result = new List<KeyValuePair<int, int>>();
            IntPtr snap = CreateToolhelp32Snapshot(2 /* TH32CS_SNAPPROCESS */, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return result;
            try
            {
                var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
                if (!Process32First(snap, ref e)) return result;
                do result.Add(new KeyValuePair<int, int>((int)e.th32ProcessID, (int)e.th32ParentProcessID));
                while (Process32Next(snap, ref e));
            }
            finally
            {
                CloseHandle(snap);
            }
            return result;
        }

        // Some programs are a small launcher that starts the real program and exits right away.
        // When the process we started has exited, take over the processes it started (and the ones those
        // started). A child must have been started while its parent was still running, which rules out
        // unrelated processes that happen to reuse an old process id. Processes the launcher didn't start
        // (for example your own windows of the same program) are never touched.
        static void AdoptIfStub(Tracked t, bool now = false)
        {
            // One at a time per program (a background wait and the window's refresh can both get here).
            lock (t.Sync) AdoptIfStubLocked(t, now);
        }

        static void AdoptIfStubLocked(Tracked t, bool now)
        {
            var proc = t.Proc;
            if (proc == null || IsAlive(proc)) return;
            // Looking up every process takes a moment; the refresh asks several times a second. Closing asks "now".
            if (!now && (DateTime.Now - t.LastAdoptScan).TotalMilliseconds < 1000) return;
            t.LastAdoptScan = DateTime.Now;

            int rootId;
            DateTime rootExit;
            try
            {
                rootId = proc.Id;
                rootExit = proc.ExitTime;
            }
            catch { return; }

            var owned = new HashSet<int> { rootId };
            foreach (var a in t.Adopted)
                try { if (IsAlive(a)) owned.Add(a.Id); } catch { }

            List<KeyValuePair<int, int>> table;
            try { table = ParentTable(); }
            catch { return; }

            bool added = true;
            while (added)
            {
                added = false;
                foreach (var entry in table)
                {
                    int pid = entry.Key, parent = entry.Value;
                    if (!owned.Contains(parent) || owned.Contains(pid)) continue;

                    Process q = null;
                    try
                    {
                        q = Process.GetProcessById(pid);
                        var st = q.StartTime;
                        bool ok = !q.HasExited &&
                                  st >= t.Started.AddSeconds(-1) &&
                                  (parent != rootId || st <= rootExit.AddSeconds(1));
                        if (ok)
                        {
                            t.Adopted.Add(q);   // under t.Sync (see AdoptIfStub)
                            owned.Add(pid);
                            added = true;
                            q = null;
                        }
                    }
                    catch { }
                    finally
                    {
                        if (q != null) q.Dispose();
                    }
                }
            }
        }

        // Kills every process below the given ones that started after the step did.
        static void KillDescendants(List<KeyValuePair<int, int>> table, List<int> roots, DateTime started)
        {
            var owned = new HashSet<int>(roots);
            var found = new List<int>();
            bool added = true;
            while (added)
            {
                added = false;
                foreach (var entry in table)
                {
                    if (owned.Contains(entry.Value) && !owned.Contains(entry.Key))
                    {
                        owned.Add(entry.Key);
                        found.Add(entry.Key);
                        added = true;
                    }
                }
            }

            for (int i = found.Count - 1; i >= 0; i--)
            {
                try
                {
                    using (var q = Process.GetProcessById(found[i]))
                        if (!q.HasExited && q.StartTime >= started.AddSeconds(-1)) q.Kill();
                }
                catch { }
            }
        }

        // "Running" = the process is alive and has a window (or has been alive for 5 s, for
        // console/background tools). If it already exited (launcher stub, or a single-instance program
        // that handed over to a copy that was already open), look for a process with the same name.
        static bool IsUp(Tracked t)
        {
            var proc = t.Proc;   // read once: the window's refresh may clear it once the program has closed
            if (proc != null && IsAlive(proc))
            {
                try
                {
                    proc.Refresh();
                    if (proc.MainWindowHandle != IntPtr.Zero) return true;
                }
                catch { }
                return (DateTime.Now - t.Started).TotalSeconds > 5;
            }

            AdoptIfStub(t);
            if (t.All().Any(p => p != proc && IsAlive(p))) return true;   // the adopted ones

            if (string.IsNullOrEmpty(t.ProcessName)) return proc == null;   // e.g. a folder opened in Explorer
            Process[] list = null;
            try
            {
                list = Process.GetProcessesByName(t.ProcessName);
                return list.Any(IsAlive);
            }
            catch { return false; }
            finally
            {
                if (list != null) foreach (var q in list) q.Dispose();
            }
        }

        // Used by "when previous exits". A stub that already exited counts as finished once the
        // program it started (if any) has closed too.
        static bool HasFinished(Tracked t)
        {
            var proc = t.Proc;
            if (proc != null && IsAlive(proc)) return false;
            AdoptIfStub(t);
            return !t.All().Any(IsAlive);
        }

        // Checks on a background thread (looking up processes can take a moment on a busy PC), so the
        // window stays smooth while a step waits.
        static async Task<bool> WaitFor(Func<bool> check, int timeoutSeconds, CancellationToken ct)
        {
            var limit = DateTime.Now.AddSeconds(timeoutSeconds);
            while (true)
            {
                bool ok = await Task.Run(() =>
                {
                    try { return check(); }
                    catch { return false; }
                });
                if (ok) return true;
                if (DateTime.Now >= limit) return false;
                await Task.Delay(300, ct);
            }
        }

        // Stops a profile's launch in progress. Programs that already started stay open.
        public void Cancel(Profile p)
        {
            foreach (var r in runs.Where(r => r.Profile == p).ToList())
                try { r.Cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        // Stops every launch in progress.
        public void CancelAll()
        {
            foreach (var r in runs.ToList())
                try { r.Cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        // Removes programs that have closed from the list (so it doesn't grow with every launch).
        void Prune()
        {
            tracked.RemoveAll(t =>
            {
                AdoptIfStub(t);
                if (t.Alive) return false;
                t.Dispose();
                return true;
            });
        }

        // Stops a launch in progress and asks every launched program to close (like clicking its X),
        // all at the same time, waiting up to waitMs. Returns the names of programs that are still open.
        // With a profile: only that profile's launch and the programs no other profile uses too.
        public async Task<List<string>> RequestCloseAsync(int waitMs, Profile only = null)
        {
            if (only == null) CancelAll();
            else Cancel(only);
            IsClosing = true;
            try
            {
                var list = Closable(only);
                await Task.Run(() =>
                {
                    foreach (var t in list) AdoptIfStub(t, true);
                    foreach (var t in list)
                        foreach (var p in t.All())
                            try { if (!p.HasExited) p.CloseMainWindow(); } catch { }

                    var limit = DateTime.Now.AddMilliseconds(waitMs);
                    while (DateTime.Now < limit && list.Any(t => t.Alive)) Thread.Sleep(200);
                });
                Prune();
                return Closable(only).Select(t => t.Label).ToList();
            }
            finally
            {
                IsClosing = false;
            }
        }

        // Ends the programs that didn't close when asked. Unsaved work in them is lost.
        // Returns the names of programs that couldn't be ended (e.g. ones running as administrator
        // while the launcher isn't).
        public async Task<List<string>> KillRemainingAsync(Profile only = null)
        {
            IsClosing = true;
            try
            {
                var list = Closable(only);
                await Task.Run(() =>
                {
                    List<KeyValuePair<int, int>> table;
                    try { table = ParentTable(); }
                    catch { table = new List<KeyValuePair<int, int>>(); }

                    foreach (var t in list)
                    {
                        // Children first (like taskkill /T), so console tools such as ping don't outlive their cmd window.
                        var roots = new List<int>();
                        foreach (var p in t.All())
                            try { if (!p.HasExited) roots.Add(p.Id); } catch { }
                        KillDescendants(table, roots, t.Started);

                        foreach (var p in t.All())
                            try { if (!p.HasExited) p.Kill(); } catch { }
                    }

                    var limit = DateTime.Now.AddSeconds(3);
                    while (DateTime.Now < limit && list.Any(t => t.Alive)) Thread.Sleep(200);
                });
                Prune();
                return Closable(only).Select(t => t.Label).ToList();
            }
            finally
            {
                IsClosing = false;
            }
        }
    }
}
