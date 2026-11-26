using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Xml;
using System.Xml.Linq;

namespace LauncherApp
{
    [DataContract]
    public enum StartMode
    {
        [EnumMember(Value = "immediately")] Immediately = 0,
        [EnumMember(Value = "afterDelay")] AfterDelay = 1,
        [EnumMember(Value = "whenPreviousRunning")] WhenPreviousRunning = 2,
        [EnumMember(Value = "whenPreviousExits")] WhenPreviousExits = 3
    }

    [DataContract]
    public class Step
    {
        public const int DefaultTimeoutSeconds = 60;
        public const int MaxTimeoutSeconds = 86400;

        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "path")] public string ProgramPath { get; set; }
        [DataMember(Name = "arguments")] public string Arguments { get; set; }
        [DataMember(Name = "start")] public StartMode Start { get; set; }
        [DataMember(Name = "delaySeconds")] public int DelaySeconds { get; set; }

        // Optional settings. Left out of profiles.json while they have their default value.
        [DataMember(Name = "timeoutSeconds", EmitDefaultValue = false)] public int TimeoutSeconds { get; set; }
        [DataMember(Name = "runAsAdmin", EmitDefaultValue = false)] public bool RunAsAdmin { get; set; }
        [DataMember(Name = "minimized", EmitDefaultValue = false)] public bool StartMinimized { get; set; }

        // How long a "when previous runs / exits" step waits at most. 0 in the file means the default.
        public int EffectiveTimeout
        {
            get { return TimeoutSeconds > 0 ? Math.Min(TimeoutSeconds, MaxTimeoutSeconds) : DefaultTimeoutSeconds; }
        }

        public bool WaitsForPrevious
        {
            get { return Start == StartMode.WhenPreviousRunning || Start == StartMode.WhenPreviousExits; }
        }

        // The name, or the program's file name when no name was given.
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Name)) return Name;
                try
                {
                    string n = Path.GetFileNameWithoutExtension(PathUtil.Expand(ProgramPath).Trim().Trim('"'));
                    if (!string.IsNullOrEmpty(n)) return n;
                }
                catch (ArgumentException) { }
                return string.IsNullOrWhiteSpace(ProgramPath) ? "(no program)" : ProgramPath;
            }
        }

        public Step Clone()
        {
            return new Step
            {
                Name = Name,
                ProgramPath = ProgramPath,
                Arguments = Arguments,
                Start = Start,
                DelaySeconds = DelaySeconds,
                TimeoutSeconds = TimeoutSeconds,
                RunAsAdmin = RunAsAdmin,
                StartMinimized = StartMinimized
            };
        }
    }

    [DataContract]
    public class Profile
    {
        // Rough time a "when previous runs / exits" wait takes, used only for the estimate in the UI.
        public const int WaitEstimateSeconds = 3;

        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "steps")] public List<Step> Steps { get; set; }

        // Icon name (see Icons.ProfileIcons). Empty = guessed from the profile name.
        [DataMember(Name = "icon", EmitDefaultValue = false)] public string Icon { get; set; }

        // Start only: once every program has started, the launcher lets go of them. They don't count as open,
        // Close all leaves them alone, and the profile is done (for tests and benchmarks that run on their own).
        [DataMember(Name = "startOnly", EmitDefaultValue = false)] public bool StartOnly { get; set; }

        public Profile()
        {
            Steps = new List<Step>();
        }

        public Profile Clone()
        {
            var p = new Profile { Name = Name, Icon = Icon, StartOnly = StartOnly };
            foreach (var s in Steps) p.Steps.Add(s.Clone());
            return p;
        }

        // Start time (seconds after pressing Launch) of every step.
        public int[] StartTimes()
        {
            var times = new int[Steps.Count];
            int t = 0;
            for (int i = 0; i < Steps.Count; i++)
            {
                var s = Steps[i];
                if (s.Start == StartMode.AfterDelay) t += Math.Max(0, s.DelaySeconds);
                else if (s.WaitsForPrevious && i > 0) t += WaitEstimateSeconds;
                times[i] = t;
            }
            return times;
        }

        public int EstimatedSeconds()
        {
            var t = StartTimes();
            return t.Length == 0 ? 0 : t[t.Length - 1];
        }
    }

    [DataContract]
    class ProfileFile
    {
        [DataMember(Name = "profiles")] public List<Profile> Profiles { get; set; }
    }

    public static class ProfileStore
    {
        public static string FilePath
        {
            get { return PathUtil.In(PathUtil.HomeDir, PathUtil.ProfilesFileName); }
        }

        static string BadCopyPath
        {
            get { return FilePath + ".bad"; }
        }

        // Reads profiles.json one entry at a time, so one broken profile or step (for example an
        // unknown "start" value) is left out on its own instead of replacing everything with the defaults.
        public static List<Profile> Load(out string warning)
        {
            warning = null;
            if (!File.Exists(FilePath))
            {
                var d = Defaults();
                try { Save(d); } catch (Exception ex) { warning = "Couldn't create profiles.json: " + ex.Message; }
                return d;
            }

            XElement root;
            try
            {
                using (var fs = JsonFile.OpenRead(FilePath))
                using (var reader = JsonReaderWriterFactory.CreateJsonReader(fs, XmlDictionaryReaderQuotas.Max))
                    root = XElement.Load(reader);
                if (root.Element("profiles") == null || (string)root.Element("profiles").Attribute("type") != "array")
                    throw new FormatException("there is no \"profiles\" list in it");
            }
            catch (Exception ex)
            {
                bool copied = CopyBad();
                warning = "Couldn't read profiles.json (" + ex.Message + ")." +
                          (copied ? " A copy was saved as profiles.json.bad and the" : " The") +
                          " default profiles were loaded.";
                return Defaults();
            }

            var list = new List<Profile>();
            var skipped = new List<string>();
            int pi = 0;
            foreach (var pe in root.Element("profiles").Elements())
            {
                pi++;
                var p = ParseProfile(pe, pi, skipped);
                if (p != null) list.Add(p);
            }

            if (skipped.Count > 0)
            {
                bool copied = CopyBad();
                warning = "Some entries in profiles.json couldn't be read and were left out:\n\n- " +
                          string.Join("\n- ", skipped) + "\n\n" +
                          (copied ? "The original file was copied to profiles.json.bad. " : "") +
                          "The next time profiles are saved, these entries will be removed from profiles.json.";
            }
            return list;
        }

        // One profile from the JSON (as XML). Steps that can't be read are left out and listed in skipped.
        static Profile ParseProfile(XElement pe, int number, List<string> skipped)
        {
            if ((string)pe.Attribute("type") != "object")
            {
                skipped.Add("profile " + number + ": it isn't a profile object");
                return null;
            }

            var stepReader = new DataContractJsonSerializer(typeof(Step));
            string name = (string)pe.Element("name");
            var p = new Profile { Name = string.IsNullOrWhiteSpace(name) ? "Untitled" : name };
            var iconEl = pe.Element("icon");
            string iconType = iconEl == null ? null : (string)iconEl.Attribute("type");
            if (iconEl != null && (iconType == null || iconType == "string")) p.Icon = (string)iconEl;
            var startOnlyEl = pe.Element("startOnly");
            if (startOnlyEl != null && (string)startOnlyEl.Attribute("type") == "boolean") p.StartOnly = (bool)startOnlyEl;

            var stepsEl = pe.Element("steps");
            if (stepsEl != null && (string)stepsEl.Attribute("type") == "array")
            {
                int si = 0;
                foreach (var se in stepsEl.Elements())
                {
                    si++;
                    try
                    {
                        if ((string)se.Attribute("type") != "object") throw new FormatException("it isn't a step object");
                        Step s;
                        using (var r = se.CreateReader())
                            s = (Step)stepReader.ReadObject(r, false);
                        if (s == null) throw new FormatException("it is empty");
                        if (!Enum.IsDefined(typeof(StartMode), s.Start)) throw new FormatException("unknown start mode");
                        if (s.Name == null) s.Name = "";
                        if (s.ProgramPath == null) s.ProgramPath = "";
                        if (s.Arguments == null) s.Arguments = "";
                        if (s.DelaySeconds < 0) s.DelaySeconds = 0;
                        if (s.TimeoutSeconds < 0) s.TimeoutSeconds = 0;
                        p.Steps.Add(s);
                    }
                    catch (Exception ex)
                    {
                        skipped.Add("\"" + p.Name + "\", step " + si + ": " + Innermost(ex));
                    }
                }
            }
            else if (stepsEl != null && (string)stepsEl.Attribute("type") != "null")
            {
                skipped.Add("\"" + p.Name + "\": \"steps\" isn't a list");
            }
            return p;
        }

        // ---- export / import ----

        // Exported profiles go here, on the stick next to profiles.json.
        public static string ExportDir
        {
            get { return PathUtil.In(PathUtil.HomeDir, "Exported profiles"); }
        }

        // Writes one profile to "Exported profiles\<name>.json" and returns the file's path.
        public static string Export(Profile p)
        {
            Directory.CreateDirectory(ExportDir);
            string name = new string(((p.Name ?? "").Trim()).Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
            if (name.Length == 0) name = "profile";
            string path = PathUtil.In(ExportDir, name + ".json");
            JsonFile.WriteAtomic(path, ToJson(p));
            return path;
        }

        // Reads profiles from an exported file (one profile) or a whole profiles.json (all of them).
        // Throws when the file can't be read at all; entries that can't be read are listed in warning.
        public static List<Profile> ReadFile(string path, out string warning)
        {
            warning = null;
            XElement root;
            using (var fs = JsonFile.OpenRead(path))
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(fs, XmlDictionaryReaderQuotas.Max))
                root = XElement.Load(reader);

            var list = new List<Profile>();
            var skipped = new List<string>();
            var many = root.Element("profiles");
            if (many != null && (string)many.Attribute("type") == "array")
            {
                int i = 0;
                foreach (var pe in many.Elements())
                {
                    var p = ParseProfile(pe, ++i, skipped);
                    if (p != null) list.Add(p);
                }
            }
            else if (root.Element("steps") != null)
            {
                var p = ParseProfile(root, 1, skipped);
                if (p != null) list.Add(p);
            }
            else
            {
                throw new FormatException("it isn't an exported profile or a profiles.json");
            }
            if (skipped.Count > 0)
                warning = "Some entries couldn't be read and were left out:\n\n- " + string.Join("\n- ", skipped);
            return list;
        }

        // "Bench setup" -> "Bench setup (2)" when that name is taken.
        public static string FreeName(string name, IEnumerable<Profile> existing)
        {
            var names = new HashSet<string>(existing.Select(p => (p.Name ?? "").Trim()), StringComparer.OrdinalIgnoreCase);
            name = (name ?? "").Trim();
            if (name.Length == 0) name = "Untitled";
            if (!names.Contains(name)) return name;
            for (int i = 2; ; i++)
                if (!names.Contains(name + " (" + i + ")")) return name + " (" + i + ")";
        }

        static bool CopyBad()
        {
            try { File.Copy(FilePath, BadCopyPath, true); return true; }
            catch { return false; }
        }

        static string Innermost(Exception ex)
        {
            bool fromSerializer = ex is SerializationException;
            while (ex.InnerException != null) ex = ex.InnerException;
            if (fromSerializer && (ex is FormatException || ex is OverflowException))
                return "a value has the wrong type (for example text where a number belongs)";
            return ex.Message;
        }

        // Writes to a temporary file first and then swaps it in (see JsonFile.WriteAtomic).
        public static void Save(List<Profile> list)
        {
            JsonFile.WriteAtomic(FilePath, JsonFile.ToJson(new ProfileFile { Profiles = list }));
        }

        public static string ToJson(Profile p)
        {
            return JsonFile.ToJson(p);
        }

        static Step S(string name, string path, StartMode mode, int delay, string args)
        {
            return new Step { Name = name, ProgramPath = path, Arguments = args, Start = mode, DelaySeconds = delay };
        }

        public static List<Profile> Defaults()
        {
            var stress = new Profile { Name = "Prime95 stress test" };
            stress.Steps.Add(S("HWMonitor", @"{tools}\HWMonitor\HWMonitor.exe", StartMode.Immediately, 0, ""));
            stress.Steps.Add(S("Core Temp", @"{tools}\CoreTemp\Core Temp.exe", StartMode.WhenPreviousRunning, 0, ""));
            stress.Steps.Add(S("Prime95", @"{tools}\Prime95\prime95.exe", StartMode.WhenPreviousRunning, 0, ""));

            var bench = new Profile { Name = "Bench setup" };
            bench.Steps.Add(S("CPU-Z", @"{tools}\CPU-Z\cpuz_x64.exe", StartMode.Immediately, 0, ""));
            bench.Steps.Add(S("GPU-Z", @"{tools}\GPU-Z\GPU-Z.exe", StartMode.AfterDelay, 2, ""));
            bench.Steps.Add(S("HWiNFO64", @"{tools}\HWiNFO\HWiNFO64.exe", StartMode.AfterDelay, 3, ""));
            bench.Steps.Add(S("Core Temp", @"{tools}\CoreTemp\Core Temp.exe", StartMode.AfterDelay, 2, ""));
            bench.Steps.Add(S("HWMonitor", @"{tools}\HWMonitor\HWMonitor.exe", StartMode.AfterDelay, 2, ""));
            bench.Steps.Add(S("CrystalDiskInfo", @"{tools}\CrystalDiskInfo\DiskInfo64.exe", StartMode.AfterDelay, 5, ""));
            bench.Steps.Add(S("Prime95", @"{tools}\Prime95\prime95.exe", StartMode.WhenPreviousRunning, 0, ""));
            bench.Steps.Add(S("FurMark", @"{tools}\FurMark\FurMark.exe", StartMode.AfterDelay, 10, ""));
            bench.Steps.Add(S("Cinebench", @"{tools}\Cinebench\Cinebench.exe", StartMode.AfterDelay, 5, ""));
            bench.Steps.Add(S("Task Manager", "taskmgr.exe", StartMode.AfterDelay, 1, ""));

            var net = new Profile { Name = "Network check" };
            net.Steps.Add(S("IP configuration", "cmd.exe", StartMode.Immediately, 0, "/k ipconfig /all"));
            net.Steps.Add(S("Ping test", "cmd.exe", StartMode.AfterDelay, 2, "/k ping 8.8.8.8"));

            return new List<Profile> { stress, bench, net };
        }
    }
}
