using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace LauncherApp
{
    // Everything that keeps the launcher portable: where its data lives, where the tools are,
    // finding programs that moved, and keeping launched programs' temp files on the stick.

    [DataContract]
    public class AppSettings
    {
        // Where the tools are. "" = the launcher data folder. Normally stored relative to the data
        // folder (e.g. "Tools" or "..\Tools") so it keeps working when the drive letter changes.
        [DataMember(Name = "toolsFolder")] public string ToolsFolder { get; set; }

        // Point TEMP/TMP of launched programs to PortableData\Temp on the stick.
        [DataMember(Name = "keepTempOnStick")] public bool KeepTempOnStick { get; set; }

        // Point APPDATA/LOCALAPPDATA of launched programs to PortableData\AppData on the stick.
        [DataMember(Name = "keepAppDataOnStick")] public bool KeepAppDataOnStick { get; set; }

        // Look: system (follow Windows), dark, light or phosphor.
        [DataMember(Name = "theme")] public string Theme { get; set; }

        // Empty PortableData\Temp when the launcher starts (files still in use are skipped).
        [DataMember(Name = "emptyTempOnStart")] public bool EmptyTempOnStart { get; set; }

        // Main window layout: "main" (wide, profile tiles) or "compact" (narrow list).
        public const string MainLayout = "main", CompactLayout = "compact";
        [DataMember(Name = "layout")] public string Layout { get; set; }

        // Icons of running profiles move (and glow) while the profile runs or its programs are open.
        [DataMember(Name = "animateRunning")] public bool AnimateRunning { get; set; }

        public AppSettings()
        {
            SetDefaults();
        }

        [OnDeserializing]
        void OnDeserializing(StreamingContext c)
        {
            SetDefaults();
        }

        void SetDefaults()
        {
            ToolsFolder = "";
            KeepTempOnStick = true;
            KeepAppDataOnStick = false;
            Theme = "system";
            EmptyTempOnStart = true;
            Layout = MainLayout;
            AnimateRunning = true;
        }

        public AppSettings Clone()
        {
            return (AppSettings)MemberwiseClone();
        }
    }

    public static class PathUtil
    {
        public const string SettingsFileName = "launcher-settings.json";
        public const string ProfilesFileName = "profiles.json";
        public const string PortableDataName = "PortableData";

        static readonly Regex PlaceholderPrefix = new Regex(@"^\s*""?\{(app|tools)\}\\*(.*)$", RegexOptions.IgnoreCase);

        // Folder Launcher.exe is in.
        public static readonly string ExeDir = Trim(AppDomain.CurrentDomain.BaseDirectory);

        // Folder with profiles.json and launcher-settings.json ({app} in paths). Normally the exe folder,
        // but when the exe was moved it is found again by looking in the folders above it and next to them.
        public static readonly string HomeDir = FindHome();

        // The tools folder ({tools} in paths).
        public static string ToolsDir { get; private set; }

        // False when the configured tools folder couldn't be found.
        public static bool ToolsDirFound { get; private set; }

        static PathUtil()
        {
            ToolsDir = HomeDir;
            ToolsDirFound = true;
        }

        // "E:\" -> "E:", "E:\Tools\" -> "E:\Tools". Placeholders are replaced with this form.
        public static string Trim(string dir)
        {
            return (dir ?? "").TrimEnd('\\', '/');
        }

        // "E:" -> "E:\" (a bare "E:" means "current folder on E:" to Windows).
        public static string Full(string dir)
        {
            dir = Trim(dir);
            return dir.EndsWith(":") ? dir + "\\" : dir;
        }

        public static string In(string dir, string name)
        {
            return Path.Combine(Trim(dir) + "\\", name);
        }

        public static bool SamePath(string a, string b)
        {
            return string.Equals(Trim(a), Trim(b), StringComparison.OrdinalIgnoreCase);
        }

        public static bool Exists(string path)
        {
            return File.Exists(path) || Directory.Exists(Full(path));
        }

        // True when the path is on the same drive as the launcher's data (the stick).
        public static bool IsOnDataDrive(string path)
        {
            try
            {
                return string.Equals(Path.GetPathRoot(Full(path)), Path.GetPathRoot(Full(HomeDir)), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // True for "C:" or "C:".
        public static bool IsDriveRoot(string path)
        {
            try
            {
                string f = Full(path);
                return string.Equals(Path.GetPathRoot(f), f, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // The folder itself, its parent, and so on up to the drive root.
        public static IEnumerable<string> Ancestors(string dir)
        {
            DirectoryInfo d;
            try { d = new DirectoryInfo(Full(dir)); }
            catch { yield break; }
            while (d != null)
            {
                yield return Trim(d.FullName);
                d = d.Parent;
            }
        }

        static IEnumerable<string> SubDirs(string dir)
        {
            try
            {
                return new DirectoryInfo(Full(dir)).GetDirectories()
                    .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                    .Select(d => Trim(d.FullName))
                    .Take(300)
                    .ToList();
            }
            catch
            {
                return new string[0];
            }
        }

        static bool IsDataFolder(string dir)
        {
            try
            {
                if (File.Exists(In(dir, SettingsFileName))) return true;
                string profiles = In(dir, ProfilesFileName);
                if (!File.Exists(profiles)) return false;
                using (var r = new StreamReader(profiles))
                {
                    var buf = new char[65536];
                    string head = new string(buf, 0, r.Read(buf, 0, buf.Length));
                    return head.Contains("\"profiles\"") && head.Contains("\"steps\"");
                }
            }
            catch
            {
                return false;
            }
        }

        static string FindHome()
        {
            try
            {
                var up = Ancestors(ExeDir).ToList();
                foreach (var d in up)
                    if (IsDataFolder(d)) return d;
                // The exe was moved next to or above its data, e.g. from E:\Tools to E:\ or E:\Launcher.
                foreach (var d in up)
                    foreach (var sub in SubDirs(d))
                        if (IsDataFolder(sub)) return sub;
            }
            catch { }
            return ExeDir;
        }

        // ---- tools folder ----

        public static void SetToolsFolder(string stored)
        {
            bool found;
            ToolsDir = ResolveToolsFolder(stored, out found);
            ToolsDirFound = found;
        }

        public static string ResolveToolsFolder(string stored, out bool found)
        {
            stored = (stored ?? "").Trim().Trim('"');
            string dir;
            try
            {
                if (stored.Length == 0) dir = HomeDir;
                else if (stored.StartsWith("\\") && !stored.StartsWith("\\\\"))
                    dir = Trim(Path.GetPathRoot(Full(HomeDir))) + stored;            // same drive as the data
                else if (IsRooted(stored)) dir = Environment.ExpandEnvironmentVariables(stored);
                else dir = Path.GetFullPath(In(HomeDir, stored));
                dir = Trim(dir);
            }
            catch
            {
                found = false;
                return HomeDir;
            }

            found = Directory.Exists(Full(dir));
            if (found || stored.Length == 0 || IsRooted(stored)) return dir;

            // Relative folder not found (the data folder was moved): try the same relative path
            // from the folders above the data folder.
            foreach (var anc in Ancestors(HomeDir).Skip(1))
            {
                try
                {
                    string c = Trim(Path.GetFullPath(In(anc, stored)));
                    if (Directory.Exists(Full(c)))
                    {
                        found = true;
                        return c;
                    }
                }
                catch { }
            }
            return dir;
        }

        // How a chosen tools folder is written to the settings: relative to the data folder when it is on
        // the same drive (survives drive letter changes), otherwise the full path.
        public static string StoreToolsFolder(string full)
        {
            full = Trim(full);
            if (full.Length == 0 || SamePath(full, HomeDir)) return "";
            try
            {
                string a = Path.GetPathRoot(Full(full)), b = Path.GetPathRoot(Full(HomeDir));
                if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return full;
                return RelativePath(HomeDir, full);
            }
            catch
            {
                return full;
            }
        }

        static string RelativePath(string from, string to)
        {
            var f = Trim(Path.GetFullPath(Full(from))).Split('\\');
            var t = Trim(Path.GetFullPath(Full(to))).Split('\\');
            int common = 0;
            while (common < f.Length && common < t.Length &&
                   string.Equals(f[common], t[common], StringComparison.OrdinalIgnoreCase)) common++;
            var parts = new List<string>();
            for (int i = common; i < f.Length; i++) parts.Add("..");
            for (int i = common; i < t.Length; i++) parts.Add(t[i]);
            return parts.Count == 0 ? "" : string.Join("\\", parts);
        }

        // ---- program paths ----

        // Replaces {app} and {tools} and expands %ENV% variables.
        public static string Expand(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Regex.Replace(s, @"\{app\}", m => HomeDir, RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\{tools\}", m => ToolsDir, RegexOptions.IgnoreCase);
            return Environment.ExpandEnvironmentVariables(s);
        }

        public static string ResolveProgram(string p)
        {
            string e = Expand(p).Trim().Trim('"');
            try
            {
                if (e.Length > 0 && !Path.IsPathRooted(e))
                {
                    foreach (var dir in new[] { ToolsDir, HomeDir, ExeDir })
                    {
                        string c = In(dir, e);
                        if (Exists(c)) return c;
                    }
                }
            }
            catch (ArgumentException) { }
            return e;
        }

        public static bool IsRooted(string e)
        {
            try { return Path.IsPathRooted(e); }
            catch (ArgumentException) { return false; }
        }

        // "{tools}\CPU-Z\cpuz.exe" -> "CPU-Z\cpuz.exe". Null for paths without a placeholder.
        public static string PlaceholderRemainder(string stored)
        {
            var m = PlaceholderPrefix.Match(stored ?? "");
            return m.Success ? m.Groups[2].Value.Trim().Trim('"') : null;
        }

        // Turns "E:\Tools\x.exe" into "{tools}\x.exe" (or "{app}\..." for the data folder), so it keeps
        // working when the drive letter changes. Uses whichever of the two folders is the closer match.
        public static string MakePortable(string full)
        {
            string best = null, bestTag = null;
            foreach (var pair in new[] { new[] { ToolsDir, "{tools}" }, new[] { HomeDir, "{app}" } })
            {
                string dir = Trim(pair[0]) + "\\";
                if (full.StartsWith(dir, StringComparison.OrdinalIgnoreCase) && (best == null || dir.Length > best.Length))
                {
                    best = dir;
                    bestTag = pair[1];
                }
            }
            return best == null ? full : bestTag + "\\" + full.Substring(best.Length);
        }
    }

    // Small helpers for reading and safely writing the JSON files.
    public static class JsonFile
    {
        // Opens a JSON file for reading, without the byte order mark Notepad and PowerShell put at the start
        // of UTF-8 files (.NET's JSON reader stops at it).
        public static Stream OpenRead(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            int skip = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return new MemoryStream(bytes, skip, bytes.Length - skip, false);
        }

        public static string ToJson(object o)
        {
            var ser = new DataContractJsonSerializer(o.GetType());
            using (var ms = new MemoryStream())
            {
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                {
                    ser.WriteObject(w, o);
                    w.Flush();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        // Writes to a temporary file first and then swaps it in, so a crash or a pulled USB stick
        // never leaves a half-written file.
        public static void WriteAtomic(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));

            if (!File.Exists(path))
            {
                File.Move(tmp, path);
                return;
            }
            try
            {
                File.Replace(tmp, path, null);
            }
            catch (Exception ex) when (ex is IOException || ex is PlatformNotSupportedException || ex is UnauthorizedAccessException)
            {
                // Some drives don't support an atomic replace. Keep a backup until the new file is in place.
                string bak = path + ".bak";
                File.Copy(path, bak, true);
                File.Delete(path);
                File.Move(tmp, path);
                try { File.Delete(bak); } catch { }
            }
        }
    }

    public static class SettingsStore
    {
        public static string FilePath
        {
            get { return PathUtil.In(PathUtil.HomeDir, PathUtil.SettingsFileName); }
        }

        public static AppSettings Current { get; private set; }

        static SettingsStore()
        {
            Current = new AppSettings();
        }

        // Loads the settings and applies them. Returns a warning to show, or null.
        public static string Load()
        {
            var s = new AppSettings();
            string warning = null;
            if (File.Exists(FilePath))
            {
                try
                {
                    using (var fs = JsonFile.OpenRead(FilePath))
                        s = (AppSettings)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(fs) ?? new AppSettings();
                    if (s.ToolsFolder == null) s.ToolsFolder = "";
                }
                catch (Exception ex)
                {
                    s = new AppSettings();
                    warning = "Couldn't read " + PathUtil.SettingsFileName + " (" + ex.Message + "). Default settings are used.";
                }
            }
            else
            {
                // First start: a "Tools" folder next to the launcher is the tools folder.
                if (Directory.Exists(PathUtil.In(PathUtil.HomeDir, "Tools"))) s.ToolsFolder = "Tools";
                // Write the defaults so the data folder can be recognised if the exe is moved later.
                try { JsonFile.WriteAtomic(FilePath, JsonFile.ToJson(s)); } catch { }
            }
            string envWarning = Apply(s);
            return Join(warning, envWarning);
        }

        // Saves and applies. Throws when the file can't be written. Returns a warning to show, or null.
        public static string Save(AppSettings s)
        {
            JsonFile.WriteAtomic(FilePath, JsonFile.ToJson(s));
            return Apply(s);
        }

        // Saves only the look (from the main window's light/dark button). Throws when the file can't be written.
        public static void SaveTheme(string theme)
        {
            Current.Theme = theme;
            JsonFile.WriteAtomic(FilePath, JsonFile.ToJson(Current));
        }

        // Saves only the layout (from the title bar's C/F button). Throws when the file can't be written.
        public static void SaveLayout(string layout)
        {
            Current.Layout = layout;
            JsonFile.WriteAtomic(FilePath, JsonFile.ToJson(Current));
        }

        static string Apply(AppSettings s)
        {
            Current = s;
            PathUtil.SetToolsFolder(s.ToolsFolder);
            return PortableEnv.Apply(s);
        }

        static string Join(string a, string b)
        {
            if (a == null) return b;
            if (b == null) return a;
            return a + "\n\n" + b;
        }
    }

    // Programs the launcher starts inherit its environment, so pointing TEMP (and optionally APPDATA)
    // at the stick here keeps many of their temp and settings files off the PC's drive.
    public static class PortableEnv
    {
        static readonly string[] Names = { "TEMP", "TMP", "APPDATA", "LOCALAPPDATA" };
        // The PC's own values. A launcher restarted as admin inherits already redirected values, so the
        // originals are also passed on in LAUNCHER_ORIGINAL_* variables.
        static readonly Dictionary<string, string> original = Names.ToDictionary(n => n, n =>
        {
            string o = Environment.GetEnvironmentVariable("LAUNCHER_ORIGINAL_" + n) ?? Environment.GetEnvironmentVariable(n);
            Environment.SetEnvironmentVariable("LAUNCHER_ORIGINAL_" + n, o);
            return o;
        });

        public static string DataDir
        {
            get { return PathUtil.In(PathUtil.HomeDir, PathUtil.PortableDataName); }
        }

        // The PC's own value of TEMP, TMP, APPDATA or LOCALAPPDATA (before the launcher pointed it at the stick).
        public static string Original(string name)
        {
            string v;
            return original.TryGetValue(name, out v) ? v : Environment.GetEnvironmentVariable(name);
        }

        // Size of a folder and everything below it, in bytes (links to other folders are not followed).
        public static long FolderSize(string dir)
        {
            long total = 0;
            try
            {
                var stack = new Stack<DirectoryInfo>();
                stack.Push(new DirectoryInfo(PathUtil.Full(dir)));
                while (stack.Count > 0)
                {
                    var d = stack.Pop();
                    try
                    {
                        foreach (var f in d.EnumerateFiles()) try { total += f.Length; } catch { }
                        foreach (var sub in d.EnumerateDirectories())
                            if ((sub.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push(sub);
                    }
                    catch { }
                }
            }
            catch { }
            return total;
        }

        public static string TempDir
        {
            get { return PathUtil.In(DataDir, "Temp"); }
        }

        // Size of everything in the temp folder on the stick, in bytes.
        public static long TempSize()
        {
            long total = 0;
            try
            {
                var root = new DirectoryInfo(TempDir);
                if (!root.Exists) return 0;
                var stack = new Stack<DirectoryInfo>();
                stack.Push(root);
                while (stack.Count > 0)
                {
                    var d = stack.Pop();
                    try
                    {
                        foreach (var f in d.EnumerateFiles()) try { total += f.Length; } catch { }
                        foreach (var sub in d.EnumerateDirectories())
                            if ((sub.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push(sub);
                    }
                    catch { }
                }
            }
            catch { }
            return total;
        }

        // Deletes what is in the temp folder on the stick (only that folder). Files that are in use are skipped.
        // Links to other folders are removed without touching what they point to. Returns the bytes freed.
        public static long EmptyTemp()
        {
            var root = new DirectoryInfo(TempDir);
            return root.Exists ? EmptyFolder(root) : 0;
        }

        static long EmptyFolder(DirectoryInfo dir)
        {
            long freed = 0;
            try
            {
                foreach (var f in dir.EnumerateFiles())
                {
                    try
                    {
                        long len = f.Length;
                        if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes &= ~FileAttributes.ReadOnly;
                        f.Delete();
                        freed += len;
                    }
                    catch { }
                }
                foreach (var sub in dir.EnumerateDirectories())
                {
                    try
                    {
                        if ((sub.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            sub.Delete(false);   // just the link
                            continue;
                        }
                        freed += EmptyFolder(sub);
                        sub.Delete(false);       // only succeeds when it is empty now
                    }
                    catch { }
                }
            }
            catch { }
            return freed;
        }

        // 1536 -> "1.5 KB", 0 -> "empty"
        public static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "empty";
            string[] units = { "bytes", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return (u == 0 ? v.ToString("0") : v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)) + " " + units[u];
        }

        public static string Apply(AppSettings s)
        {
            var problems = new List<string>();

            string temp = PathUtil.In(DataDir, "Temp");
            if (s.KeepTempOnStick && Ensure(temp, problems))
            {
                Environment.SetEnvironmentVariable("TEMP", temp);
                Environment.SetEnvironmentVariable("TMP", temp);
            }
            else
            {
                Restore("TEMP");
                Restore("TMP");
            }

            string roaming = PathUtil.In(DataDir, @"AppData\Roaming");
            string local = PathUtil.In(DataDir, @"AppData\Local");
            if (s.KeepAppDataOnStick && Ensure(roaming, problems) && Ensure(local, problems))
            {
                Environment.SetEnvironmentVariable("APPDATA", roaming);
                Environment.SetEnvironmentVariable("LOCALAPPDATA", local);
            }
            else
            {
                Restore("APPDATA");
                Restore("LOCALAPPDATA");
            }

            return problems.Count == 0 ? null
                : "Couldn't create folders on the stick, so launched programs use the PC's folders instead:\n- " + string.Join("\n- ", problems);
        }

        static bool Ensure(string dir, List<string> problems)
        {
            try
            {
                Directory.CreateDirectory(dir);
                return true;
            }
            catch (Exception ex)
            {
                problems.Add(dir + ": " + ex.Message);
                return false;
            }
        }

        static void Restore(string name)
        {
            Environment.SetEnvironmentVariable(name, original[name]);
        }
    }

    // Walks a folder tree without failing on folders it may not open, skipping hidden and system folders,
    // the stick's recycle bin and the launcher's own PortableData folder.
    public static class FileWalker
    {
        public static readonly string[] ProgramExtensions = { ".exe", ".bat", ".cmd", ".lnk", ".ps1", ".com" };

        public static bool IsProgram(string path)
        {
            string ext = Path.GetExtension(path);
            return ProgramExtensions.Any(x => string.Equals(x, ext, StringComparison.OrdinalIgnoreCase));
        }

        public static List<string> Walk(string root, Func<string, bool> include, int maxDepth, int maxFiles, CancellationToken ct)
        {
            var result = new List<string>();
            string skip = PathUtil.Trim(PortableEnv.DataDir);
            var queue = new Queue<KeyValuePair<string, int>>();
            queue.Enqueue(new KeyValuePair<string, int>(PathUtil.Full(root), 0));

            while (queue.Count > 0 && result.Count < maxFiles)
            {
                ct.ThrowIfCancellationRequested();
                var item = queue.Dequeue();
                DirectoryInfo dir = new DirectoryInfo(item.Key);
                try
                {
                    foreach (var f in dir.EnumerateFiles())
                    {
                        if (include(f.FullName)) result.Add(f.FullName);
                        if (result.Count >= maxFiles) break;
                    }
                    if (item.Value >= maxDepth) continue;
                    foreach (var d in dir.EnumerateDirectories())
                    {
                        if ((d.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0) continue;
                        if (PathUtil.SamePath(d.FullName, skip)) continue;
                        queue.Enqueue(new KeyValuePair<string, int>(d.FullName, item.Value + 1));
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is System.Security.SecurityException) { }
            }
            return result;
        }
    }

    // How much a file looks like a missing program: the same file name, the same name with another version
    // or suffix ("HWMonitor_old.exe" / "HWMonitor.exe", "cpuz_x64.exe" / "cpuz.exe"), or a file in a folder with
    // the old folder's name. Used by the program list and by "Look in a folder".
    public static class ProgramMatch
    {
        // 80 and up: the same program (the name only differs in version or suffix). Below that: a guess.
        public const int Sure = 80;

        static readonly HashSet<string> Noise = new HashSet<string> { "x64", "x86", "64", "32", "win", "win32", "win64", "old", "new", "setup", "portable", "exe" };

        // "HWMonitor_x64 (old).exe" -> "hwmonitor": letters and digits of the name, without version-like endings.
        public static string Squash(string fileName)
        {
            string stem = (Path.GetFileNameWithoutExtension(fileName ?? "") ?? "").ToLowerInvariant();
            var words = Regex.Split(stem, "[^a-z0-9]+").Where(w => w.Length > 0 && !Noise.Contains(w));
            string s = string.Concat(words);
            foreach (var end in new[] { "x64", "x86", "64", "32" })
                if (s.Length > end.Length + 2 && s.EndsWith(end)) s = s.Substring(0, s.Length - end.Length);
            return s;
        }

        // candidate: a file (full or relative path). lookingName / lookingFolder: the missing program's file name
        // and the name of the folder it used to be in. 0 = no match; higher = more likely.
        public static int Score(string candidate, string lookingName, string lookingFolder)
        {
            if (string.IsNullOrEmpty(lookingName)) return 0;
            string name, folder;
            try
            {
                name = Path.GetFileName(candidate);
                folder = Path.GetFileName(Path.GetDirectoryName(candidate) ?? "");
            }
            catch (ArgumentException) { return 0; }

            int score = 0;
            if (string.Equals(name, lookingName, StringComparison.OrdinalIgnoreCase)) score = 100;
            else if (string.Equals(Path.GetFileNameWithoutExtension(name), Path.GetFileNameWithoutExtension(lookingName), StringComparison.OrdinalIgnoreCase)) score = 90;
            else
            {
                string a = Squash(name), b = Squash(lookingName);
                if (a.Length >= 3 && a == b) score = Sure;
                else if (a.Length >= 4 && b.Length >= 4 && (a.Contains(b) || b.Contains(a))) score = 60;
            }
            // In a folder with the same name as the old one (e.g. ...\HWMonitor\)
            if (!string.IsNullOrEmpty(lookingFolder) && string.Equals(folder, lookingFolder, StringComparison.OrdinalIgnoreCase))
                score = score > 0 ? score + 15 : 30;
            return score;
        }

        // The file name and folder name of where a program used to be.
        public static void Split(string expectedPath, out string name, out string folder)
        {
            name = null;
            folder = null;
            try
            {
                string p = (expectedPath ?? "").Trim().Trim('"');
                name = Path.GetFileName(p);
                folder = Path.GetFileName(Path.GetDirectoryName(p) ?? "");
                if (string.IsNullOrEmpty(name)) name = null;
            }
            catch (ArgumentException) { name = null; }
        }
    }

    // Suggests where a missing program might be now. It never scans folders: it only checks a few exact
    // paths, and every suggestion is shown to you to confirm. Create one per launch or check, so moves you
    // confirm ("HWMonitor went from E:\Tools to E:\Sensors") are reused for the next missing programs.
    public class ProgramLocator
    {
        readonly List<KeyValuePair<string, string>> moves = new List<KeyValuePair<string, string>>();   // old folder -> new folder

        // -File "path" (or -File path) in PowerShell arguments.
        static readonly Regex ScriptArg = new Regex(@"-File\s+(?:""(?<p>[^""]+)""|(?<p>[^\s""]+))", RegexOptions.IgnoreCase);

        static Match ScriptMatch(Step s)
        {
            try
            {
                string exe = Path.GetFileNameWithoutExtension(PathUtil.ResolveProgram(s.ProgramPath)) ?? "";
                if (!exe.Equals("powershell", StringComparison.OrdinalIgnoreCase) && !exe.Equals("pwsh", StringComparison.OrdinalIgnoreCase))
                    return null;
            }
            catch (ArgumentException) { return null; }
            var m = ScriptArg.Match(s.Arguments ?? "");
            return m.Success ? m : null;
        }

        // The file a step needs: its program, or for a PowerShell script step the script (as stored).
        public static string TargetOf(Step s)
        {
            var m = ScriptMatch(s);
            return m != null ? m.Groups["p"].Value : s.ProgramPath;
        }

        public static bool IsMissing(Step s)
        {
            return IsMissing(TargetOf(s));
        }

        // Points the step at a new location of its program (or of its script).
        public static void SetTarget(Step s, string stored)
        {
            var m = ScriptMatch(s);
            if (m == null)
            {
                s.ProgramPath = stored;
                return;
            }
            var g = m.Groups["p"];
            int start = g.Index, end = g.Index + g.Length;
            // Replace the quotes too when there were any, and always write it quoted.
            if (start > 0 && s.Arguments[start - 1] == '"') start--;
            if (end < s.Arguments.Length && s.Arguments[end] == '"') end++;
            s.Arguments = s.Arguments.Substring(0, start) + "\"" + stored + "\"" + s.Arguments.Substring(end);
        }

        // True when the path is a file or folder path that doesn't exist.
        // Bare names such as "cmd.exe" are found by Windows itself and never count as missing.
        public static bool IsMissing(string stored)
        {
            string cur = PathUtil.ResolveProgram(stored);
            return cur.Length > 0 && PathUtil.IsRooted(cur) && !PathUtil.Exists(cur);
        }

        // Where the profile expects the program (full path).
        public static string Expected(string stored)
        {
            return PathUtil.ResolveProgram(stored);
        }

        // An existing path the program may have moved to, or null. Checks only exact paths:
        // 1. a move you already confirmed in this launch/check, applied to this program,
        // 2. the same {tools}\... or {app}\... path seen from the folders above (the launcher folder moved),
        // 3. the same path on the launcher's drive (it was saved with another drive letter).
        public string Suggest(string stored)
        {
            string cur = Expected(stored);
            if (cur.Length == 0 || !PathUtil.IsRooted(cur) || PathUtil.Exists(cur)) return null;

            foreach (var m in moves)
            {
                if (cur.StartsWith(m.Key + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    string c = m.Value + cur.Substring(m.Key.Length);
                    if (PathUtil.Exists(c)) return c;
                }
            }

            string rel = PathUtil.PlaceholderRemainder(stored);
            if (!string.IsNullOrEmpty(rel))
            {
                foreach (var root in new[] { PathUtil.ToolsDir, PathUtil.HomeDir })
                    foreach (var anc in PathUtil.Ancestors(root).Skip(1))
                    {
                        string c = SafeIn(anc, rel);
                        if (c != null && PathUtil.Exists(c)) return c;
                    }
            }

            try
            {
                string curRoot = Path.GetPathRoot(cur);
                string homeRoot = Path.GetPathRoot(PathUtil.Full(PathUtil.HomeDir));
                if (!string.IsNullOrEmpty(curRoot) && !string.Equals(curRoot, homeRoot, StringComparison.OrdinalIgnoreCase))
                {
                    string c = homeRoot + cur.Substring(curRoot.Length);
                    if (PathUtil.Exists(c)) return c;
                }
            }
            catch { }
            return null;
        }

        // Remembers a confirmed move, e.g. E:\Tools\HWMonitor\HWMonitor.exe -> E:\Sensors\HWMonitor\HWMonitor.exe
        // becomes "E:\Tools -> E:\Sensors" (the matching end of both paths is left out).
        public void Learn(string expected, string chosen)
        {
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(chosen)) return;
            var a = expected.Split('\\');
            var b = chosen.Split('\\');
            int same = 0;
            while (same < a.Length - 1 && same < b.Length - 1 &&
                   string.Equals(a[a.Length - 1 - same], b[b.Length - 1 - same], StringComparison.OrdinalIgnoreCase)) same++;
            if (same == 0) return;   // different file name: nothing to learn
            string from = string.Join("\\", a, 0, a.Length - same);
            string to = string.Join("\\", b, 0, b.Length - same);
            if (from.Length == 0 || to.Length == 0 || PathUtil.SamePath(from, to)) return;
            moves.RemoveAll(m => PathUtil.SamePath(m.Key, from));
            moves.Insert(0, new KeyValuePair<string, string>(from, to));
        }

        static string SafeIn(string dir, string rel)
        {
            try { return PathUtil.In(dir, rel); }
            catch { return null; }
        }
    }
}
