using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LauncherApp
{
    // Lists programs to pick from, made for the keyboard: one list of the current folder with a
    // clickable folder path, Name / Where / Size / Version columns, and likely matches (when looking for a missing
    // program) pinned on top. Type to jump to a name, Enter opens a folder or uses the program, Backspace goes up,
    // Alt+Left goes back, Ctrl+F lists everything 2 levels down, Esc clears / cancels.
    // Used instead of the Windows file dialog, which remembers recent files and folders in the PC's registry.
    class ProgramPicker : Form
    {
        const int MaxFiles = 20000;
        const int MatchDepth = 2;   // how deep likely matches and Ctrl+F look

        enum Kind { Up, Drive, Dir, File, Header, Gap }

        class Entry
        {
            public Kind Kind;
            public string Name = "", Path = "", Where = "", Size = "", Version = "";
            public int Score;          // likely match (files)
            public bool HasMatch;      // a likely match is inside (folders)
        }

        public string SelectedPath { get; private set; }

        readonly Func<string, bool> include;   // which files are listed (programs by default)
        readonly string noun;                  // "programs", "profile files"
        readonly string lookingName;           // file name of the program being looked for, or null
        readonly string lookingFolder;         // name of the folder it used to be in, or null
        readonly string preselect;
        readonly string self = Application.ExecutablePath;

        string folder;                         // current folder; "" = the list of drives
        readonly Stack<string> history = new Stack<string>();
        List<Entry> local = new List<Entry>();
        List<Entry> matches = new List<Entry>();
        List<Entry> found = new List<Entry>();
        bool findMode, userMoved;
        string jump = "";
        CancellationTokenSource matchScan, findScan;

        readonly ListBox list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, BorderStyle = BorderStyle.None };
        readonly Panel columns = new Panel { Dock = DockStyle.Top };
        readonly FlowLayoutPanel crumbs = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Anchor = AnchorStyles.Left };
        readonly Panel jumpBox = new Panel();
        readonly Label info = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        readonly Button useBtn = new Button { Text = "Use this one", AutoSize = true, Tag = Theme.Primary };
        readonly Panel keysBar = new Panel { Dock = DockStyle.Bottom };

        // lookingFor: the full path where a missing program used to be (its matches go first).
        // title / include / noun let the same window pick other files (Import uses it for profile files).
        public ProgramPicker(string startFolder, string currentFullPath, string lookingFor = null,
                             string title = null, Func<string, bool> include = null, string noun = null)
        {
            this.include = include ?? FileWalker.IsProgram;
            this.noun = noun ?? "programs";
            if (!string.IsNullOrWhiteSpace(lookingFor)) ProgramMatch.Split(lookingFor, out lookingName, out lookingFolder);
            preselect = currentFullPath;

            Text = title ?? (lookingName != null ? "Find " + lookingName : "Choose a program");
            Font = Theme.Ui;
            ClientSize = Theme.S(860, 600);
            MinimumSize = Theme.S(560, 420);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            KeyPreview = true;

            keysBar.Height = Theme.S(34);
            keysBar.Paint += PaintKeys;
            Controls.Add(keysBar);

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = Theme.Pad(18, 12, 18, 10) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(t);
            t.BringToFront();

            t.Controls.Add(new Label { Text = Theme.Up(Text), AutoSize = true, Font = Theme.Heading, ForeColor = Theme.Terminal ? Theme.Accent : Theme.Bright, Margin = Theme.Pad(0, 0, 0, 2) }, 0, 0);
            t.Controls.Add(new Label
            {
                Text = lookingName != null
                    ? "Likely matches from the folders below are pinned on top. Start typing to jump to a name; Enter opens a folder or uses the program."
                    : "Start typing to jump to a name; Enter opens a folder or uses the " + (this.noun == "programs" ? "program" : "file") + ". Backspace goes up a folder.",
                AutoSize = true, MaximumSize = new Size(Theme.S(820), 0), ForeColor = Theme.Muted, Font = Theme.Small, Margin = Theme.Pad(0, 0, 0, 8)
            }, 0, 1);

            // Folder path, clickable, with Tools folder / Other folder on the right
            var pathBar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Theme.Pad(0, 0, 0, 8), Padding = Theme.Pad(6, 3, 6, 3), BackColor = Theme.Field };
            pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathBar.Paint += (s, e) => { using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, pathBar.Width - 1, pathBar.Height - 1); };
            pathBar.Controls.Add(crumbs, 0, 0);
            var links = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Anchor = AnchorStyles.Right };
            var tools = new Button { Text = "Tools folder", Tag = Theme.Link, AutoSize = true, Margin = new Padding(0), Font = Theme.Small };
            tools.Click += (s, e) => { Navigate(PathUtil.ToolsDir, true, null); list.Focus(); };
            var other = new Button { Text = "Other folder...", Tag = Theme.Link, AutoSize = true, Margin = Theme.Pad(4, 0, 0, 0), Font = Theme.Small };
            other.Click += (s, e) => BrowseFolder();
            links.Controls.Add(tools);
            links.Controls.Add(other);
            pathBar.Controls.Add(links, 1, 0);
            t.Controls.Add(pathBar, 0, 2);

            // The list, with its column titles
            var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0), BackColor = Theme.Field };
            frame.Paint += (s, e) => { using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, frame.Width - 1, frame.Height - 1); };
            columns.Height = Theme.S(24);
            columns.BackColor = Theme.Field;
            columns.Paint += PaintColumns;
            list.ItemHeight = Theme.S(28);
            list.BackColor = Theme.Field;
            list.DrawItem += DrawEntry;
            list.SelectedIndexChanged += (s, e) => SkipNonItems();
            list.MouseDown += (s, e) => userMoved = true;
            list.DoubleClick += (s, e) => OpenOrUse();
            list.Resize += (s, e) => { columns.Invalidate(); list.Invalidate(); };
            frame.Controls.Add(list);
            frame.Controls.Add(columns);
            t.Controls.Add(frame, 0, 3);

            // Bottom: the jump box, what is happening, Cancel and Use / Open
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, Margin = Theme.Pad(0, 8, 0, 0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            jumpBox.Size = Theme.S(240, 30);
            jumpBox.Margin = Theme.Pad(0, 0, 10, 0);
            jumpBox.Paint += PaintJump;
            bottom.Controls.Add(jumpBox, 0, 0);
            info.Font = Theme.Small;
            info.MaximumSize = new Size(Theme.S(360), 0);
            bottom.Controls.Add(info, 1, 0);
            var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = Theme.S(90, 32), Margin = Theme.Pad(0, 0, 8, 0) };
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            bottom.Controls.Add(cancel, 2, 0);
            useBtn.MinimumSize = Theme.S(120, 32);
            useBtn.Margin = new Padding(0);
            useBtn.Click += (s, e) => OpenOrUse();
            bottom.Controls.Add(useBtn, 3, 0);
            t.Controls.Add(bottom, 0, 4);

            Theme.ApplyToForm(this);
            Load += (s, e) =>
            {
                string start = startFolder;
                if (string.IsNullOrEmpty(start) || !Directory.Exists(PathUtil.Full(start))) start = PathUtil.HomeDir;
                Navigate(start, false, null);
                ActiveControl = list;   // keys go to the list from the start
            };
            Shown += (s, e) => list.Focus();
            FormClosed += (s, e) => { Cancel(ref matchScan); Cancel(ref findScan); };
        }

        static void Cancel(ref CancellationTokenSource cts)
        {
            if (cts != null) { try { cts.Cancel(); } catch { } cts = null; }
        }

        // ---- keyboard ----

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (ActiveControl == list)
            {
                switch (keyData)
                {
                    case Keys.Enter: OpenOrUse(); return true;
                    case Keys.Escape:
                        if (jump.Length > 0) SetJump("");
                        else if (findMode) SetFindMode(false);
                        else DialogResult = DialogResult.Cancel;
                        return true;
                    case Keys.Back:
                        if (jump.Length > 0) SetJump(jump.Substring(0, jump.Length - 1));
                        else GoUp();
                        return true;
                    case Keys.Alt | Keys.Up: GoUp(); return true;
                    case Keys.Alt | Keys.Left: GoBack(); return true;
                    case Keys.Control | Keys.F: SetFindMode(!findMode); return true;
                    case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End:
                        userMoved = true;
                        if (jump.Length > 0 && !findMode) { jump = ""; jumpBox.Invalidate(); UpdateInfo(); list.Invalidate(); }
                        break;
                }
            }
            else if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            // Letters always go to the jump box, also when a button has the focus.
            if (!char.IsControl(e.KeyChar) && !(e.KeyChar == ' ' && ActiveControl is Button))
            {
                if (ActiveControl != list) list.Focus();
                SetJump(jump + e.KeyChar);
                e.Handled = true;
            }
        }

        // Typing: jumps to the first name that starts with the text (else contains it). In find mode it filters.
        void SetJump(string text)
        {
            jump = text;
            userMoved = true;
            if (findMode)
            {
                Fill(null);
            }
            else if (jump.Length > 0)
            {
                int best = -1;
                for (int pass = 0; pass < 2 && best < 0; pass++)
                    for (int i = 0; i < list.Items.Count; i++)
                    {
                        var en = (Entry)list.Items[i];
                        if (en.Kind == Kind.Header || en.Kind == Kind.Gap || en.Kind == Kind.Up) continue;
                        bool hit = pass == 0 ? en.Name.StartsWith(jump, StringComparison.OrdinalIgnoreCase)
                                             : en.Name.IndexOf(jump, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (hit) { best = i; break; }
                    }
                if (best >= 0) list.SelectedIndex = best;
            }
            jumpBox.Invalidate();
            list.Invalidate();
            UpdateInfo();
        }

        // ---- navigation ----

        void OpenOrUse()
        {
            var en = list.SelectedItem as Entry;
            if (en == null) return;
            switch (en.Kind)
            {
                case Kind.Up: GoUp(); break;
                case Kind.Drive:
                case Kind.Dir: Navigate(en.Path, true, null); break;
                case Kind.File:
                    SelectedPath = en.Path;
                    DialogResult = DialogResult.OK;
                    break;
            }
        }

        void GoUp()
        {
            if (string.IsNullOrEmpty(folder)) return;
            string name = System.IO.Path.GetFileName(PathUtil.Trim(folder));
            DirectoryInfo parent = null;
            try { parent = Directory.GetParent(PathUtil.Full(folder)); } catch { }
            if (parent == null) Navigate("", true, PathUtil.Trim(folder));          // from a drive to the list of drives
            else Navigate(parent.FullName, true, PathUtil.Trim(folder));            // the folder you came from stays selected
        }

        void GoBack()
        {
            if (history.Count > 0) Navigate(history.Pop(), false, PathUtil.Trim(folder ?? ""));
        }

        void BrowseFolder()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Folder to list " + noun + " from", ShowNewFolderButton = false })
            {
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(PathUtil.Full(folder))) dlg.SelectedPath = PathUtil.Full(folder);
                if (dlg.ShowDialog(this) == DialogResult.OK) Navigate(dlg.SelectedPath, true, null);
            }
            list.Focus();
        }

        // Shows a folder ("" = the drives). selectPath: put the cursor on this entry (e.g. the folder you came from).
        void Navigate(string path, bool remember, string selectPath)
        {
            path = path == "" ? "" : PathUtil.Trim(path);
            if (remember && folder != null && !PathUtil.SamePath(folder, path)) history.Push(folder);
            Cancel(ref matchScan);
            Cancel(ref findScan);
            folder = path;
            findMode = false;
            jump = "";
            userMoved = selectPath != null;   // came back from a folder: keep the cursor on it, not on a match
            matches = new List<Entry>();
            found = new List<Entry>();
            local = ListFolder(path);
            BuildCrumbs();

            string select = selectPath;
            if (select == null && preselect != null && local.Any(en => PathUtil.SamePath(en.Path, preselect))) select = preselect;
            Fill(select);
            jumpBox.Invalidate();

            if (lookingName != null && path.Length > 0) StartMatchScan(path);
        }

        List<Entry> ListFolder(string path)
        {
            var result = new List<Entry>();
            if (path.Length == 0)
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    string where = "";
                    try { if (d.IsReady) where = (string.IsNullOrEmpty(d.VolumeLabel) ? d.DriveType.ToString() : d.VolumeLabel) + (d.DriveType == DriveType.Removable ? " (stick)" : ""); }
                    catch { }
                    result.Add(new Entry { Kind = Kind.Drive, Name = PathUtil.Trim(d.Name), Path = d.Name, Where = where });
                }
                return result;
            }

            result.Add(new Entry { Kind = Kind.Up, Name = "..", Path = "" });
            string skip = PathUtil.Trim(PortableEnv.DataDir);
            try
            {
                var dir = new DirectoryInfo(PathUtil.Full(path));
                foreach (var d in dir.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if ((d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    if (PathUtil.SamePath(d.FullName, skip)) continue;
                    result.Add(new Entry { Kind = Kind.Dir, Name = d.Name, Path = d.FullName, Where = "folder" });
                }
                foreach (var f in dir.EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (!include(f.FullName) || string.Equals(f.FullName, self, StringComparison.OrdinalIgnoreCase)) continue;
                    result.Add(FileEntry(f.FullName, ""));
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is System.Security.SecurityException)
            {
                info.Text = "Can't open this folder: " + ex.Message;
            }
            return result;
        }

        Entry FileEntry(string full, string where)
        {
            var en = new Entry { Kind = Kind.File, Name = System.IO.Path.GetFileName(full), Path = full, Where = where };
            try
            {
                var fi = new FileInfo(full);
                en.Size = PortableEnv.FormatSize(fi.Length);
                if (fi.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    string v = FileVersionInfo.GetVersionInfo(full).FileVersion;
                    en.Version = string.IsNullOrWhiteSpace(v) ? "" : v.Trim();
                }
            }
            catch { }
            en.Score = ProgramMatch.Score(full, lookingName, lookingFolder);
            return en;
        }

        // Looks for likely matches up to 2 folders below the current one, in the background, and pins them on top.
        async void StartMatchScan(string path)
        {
            var cts = matchScan = new CancellationTokenSource();
            string name = lookingName, oldFolder = lookingFolder;
            List<Entry> result;
            try
            {
                result = await Task.Run(() =>
                {
                    var files = FileWalker.Walk(path, f => include(f) && !string.Equals(f, self, StringComparison.OrdinalIgnoreCase)
                                                           && ProgramMatch.Score(f, name, oldFolder) > 0, MatchDepth, 200, cts.Token);
                    string prefix = PathUtil.Trim(path) + "\\";
                    return files.Select(f =>
                    {
                        string rel = f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? System.IO.Path.GetDirectoryName(f.Substring(prefix.Length)) : "";
                        return FileEntry(f, string.IsNullOrEmpty(rel) ? "(here)" : rel);
                    })
                    .OrderByDescending(en => en.Score).ThenBy(en => en.Path.Count(ch => ch == '\\')).ThenBy(en => en.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(8).ToList();
                }, cts.Token);
            }
            catch (OperationCanceledException) { return; }
            if (cts != matchScan || IsDisposed) return;
            matches = result;
            foreach (var d in local.Where(en => en.Kind == Kind.Dir))
                d.HasMatch = matches.Any(m => m.Path.StartsWith(PathUtil.Trim(d.Path) + "\\", StringComparison.OrdinalIgnoreCase));
            var keep = list.SelectedItem as Entry;
            // The cursor goes to the best match unless you already moved it.
            Fill(!userMoved && matches.Count > 0 ? matches[0].Path : keep != null ? keep.Path : null);
        }

        // Ctrl+F: every program in this folder and up to 2 levels below, filtered by what you type.
        async void SetFindMode(bool on)
        {
            if (on == findMode || string.IsNullOrEmpty(folder)) return;
            findMode = on;
            jump = "";
            jumpBox.Invalidate();
            Cancel(ref findScan);
            if (!on)
            {
                Fill(null);
                return;
            }
            found = new List<Entry>();
            Fill(null);
            info.Text = "Looking in this folder and 2 levels below...";
            var cts = findScan = new CancellationTokenSource();
            string path = folder;
            List<Entry> result;
            try
            {
                result = await Task.Run(() =>
                {
                    var files = FileWalker.Walk(path, f => include(f) && !string.Equals(f, self, StringComparison.OrdinalIgnoreCase), MatchDepth, MaxFiles, cts.Token);
                    string prefix = PathUtil.Trim(path) + "\\";
                    return files.Select(f =>
                    {
                        string rel = f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? System.IO.Path.GetDirectoryName(f.Substring(prefix.Length)) : "";
                        long size = 0;
                        try { size = new FileInfo(f).Length; } catch { }
                        return new Entry { Kind = Kind.File, Name = System.IO.Path.GetFileName(f), Path = f, Where = string.IsNullOrEmpty(rel) ? "(here)" : rel,
                                           Size = PortableEnv.FormatSize(size), Score = ProgramMatch.Score(f, lookingName, lookingFolder) };
                    })
                    .OrderByDescending(en => en.Score).ThenBy(en => en.Name, StringComparer.OrdinalIgnoreCase).ToList();
                }, cts.Token);
            }
            catch (OperationCanceledException) { return; }
            if (cts != findScan || IsDisposed || !findMode) return;
            found = result;
            Fill(null);
        }

        // ---- the list ----

        void Fill(string selectPath)
        {
            var items = new List<Entry>();
            if (findMode)
            {
                items.Add(new Entry { Kind = Kind.Header, Name = "All " + noun + " in this folder and 2 levels below" + (jump.Length > 0 ? ", with \"" + jump + "\" in the name" : "") });
                items.AddRange(found.Where(en => jump.Length == 0 || en.Name.IndexOf(jump, StringComparison.OrdinalIgnoreCase) >= 0 || en.Where.IndexOf(jump, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            else
            {
                if (matches.Count > 0)
                {
                    items.Add(new Entry { Kind = Kind.Header, Name = "Likely matches below this folder" });
                    items.AddRange(matches);
                    items.Add(new Entry { Kind = Kind.Gap });
                }
                items.AddRange(local);
            }

            list.BeginUpdate();
            list.Items.Clear();
            list.Items.AddRange(items.Cast<object>().ToArray());
            list.EndUpdate();

            int pick = -1;
            if (selectPath != null)
                pick = items.FindIndex(en => (en.Kind == Kind.File || en.Kind == Kind.Dir || en.Kind == Kind.Drive) && PathUtil.SamePath(en.Path, selectPath));
            if (pick < 0) pick = items.FindIndex(en => en.Kind == Kind.File || en.Kind == Kind.Dir || en.Kind == Kind.Drive);
            if (pick < 0) pick = items.FindIndex(en => en.Kind == Kind.Up);
            if (pick >= 0) list.SelectedIndex = pick;
            UpdateInfo();
            UpdateUseButton();
        }

        int lastIndex = -1;

        // Headers and gaps can't be selected: the cursor jumps over them.
        void SkipNonItems()
        {
            int i = list.SelectedIndex;
            if (i >= 0)
            {
                var en = (Entry)list.Items[i];
                if (en.Kind == Kind.Header || en.Kind == Kind.Gap)
                {
                    int dir = i >= lastIndex ? 1 : -1;
                    int j = i + dir;
                    while (j >= 0 && j < list.Items.Count && (((Entry)list.Items[j]).Kind == Kind.Header || ((Entry)list.Items[j]).Kind == Kind.Gap)) j += dir;
                    if (j < 0 || j >= list.Items.Count) j = lastIndex;
                    if (j >= 0 && j < list.Items.Count) { list.SelectedIndex = j; return; }
                }
            }
            lastIndex = list.SelectedIndex;
            UpdateUseButton();
        }

        void UpdateUseButton()
        {
            var en = list.SelectedItem as Entry;
            bool file = en != null && en.Kind == Kind.File;
            useBtn.Text = file ? (lookingName != null ? "Use this one" : "OK") : "Open folder";
        }

        void UpdateInfo()
        {
            Color color = Theme.Muted;
            string text;
            if (findMode)
            {
                int shown = list.Items.Cast<Entry>().Count(en => en.Kind == Kind.File);
                text = found.Count == 0 ? "No " + noun + " in this folder or 2 levels below." : (jump.Length > 0 ? shown + " of " : "") + found.Count + " " + noun + " · Esc goes back to the folder";
            }
            else if (jump.Length > 0)
            {
                var items = list.Items.Cast<Entry>().Where(en => en.Kind == Kind.File || en.Kind == Kind.Dir || en.Kind == Kind.Drive).ToList();
                int starts = items.Count(en => en.Name.StartsWith(jump, StringComparison.OrdinalIgnoreCase));
                int contains = items.Count(en => en.Name.IndexOf(jump, StringComparison.OrdinalIgnoreCase) >= 0);
                if (contains == 0) { text = "Nothing here has \"" + jump + "\" in its name. Ctrl+F looks in the folders below."; color = Theme.Warning; }
                else text = starts > 0 ? starts + (starts == 1 ? " name starts" : " names start") + " with \"" + jump + "\"" : contains + " name(s) contain \"" + jump + "\"";
            }
            else if (string.IsNullOrEmpty(folder))
            {
                text = "Pick a drive.";
            }
            else
            {
                int dirs = local.Count(en => en.Kind == Kind.Dir), files = local.Count(en => en.Kind == Kind.File);
                text = dirs + (dirs == 1 ? " folder · " : " folders · ") + files + " " + (files == 1 ? noun.TrimEnd('s') : noun) +
                       (lookingName != null ? " · " + (matches.Count == 0 ? "no likely match below" : matches.Count + " likely match" + (matches.Count == 1 ? "" : "es") + " below") : "") +
                       " · Ctrl+F looks 2 levels down";
                if (lookingName != null && matches.Count == 0 && files == 0 && dirs == 0) color = Theme.Warning;
            }
            info.Text = text;
            info.ForeColor = color;
        }

        // ---- the folder path ----

        void BuildCrumbs()
        {
            crumbs.SuspendLayout();
            foreach (Control c in crumbs.Controls.Cast<Control>().ToList()) { crumbs.Controls.Remove(c); c.Dispose(); }
            var parts = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("This PC", "") };
            if (!string.IsNullOrEmpty(folder))
            {
                var stack = new Stack<KeyValuePair<string, string>>();
                try
                {
                    for (var d = new DirectoryInfo(PathUtil.Full(folder)); d != null; d = d.Parent)
                        stack.Push(new KeyValuePair<string, string>(d.Parent == null ? PathUtil.Trim(d.FullName) : d.Name, d.FullName));
                }
                catch { }
                while (stack.Count > 0) parts.Add(stack.Pop());
            }
            // Long paths: "This PC › … › last 4 folders" (… goes to the folder above those).
            if (parts.Count > 6)
            {
                var hidden = parts[parts.Count - 5];
                parts = new List<KeyValuePair<string, string>> { parts[0], new KeyValuePair<string, string>("…", hidden.Value) }
                        .Concat(parts.Skip(parts.Count - 4)).ToList();
            }
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                bool last = i == parts.Count - 1;
                var b = new Button { Text = part.Key, Tag = Theme.Link, AutoSize = true, Margin = new Padding(0), Font = last ? Theme.UiBold : Theme.Ui, TabStop = false };
                b.Click += (s, e) => { Navigate(part.Value, true, null); list.Focus(); };
                crumbs.Controls.Add(b);
                if (!last) crumbs.Controls.Add(new Label { Text = "›", AutoSize = true, ForeColor = Theme.Disabled, Margin = Theme.Pad(0, 6, 0, 0) });
            }
            Theme.Apply(crumbs);
            foreach (Control c in crumbs.Controls)
                if (c is Button)
                {
                    c.BackColor = Theme.Field;
                    if (c == crumbs.Controls[crumbs.Controls.Count - 1]) c.ForeColor = Theme.Bright;
                }
            crumbs.ResumeLayout();
        }

        // ---- drawing ----

        // Column positions from the right: Where, Size, Version.
        void ColumnX(int width, out int whereX, out int sizeX, out int versionX)
        {
            versionX = width - Theme.S(100);
            sizeX = versionX - Theme.S(84);
            whereX = sizeX - Theme.S(150);
        }

        void PaintColumns(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = list.ClientSize.Width, whereX, sizeX, versionX;
            ColumnX(w, out whereX, out sizeX, out versionX);
            const TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            int h = columns.Height;
            TextRenderer.DrawText(g, Theme.Up("Name"), Theme.Small, new Rectangle(Theme.S(36), 0, whereX - Theme.S(36), h), Theme.Muted, f);
            TextRenderer.DrawText(g, Theme.Up("Where"), Theme.Small, new Rectangle(whereX, 0, sizeX - whereX, h), Theme.Muted, f);
            TextRenderer.DrawText(g, Theme.Up("Size"), Theme.Small, new Rectangle(sizeX, 0, versionX - sizeX - Theme.S(10), h), Theme.Muted, f | TextFormatFlags.Right);
            TextRenderer.DrawText(g, Theme.Up("Version"), Theme.Small, new Rectangle(versionX, 0, w - versionX - Theme.S(10), h), Theme.Muted, f | TextFormatFlags.Right);
            using (var pen = new Pen(Theme.Divider)) g.DrawLine(pen, 0, h - 1, columns.Width, h - 1);
        }

        Color CursorBack { get { return Theme.Terminal ? Theme.Accent : Theme.IsDark ? Color.FromArgb(0x2F, 0x5A, 0x3C) : Color.FromArgb(0xCD, 0xE9, 0xD4); } }
        Color CursorText { get { return Theme.Terminal ? Theme.Back : Theme.Bright; } }

        void DrawEntry(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var en = (Entry)list.Items[e.Index];
            var g = e.Graphics;
            var b = e.Bounds;
            bool sel = (e.State & DrawItemState.Selected) != 0 && en.Kind != Kind.Header && en.Kind != Kind.Gap;
            using (var bg = new SolidBrush(sel ? CursorBack : Theme.Field)) g.FillRectangle(bg, b);
            const TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

            if (en.Kind == Kind.Header)
            {
                TextRenderer.DrawText(g, Theme.Up(en.Name), Theme.Small, new Rectangle(b.X + Theme.S(12), b.Y, b.Width - Theme.S(24), b.Height), Theme.Accent, f | TextFormatFlags.EndEllipsis);
                return;
            }
            if (en.Kind == Kind.Gap)
            {
                using (var pen = new Pen(Theme.Divider)) g.DrawLine(pen, b.X + Theme.S(12), b.Y + b.Height / 2, b.Right - Theme.S(12), b.Y + b.Height / 2);
                return;
            }

            int whereX, sizeX, versionX;
            ColumnX(b.Width, out whereX, out sizeX, out versionX);
            Color text = sel ? CursorText : Theme.Text, dim = sel ? CursorText : Theme.Muted;

            // icon
            var ir = new RectangleF(b.X + Theme.S(12), b.Y + (b.Height - Theme.S(16)) / 2f, Theme.S(16), Theme.S(16));
            DrawKindIcon(g, en.Kind, ir, sel);

            // name, with the typed letters highlighted
            int x = b.X + Theme.S(36);
            int nameRight = whereX - Theme.S(10);
            Font nameFont = en.Score > 0 || en.Kind == Kind.Drive ? Theme.UiBold : Theme.Ui;
            const TextFormatFlags m = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            int hit = jump.Length > 0 && !findMode ? en.Name.IndexOf(jump, StringComparison.OrdinalIgnoreCase) : -1;
            if (hit >= 0)
            {
                string before = en.Name.Substring(0, hit), mid = en.Name.Substring(hit, jump.Length);
                int bw = before.Length == 0 ? 0 : TextRenderer.MeasureText(g, before, nameFont, Size.Empty, m).Width;
                int mw = TextRenderer.MeasureText(g, mid, nameFont, Size.Empty, m).Width;
                using (var hb = new SolidBrush(Theme.StatusBusy)) g.FillRectangle(hb, x + bw, b.Y + Theme.S(5), mw, b.Height - Theme.S(10));
                TextRenderer.DrawText(g, en.Name, nameFont, new Rectangle(x, b.Y, nameRight - x, b.Height), text, f | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, mid, nameFont, new Point(x + bw, b.Y + (b.Height - nameFont.Height) / 2), Theme.Back, m);
            }
            else
            {
                TextRenderer.DrawText(g, en.Name, nameFont, new Rectangle(x, b.Y, nameRight - x, b.Height), text, f | TextFormatFlags.EndEllipsis);
            }

            // "match" / "match inside" tag after the name
            string tag = en.Kind == Kind.File && en.Score > 0 ? "match" : en.Kind == Kind.Dir && en.HasMatch ? "match inside" : null;
            if (tag != null)
            {
                int nw = TextRenderer.MeasureText(g, en.Name, nameFont, Size.Empty, m).Width;
                int tw = TextRenderer.MeasureText(g, Theme.Up(tag), Theme.Small, Size.Empty, m).Width + Theme.S(12);
                int tx = x + nw + Theme.S(8);
                if (tx + tw < nameRight)
                {
                    var tr = new Rectangle(tx, b.Y + (b.Height - Theme.S(18)) / 2, tw, Theme.S(18));
                    using (var tb = new SolidBrush(sel ? Theme.Back : Theme.BarFill)) g.FillRectangle(tb, tr);
                    TextRenderer.DrawText(g, Theme.Up(tag), Theme.Small, tr, Theme.BarText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }

            TextRenderer.DrawText(g, en.Where, Theme.Small, new Rectangle(whereX, b.Y, sizeX - whereX - Theme.S(6), b.Height), dim, f | TextFormatFlags.PathEllipsis);
            TextRenderer.DrawText(g, en.Size, Theme.MonoSmall, new Rectangle(sizeX, b.Y, versionX - sizeX - Theme.S(10), b.Height), dim, f | TextFormatFlags.Right);
            TextRenderer.DrawText(g, en.Version, Theme.MonoSmall, new Rectangle(versionX, b.Y, b.Right - versionX - Theme.S(10), b.Height), dim, f | TextFormatFlags.Right | TextFormatFlags.EndEllipsis);
        }

        static void DrawKindIcon(Graphics g, Kind kind, RectangleF r, bool selected)
        {
            var state = g.Save();
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 24f, r.Height / 24f);
            Color folderColor = Theme.Terminal ? Theme.Accent : Color.FromArgb(0xD2, 0x99, 0x22);
            Color fileColor = Theme.Terminal ? Theme.BarText : Theme.IsDark ? Color.FromArgb(0x79, 0xA8, 0xF0) : Color.FromArgb(0x2F, 0x6F, 0xD6);
            switch (kind)
            {
                case Kind.Up:
                    using (var p = new Pen(selected ? Theme.Bright : Theme.Muted, 2.2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                    {
                        g.DrawLine(p, 12, 19, 12, 6);
                        g.DrawLines(p, new[] { new PointF(6.5f, 11.5f), new PointF(12, 6), new PointF(17.5f, 11.5f) });
                    }
                    break;
                case Kind.Drive:
                    using (var p = new Pen(fileColor, 2f))
                    {
                        g.DrawRectangle(p, 3, 7, 18, 10);
                        g.DrawLine(p, 6, 14, 10, 14);
                    }
                    using (var b2 = new SolidBrush(Theme.Accent)) g.FillEllipse(b2, 16, 11, 3, 3);
                    break;
                case Kind.Dir:
                    using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        path.AddLines(new[] { new PointF(3, 6), new PointF(9, 6), new PointF(11, 8), new PointF(21, 8), new PointF(21, 19), new PointF(3, 19) });
                        path.CloseFigure();
                        using (var b2 = new SolidBrush(folderColor)) g.FillPath(b2, path);
                    }
                    break;
                default:
                    using (var p = new Pen(fileColor, 2f))
                    {
                        g.DrawRectangle(p, 4, 5, 16, 14);
                        g.DrawLine(p, 4, 9, 20, 9);
                    }
                    break;
            }
            g.Restore(state);
        }

        // The "jump:" box: what you typed, with a block cursor; "find:" in Ctrl+F mode.
        void PaintJump(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            bool active = jump.Length > 0 || findMode;
            using (var b = new SolidBrush(Theme.Field)) g.FillRectangle(b, jumpBox.ClientRectangle);
            using (var pen = new Pen(active ? Theme.Accent : Theme.Border)) g.DrawRectangle(pen, 0, 0, jumpBox.Width - 1, jumpBox.Height - 1);
            const TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            string label = findMode ? "find:" : "jump:";
            int x = Theme.S(10);
            TextRenderer.DrawText(g, label, Theme.Mono, new Rectangle(x, 0, Theme.S(50), jumpBox.Height), active ? Theme.Accent : Theme.Muted, f);
            x += TextRenderer.MeasureText(g, label, Theme.Mono, Size.Empty, f).Width + Theme.S(6);
            if (jump.Length == 0)
            {
                TextRenderer.DrawText(g, findMode ? "type to filter" : "just type", Theme.Small, new Rectangle(x, 0, jumpBox.Width - x - Theme.S(6), jumpBox.Height), Theme.Disabled, f);
                return;
            }
            TextRenderer.DrawText(g, jump, Theme.Mono, new Rectangle(x, 0, jumpBox.Width - x - Theme.S(16), jumpBox.Height), Theme.Text, f | TextFormatFlags.EndEllipsis);
            int w = Math.Min(jumpBox.Width - x - Theme.S(16), TextRenderer.MeasureText(g, jump, Theme.Mono, Size.Empty, f).Width);
            using (var c = new SolidBrush(Theme.Accent)) g.FillRectangle(c, x + w + Theme.S(2), (jumpBox.Height - Theme.S(15)) / 2, Theme.S(7), Theme.S(15));
        }

        // The keys along the bottom.
        void PaintKeys(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Theme.StatusBar)) g.FillRectangle(b, keysBar.ClientRectangle);
            using (var pen = new Pen(Theme.Divider)) g.DrawLine(pen, 0, 0, keysBar.Width, 0);
            var keys = new[]
            {
                new[] { "↑ ↓ PgUp PgDn", "move" }, new[] { "Enter", "open / use" }, new[] { "Backspace", "up" },
                new[] { "type", "jump" }, new[] { "Ctrl+F", "look 2 levels down" }, new[] { "Alt+←", "back" }, new[] { "Esc", "clear / cancel" }
            };
            const TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            int x = Theme.S(18), h = keysBar.Height;
            foreach (var k in keys)
            {
                int kw = TextRenderer.MeasureText(g, k[0], Theme.MonoSmall, Size.Empty, f).Width + Theme.S(12);
                int tw = TextRenderer.MeasureText(g, Theme.Up(k[1]), Theme.Small, Size.Empty, f).Width;
                if (x + kw + Theme.S(6) + tw > keysBar.Width - Theme.S(10)) break;
                var kr = new Rectangle(x, (h - Theme.S(20)) / 2, kw, Theme.S(20));
                using (var kb = new SolidBrush(Theme.Surface)) g.FillRectangle(kb, kr);
                using (var kp = new Pen(Theme.Border)) g.DrawRectangle(kp, kr.X, kr.Y, kr.Width - 1, kr.Height - 1);
                TextRenderer.DrawText(g, k[0], Theme.MonoSmall, kr, Theme.Text, f | TextFormatFlags.HorizontalCenter);
                x += kw + Theme.S(6);
                TextRenderer.DrawText(g, Theme.Up(k[1]), Theme.Small, new Rectangle(x, 0, tw + Theme.S(4), h), Theme.Muted, f);
                x += tw + Theme.S(16);
            }
        }
    }

    class SettingsForm : Form
    {
        public bool ToolsFolderChanged { get; private set; }
        public bool ThemeChanged { get; private set; }

        // Look choices: shown text and the value saved in launcher-settings.json.
        static readonly string[][] Looks =
        {
            new[] { "Match Windows (dark or light)", "system" },
            new[] { "Dark", "dark" },
            new[] { "Light", "light" },
            new[] { "Green phosphor", "phosphor" }
        };
        readonly ComboBox lookBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        public bool LayoutChanged { get; private set; }

        // Layout choices: shown text and the value saved in launcher-settings.json.
        static readonly string[][] Layouts =
        {
            new[] { "Main: wide window with profile tiles", AppSettings.MainLayout },
            new[] { "Compact: narrow list", AppSettings.CompactLayout }
        };
        readonly ComboBox layoutBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        public string Warning { get; private set; }

        readonly TextBox toolsBox = new TextBox { Dock = DockStyle.Fill };
        readonly Label toolsInfo = new Label { AutoSize = true, Font = Theme.Small };
        readonly CheckBox tempBox = new CheckBox { Text = "Keep temp files of launched programs on the stick", AutoSize = true };
        readonly CheckBox appDataBox = new CheckBox { Text = "Also keep AppData of launched programs on the stick", AutoSize = true };
        readonly CheckBox animateBox = new CheckBox { Text = "Move the icons of running profiles, with a soft green glow", AutoSize = true };
        readonly CheckBox emptyTempBox = new CheckBox { Text = "Empty the temp folder on the stick when the launcher starts", AutoSize = true };
        readonly Label tempSize = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        readonly bool programsOpen;

        // programsOpen: programs the launcher started are still open (they may still need their temp files).
        public SettingsForm(bool programsOpen = false)
        {
            this.programsOpen = programsOpen;
            Text = "Settings";
            Font = Theme.Ui;
            ClientSize = Theme.S(700, 860);
            MinimumSize = Theme.S(560, 460);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;

            int wrap = Theme.S(640);
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = Theme.Pad(14, 10, 14, 10), AutoScroll = true };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Controls.Add(t);

            Action<Control> full = c => { t.Controls.Add(c); t.SetColumnSpan(c, 2); };
            Func<string, Label> heading = s => new Label { Text = s, AutoSize = true, Font = Theme.TileTitle, Margin = Theme.Pad(0, 10, 0, 4) };
            Func<string, Label> note = s => new Label { Text = s, AutoSize = true, MaximumSize = new System.Drawing.Size(wrap, 0), ForeColor = Theme.Muted, Font = Theme.Small, Margin = Theme.Pad(18, 0, 0, 6) };

            // Tools folder
            full(heading("Tools folder"));
            var toolsNote = note("Where your tools are. In program paths, {tools} stands for this folder. It is saved relative to the " +
                                 "launcher's data, so it keeps working when the stick gets another drive letter or the launcher is moved.");
            toolsNote.Margin = Theme.Pad(0, 0, 0, 6);
            full(toolsNote);
            var browse = new Button { Text = "Browse...", AutoSize = true, Margin = Theme.Pad(6, 0, 0, 0) };
            browse.Click += (s, e) => BrowseTools();
            var reset = new Button { Text = "Launcher folder", AutoSize = true, Margin = Theme.Pad(6, 0, 0, 0) };
            reset.Click += (s, e) => toolsBox.Text = PathUtil.Full(PathUtil.HomeDir);
            var toolButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            toolButtons.Controls.Add(browse);
            toolButtons.Controls.Add(reset);
            t.Controls.Add(toolsBox);
            t.Controls.Add(toolButtons);
            toolsInfo.MaximumSize = new System.Drawing.Size(wrap, 0);
            toolsInfo.Margin = Theme.Pad(0, 4, 0, 6);
            full(toolsInfo);

            // Portable
            full(heading("Keep things on the stick"));
            full(tempBox);
            full(note("Launched programs get " + PathUtil.PortableDataName + "\\Temp on the stick as their TEMP folder. Programs started " +
                      "with \"Run as administrator\" while the launcher itself isn't admin get Windows' normal folders; use " +
                      "\"Restart as admin\" to cover those too."));
            full(appDataBox);
            full(note("Points APPDATA and LOCALAPPDATA to " + PathUtil.PortableDataName + "\\AppData. Programs that keep settings " +
                      "there then start with their default settings the first time. Programs that ask Windows for these folders " +
                      "directly, or that use the registry, still write to the PC."));
            full(emptyTempBox);
            var tempRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Theme.Pad(18, 0, 0, 6) };
            tempSize.Font = Theme.Small;
            tempSize.ForeColor = Theme.Muted;
            tempSize.Margin = Theme.Pad(0, 7, 10, 0);
            tempSize.Text = PathUtil.PortableDataName + "\\Temp: ...";
            var emptyNow = new Button { Text = "Empty now", AutoSize = true, Margin = new Padding(0) };
            emptyNow.Click += (s, e) => EmptyTempNow();
            tempRow.Controls.Add(tempSize);
            tempRow.Controls.Add(emptyNow);
            full(tempRow);
            full(note("Launched programs leave files there that would otherwise pile up on the stick. Files that are still in " +
                      "use are skipped. At start it is left alone while another launcher on this stick is open, or right after Restart as admin."));

            // Look
            full(heading("Look"));
            lookBox.Width = Theme.S(260);
            lookBox.Margin = Theme.Pad(3, 0, 0, 6);
            foreach (var look in Looks) lookBox.Items.Add(look[0]);
            full(lookBox);
            var lookNote = note("Dark and Light use normal text. Green phosphor is green on black with CAPITALS and [BRACKETS]. " +
                                "Match Windows follows the dark/light app setting of the PC you're on. " +
                                "The sun/moon button in the main window switches between Dark and Light.");
            lookNote.Margin = Theme.Pad(0, 0, 0, 6);
            full(lookNote);
            full(animateBox);
            full(note("While a profile launches, and while programs it started are open, its icon moves (fans spin, the needle " +
                      "swings, ...). Also off when Windows' \"Animation effects\" are off."));

            // Layout
            full(heading("Layout"));
            layoutBox.Width = Theme.S(260);
            layoutBox.Margin = Theme.Pad(3, 0, 0, 6);
            foreach (var l in Layouts) layoutBox.Items.Add(l[0]);
            full(layoutBox);
            var layoutNote = note("Main shows each profile as a card in a wide window, with a status bar at the bottom. " +
                                  "Compact is the narrow window with a list, a colour key and a 3-line log. Both do the same things.");
            layoutNote.Margin = Theme.Pad(0, 0, 0, 6);
            full(layoutNote);

            // Data folder
            full(heading("Launcher data"));
            var dataRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0) };
            dataRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            dataRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var dataLabel = new Label
            {
                Text = "profiles.json and " + PathUtil.SettingsFileName + " are in " + PathUtil.Full(PathUtil.HomeDir) +
                       (PathUtil.SamePath(PathUtil.HomeDir, PathUtil.ExeDir) ? "" : "\n(found there because Launcher.exe was moved to " + PathUtil.Full(PathUtil.ExeDir) + ")"),
                AutoSize = true,
                MaximumSize = new System.Drawing.Size(Theme.S(520), 0),
                ForeColor = Theme.Muted,
                Anchor = AnchorStyles.Left
            };
            var open = new Button { Text = "Open folder", AutoSize = true, Anchor = AnchorStyles.Right };
            open.Click += (s, e) => OpenDataFolder();
            dataRow.Controls.Add(dataLabel, 0, 0);
            dataRow.Controls.Add(open, 1, 0);
            full(dataRow);

            // Buttons
            var save = new Button { Text = "Save", Width = Theme.S(90), Height = Theme.S(30), Tag = Theme.Primary };
            save.Click += (s, e) => Save();
            var cancel = new Button { Text = "Cancel", Width = Theme.S(90), Height = Theme.S(30), DialogResult = DialogResult.Cancel, Margin = Theme.Pad(6, 3, 0, 3) };
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Margin = Theme.Pad(0, 14, 0, 0) };
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            full(buttons);
            AcceptButton = save;
            CancelButton = cancel;
            ActiveControl = save;   // don't open with the tools path highlighted

            var cur = SettingsStore.Current;
            toolsBox.Text = PathUtil.Full(PathUtil.ToolsDir);
            tempBox.Checked = cur.KeepTempOnStick;
            appDataBox.Checked = cur.KeepAppDataOnStick;
            emptyTempBox.Checked = cur.EmptyTempOnStart;
            animateBox.Checked = cur.AnimateRunning;
            Load += (s, e) => ShowTempSize();
            int lookIndex = Array.FindIndex(Looks, l => l[1] == (cur.Theme ?? "system"));
            lookBox.SelectedIndex = lookIndex < 0 ? 0 : lookIndex;
            int layoutIndex = Array.FindIndex(Layouts, l => l[1] == (cur.Layout ?? AppSettings.MainLayout));
            layoutBox.SelectedIndex = layoutIndex < 0 ? 0 : layoutIndex;
            toolsBox.TextChanged += (s, e) => UpdateToolsInfo();
            UpdateToolsInfo();

            Theme.ApplyToForm(this);
        }

        string ToolsFull()
        {
            string text = Environment.ExpandEnvironmentVariables(toolsBox.Text.Trim().Trim('"'));
            if (text.Length == 0) return PathUtil.HomeDir;
            try
            {
                string f = PathUtil.IsRooted(text) ? text : PathUtil.In(PathUtil.HomeDir, text);
                return PathUtil.Trim(Path.GetFullPath(PathUtil.Full(f)));
            }
            catch
            {
                return text;
            }
        }

        void UpdateToolsInfo()
        {
            string f = ToolsFull();
            bool exists = Directory.Exists(PathUtil.Full(f));
            string stored = PathUtil.StoreToolsFolder(f);
            bool onStick = PathUtil.IsOnDataDrive(f);
            if (!exists)
                toolsInfo.Text = "This folder doesn't exist.";
            else if (!onStick)
                toolsInfo.Text = "This folder isn't on the launcher's drive (" + PathUtil.Full(Path.GetPathRoot(PathUtil.Full(PathUtil.HomeDir))) +
                                 "). It won't move with the stick and is saved as a full path." +
                                 (PathUtil.IsDriveRoot(f) ? " It is a whole drive, which is probably a mistake." : "");
            else if (stored.Length == 0)
                toolsInfo.Text = "Same folder as the launcher's data.";
            else
                toolsInfo.Text = "Saved as \"" + stored + "\" (relative to the launcher's data folder).";
            toolsInfo.ForeColor = exists && onStick ? Theme.Muted : Theme.Warning;
        }

        void BrowseTools()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Choose the folder your tools are in", ShowNewFolderButton = false })
            {
                string cur = ToolsFull();
                if (Directory.Exists(PathUtil.Full(cur))) dlg.SelectedPath = PathUtil.Full(cur);
                if (dlg.ShowDialog(this) == DialogResult.OK) toolsBox.Text = dlg.SelectedPath;
            }
        }

        // Adds up the temp folder in the background (a slow stick with many files can take a moment).
        async void ShowTempSize()
        {
            long size = await Task.Run(() => PortableEnv.TempSize());
            if (!IsDisposed) tempSize.Text = PathUtil.PortableDataName + "\\Temp: " + PortableEnv.FormatSize(size);
        }

        async void EmptyTempNow()
        {
            if (programsOpen)
            {
                var r = MessageBox.Show(this,
                    "Programs the launcher started are still open. Files they have open are skipped, but they may still " +
                    "need other files in the temp folder.\n\nEmpty it anyway?",
                    "Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) return;
            }
            tempSize.Text = "Emptying...";
            long freed = await Task.Run(() => PortableEnv.EmptyTemp());
            if (IsDisposed) return;
            long left = await Task.Run(() => PortableEnv.TempSize());
            if (IsDisposed) return;
            tempSize.Text = "Freed " + (freed > 0 ? PortableEnv.FormatSize(freed) : "nothing") +
                            (left > 0 ? "; " + PortableEnv.FormatSize(left) + " in use, left" : "");
        }

        void OpenDataFolder()
        {
            try
            {
                string profiles = PathUtil.In(PathUtil.HomeDir, PathUtil.ProfilesFileName);
                if (File.Exists(profiles)) Process.Start("explorer.exe", "/select,\"" + profiles + "\"");
                else Process.Start("explorer.exe", "\"" + PathUtil.Full(PathUtil.HomeDir) + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Launcher");
            }
        }

        void Save()
        {
            string f = ToolsFull();
            if (!Directory.Exists(PathUtil.Full(f)))
            {
                var r = MessageBox.Show(this, "The tools folder " + f + " doesn't exist. Save anyway?", "Launcher",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) return;
            }
            else if (!PathUtil.IsOnDataDrive(f))
            {
                var r = MessageBox.Show(this,
                    "The tools folder " + PathUtil.Full(f) + " isn't on the launcher's drive" +
                    (PathUtil.IsDriveRoot(f) ? ", and it is a whole drive" : "") +
                    ".\n\nPrograms picked from it won't move with the stick. Use it anyway?",
                    "Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) return;
            }

            var s = SettingsStore.Current.Clone();
            s.ToolsFolder = PathUtil.StoreToolsFolder(f);
            s.KeepTempOnStick = tempBox.Checked;
            s.KeepAppDataOnStick = appDataBox.Checked;
            s.EmptyTempOnStart = emptyTempBox.Checked;
            s.AnimateRunning = animateBox.Checked;
            string oldTheme = SettingsStore.Current.Theme ?? "system";
            s.Theme = Looks[Math.Max(0, lookBox.SelectedIndex)][1];
            string oldLayout = SettingsStore.Current.Layout ?? AppSettings.MainLayout;
            s.Layout = Layouts[Math.Max(0, layoutBox.SelectedIndex)][1];

            string before = PathUtil.ToolsDir;
            try
            {
                Warning = SettingsStore.Save(s);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't save " + PathUtil.SettingsFileName + ":\n" + ex.Message, "Launcher",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            ToolsFolderChanged = !PathUtil.SamePath(before, PathUtil.ToolsDir);
            ThemeChanged = s.Theme != oldTheme;
            LayoutChanged = s.Layout != oldLayout;
            DialogResult = DialogResult.OK;
        }
    }

    // Shown when a program isn't where the profile says. Nothing is searched automatically: you see where
    // it was expected, a matching location if one already exists, and you choose what to do.
    class MissingProgramForm : Form
    {
        // Fixed: "Look in a folder" found this program (and maybe others); the step is already updated.
        public enum Choice { Skip, Use, Stop, Fixed }

        public Choice Result { get; private set; }
        public string ChosenPath { get; private set; }

        readonly string expected;

        // lookInFolder: looks for all missing programs in a folder (see FolderFixForm); returns true when this
        // program was fixed by it.
        public MissingProgramForm(string profileName, int stepNumber, string programName, string expectedPath,
                                  string suggestion, bool launching, Func<bool> lookInFolder = null)
        {
            expected = expectedPath;
            Result = Choice.Skip;

            Text = "Program not found";
            Font = Theme.Ui;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            int width = Theme.S(560);
            var t = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = Theme.Pad(16, 12, 16, 12), Dock = DockStyle.Fill };
            Controls.Add(t);

            // Read-only, wrapped over as many lines as needed, so the whole path (file name included) is visible.
            Func<string, TextBox> pathBox = s =>
            {
                var box = new TextBox { Text = s, ReadOnly = true, Multiline = true, WordWrap = true, Width = width, Margin = Theme.Pad(3, 2, 3, 10) };
                var size = TextRenderer.MeasureText(s ?? "", Font, new Size(width - Theme.S(14), 0),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
                box.Height = Math.Max(size.Height, Font.Height) + Theme.S(8);
                return box;
            };

            t.Controls.Add(new Label { Text = programName, AutoSize = true, Font = Theme.TileTitle });
            t.Controls.Add(new Label
            {
                Text = "\"" + profileName + "\", step " + stepNumber,
                AutoSize = true,
                ForeColor = Theme.Muted,
                Margin = Theme.Pad(0, 0, 0, 10)
            });
            t.Controls.Add(new Label { Text = "It isn't here:", AutoSize = true });
            t.Controls.Add(pathBox(expectedPath));

            Button use = null;
            if (suggestion != null)
            {
                t.Controls.Add(new Label { Text = "A matching file is here. Use this location?", AutoSize = true });
                t.Controls.Add(pathBox(suggestion));
                use = new Button { Text = "Use this location", AutoSize = true, Tag = Theme.Primary };
                use.Click += (s, e) => Finish(Choice.Use, suggestion);
            }
            else
            {
                t.Controls.Add(new Label
                {
                    Text = "Press \"Find it\" to pick where it is now. The list shows your tools, with likely matches at the top " +
                           "(also when it was renamed or updated)." +
                           (lookInFolder != null ? " \"Look in a folder\" looks for all missing programs at once." : ""),
                    AutoSize = true,
                    MaximumSize = new System.Drawing.Size(width, 0),
                    ForeColor = Theme.Muted,
                    Margin = Theme.Pad(0, 0, 0, 10)
                });
            }

            var find = new Button { Text = "Find it...", AutoSize = true, Tag = suggestion == null ? Theme.Primary : null };
            find.Click += (s, e) => Find();
            var skip = new Button { Text = launching ? "Skip this program" : "Skip", AutoSize = true, DialogResult = DialogResult.Cancel };
            var stop = new Button { Text = launching ? "Don't launch" : "Stop checking", AutoSize = true };
            stop.Click += (s, e) => Finish(Choice.Stop, null);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Theme.Pad(0, 6, 0, 0) };
            if (use != null) buttons.Controls.Add(use);
            buttons.Controls.Add(find);
            if (lookInFolder != null)
            {
                var look = new Button { Text = "Look in a folder...", AutoSize = true };
                look.Click += (s, e) => { if (lookInFolder()) Finish(Choice.Fixed, null); };
                buttons.Controls.Add(look);
            }
            buttons.Controls.Add(skip);
            buttons.Controls.Add(stop);
            t.Controls.Add(buttons);

            AcceptButton = use ?? find;
            CancelButton = skip;
            ActiveControl = use ?? find;   // focus the main choice, not the path box
            Theme.ApplyToForm(this);
        }

        void Finish(Choice c, string path)
        {
            Result = c;
            ChosenPath = path;
            DialogResult = DialogResult.OK;
        }

        // Opens the program list to find where the program is now, with likely matches at the top.
        // It lists the tools folder and everything below it. Only when the program used to be somewhere else
        // on the stick (outside the tools folder) does it start in the nearest folder of that place that exists.
        void Find()
        {
            string start = PathUtil.ToolsDirFound ? PathUtil.ToolsDir : PathUtil.HomeDir;
            try
            {
                bool underTools = PathUtil.ToolsDirFound &&
                    expected.StartsWith(PathUtil.Trim(PathUtil.ToolsDir) + "\\", StringComparison.OrdinalIgnoreCase);
                if (!underTools)
                {
                    foreach (var anc in PathUtil.Ancestors(Path.GetDirectoryName(expected)))
                    {
                        if (!Directory.Exists(PathUtil.Full(anc))) continue;
                        if (PathUtil.IsOnDataDrive(anc) && !PathUtil.IsDriveRoot(anc)) start = anc;
                        break;
                    }
                }
            }
            catch { }

            using (var picker = new ProgramPicker(start, null, expected))
            {
                if (picker.ShowDialog(this) == DialogResult.OK) Finish(Choice.Use, picker.SelectedPath);
            }
        }
    }

    // "Look in a folder": looks for every missing program at once in one folder you choose (the tools folder to
    // start with) and its subfolders, at most two levels deep. It proposes a new location for each one it finds
    // (sure matches ticked, guesses not) and changes nothing until you press Fix.
    class FolderFixForm : Form
    {
        public const int MaxDepth = 2;   // the folder itself, its subfolders and theirs
        const int MaxFiles = 20000;

        public class Item
        {
            public string Where;      // "Bench setup", step 3 · CPU-Z
            public Step Step;
            public string Expected;   // full path where it used to be
            internal string Found;    // full path of the best match, or null
            internal int Score;
            internal CheckBox Box;
        }

        // The fixes you confirmed: the step and the program's full path now.
        public List<KeyValuePair<Step, string>> Fixes { get; private set; }

        readonly List<Item> items;
        readonly TextBox folderBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
        readonly FlowLayoutPanel rows = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Margin = new Padding(0) };
        readonly Label info = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        readonly Button fix = new Button { Text = "Fix", AutoSize = true, Tag = Theme.Primary };
        string folder;
        CancellationTokenSource scan;

        public FolderFixForm(List<Item> missing, string startFolder)
        {
            items = missing;
            Fixes = new List<KeyValuePair<Step, string>>();
            Text = "Look in a folder";
            Font = Theme.Ui;
            ClientSize = Theme.S(720, 540);
            MinimumSize = Theme.S(520, 380);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;

            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = Theme.Pad(16, 12, 16, 12) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 3; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(t);

            var heading = new Label { Text = Theme.Up("Look for " + missing.Count + (missing.Count == 1 ? " missing program" : " missing programs")), AutoSize = true, Font = Theme.Heading, ForeColor = Theme.Terminal ? Theme.Accent : Theme.Bright, Margin = Theme.Pad(0, 0, 0, 2) };
            t.Controls.Add(heading, 0, 0);
            t.SetColumnSpan(heading, 2);
            var sub = new Label
            {
                Text = "Looks in this folder and in its subfolders, at most 2 levels deep. Nothing else on the PC is searched, " +
                       "and nothing changes until you press Fix. Sure matches are ticked; check the guesses before ticking them.",
                AutoSize = true, MaximumSize = new Size(Theme.S(680), 0), ForeColor = Theme.Muted, Margin = Theme.Pad(0, 0, 0, 10)
            };
            t.Controls.Add(sub, 0, 1);
            t.SetColumnSpan(sub, 2);

            folderBox.Margin = Theme.Pad(0, 0, 8, 10);
            t.Controls.Add(folderBox, 0, 2);
            var other = new Button { Text = "Other folder...", Tag = Theme.Link, AutoSize = true, Margin = new Padding(0) };
            other.Click += (s, e) => ChooseFolder();
            var tools = new Button { Text = "Tools folder", Tag = Theme.Link, AutoSize = true, Margin = Theme.Pad(4, 0, 0, 0) };
            tools.Click += (s, e) => Scan(PathUtil.ToolsDir);
            var folderButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Theme.Pad(0, 0, 0, 10) };
            folderButtons.Controls.Add(other);
            folderButtons.Controls.Add(tools);
            t.Controls.Add(folderButtons, 1, 2);

            var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0) };
            frame.Paint += (s, e) => { using (var pen = new Pen(Theme.Border)) e.Graphics.DrawRectangle(pen, 0, 0, frame.Width - 1, frame.Height - 1); };
            frame.Controls.Add(rows);
            rows.ClientSizeChanged += (s, e) => { foreach (Control c in rows.Controls) c.Width = rows.ClientSize.Width; };
            t.Controls.Add(frame, 0, 3);
            t.SetColumnSpan(frame, 2);

            fix.MinimumSize = Theme.S(90, 30);
            fix.Click += (s, e) => Apply();
            var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = Theme.S(90, 30), DialogResult = DialogResult.Cancel, Margin = Theme.Pad(0, 3, 8, 3) };
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Theme.Pad(0, 10, 0, 0) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(fix);
            info.Font = Theme.Small;
            info.Margin = Theme.Pad(0, 10, 0, 0);
            info.MaximumSize = new Size(Theme.S(440), 0);
            t.Controls.Add(info, 0, 4);
            t.Controls.Add(buttons, 1, 4);
            AcceptButton = fix;
            CancelButton = cancel;

            Theme.ApplyToForm(this);
            Load += (s, e) => Scan(startFolder);
            FormClosed += (s, e) => { if (scan != null) scan.Cancel(); };
        }

        void ChooseFolder()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Folder to look in (and 2 levels of subfolders)", ShowNewFolderButton = false })
            {
                if (folder != null && Directory.Exists(PathUtil.Full(folder))) dlg.SelectedPath = PathUtil.Full(folder);
                if (dlg.ShowDialog(this) == DialogResult.OK) Scan(dlg.SelectedPath);
            }
        }

        async void Scan(string dir)
        {
            dir = PathUtil.Trim((dir ?? "").Trim().Trim('"'));
            if (scan != null) scan.Cancel();
            if (dir.Length == 0 || !Directory.Exists(PathUtil.Full(dir)))
            {
                ShowStatus("That folder doesn't exist.", true);
                return;
            }
            // Never go through a whole drive of the PC without asking (e.g. C:\ chosen by mistake).
            if (PathUtil.IsDriveRoot(dir) && !PathUtil.IsOnDataDrive(dir))
            {
                var r = MessageBox.Show(this, "Look on the whole drive " + PathUtil.Full(dir) + " (2 levels of folders)?\n\n" +
                    "This isn't the drive the launcher is on. Choosing the folder your tools are in is usually better.",
                    "Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) return;
            }

            folder = dir;
            folderBox.Text = PathUtil.Full(dir);
            folderBox.SelectionStart = folderBox.TextLength;
            if (folderBox.IsHandleCreated) folderBox.ScrollToCaret();
            ShowStatus("Looking in " + PathUtil.Full(dir) + "...", false);

            var cts = scan = new CancellationTokenSource();
            List<string> files;
            try
            {
                string self = Application.ExecutablePath;
                files = await Task.Run(() => FileWalker.Walk(dir,
                    f => FileWalker.IsProgram(f) && !string.Equals(f, self, StringComparison.OrdinalIgnoreCase), MaxDepth, MaxFiles, cts.Token), cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (cts != scan || IsDisposed) return;

            // Best match per missing program: highest score, then the one closest to the folder, then by name.
            string prefix = PathUtil.Trim(dir) + "\\";
            foreach (var it in items)
            {
                string name, oldFolder;
                ProgramMatch.Split(it.Expected, out name, out oldFolder);
                it.Found = null;
                it.Score = 0;
                foreach (var f in files
                    .Select(f => new { Path = f, Score = ProgramMatch.Score(f, name, oldFolder) })
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Path.Count(ch => ch == '\\'))
                    .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                    .Take(1))
                {
                    it.Found = f.Path;
                    it.Score = f.Score;
                }
            }
            BuildRows(prefix, files.Count);
        }

        void BuildRows(string prefix, int fileCount)
        {
            rows.SuspendLayout();
            foreach (Control c in rows.Controls.Cast<Control>().ToList()) { rows.Controls.Remove(c); c.Dispose(); }
            foreach (var it in items)
            {
                var row = new Panel { Width = rows.ClientSize.Width, Height = Theme.S(58), Margin = new Padding(0), BackColor = Theme.Field };
                var item = it;
                row.Paint += (s, e) => { using (var pen = new Pen(Theme.Divider)) e.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1); };
                string oldName = "";
                try { oldName = Path.GetFileName(it.Expected); } catch (ArgumentException) { }
                if (it.Found != null)
                {
                    bool sure = it.Score >= ProgramMatch.Sure;
                    it.Box = new CheckBox { Text = it.Where, AutoSize = true, Checked = sure, Location = new Point(Theme.S(10), Theme.S(8)), Font = Theme.UiBold };
                    it.Box.CheckedChanged += (s, e) => UpdateFixButton();
                    string rel = it.Found.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? it.Found.Substring(prefix.Length) : it.Found;
                    row.Controls.Add(it.Box);
                    row.Controls.Add(new Label
                    {
                        Text = "→ " + rel + (sure ? "" : "   (guess: was " + oldName + ")"),
                        AutoSize = true, AutoEllipsis = true, MaximumSize = new Size(Math.Max(Theme.S(200), rows.ClientSize.Width - Theme.S(48)), 0),
                        Location = new Point(Theme.S(30), Theme.S(32)), Font = Theme.Small,
                        ForeColor = sure ? Theme.Accent : Theme.Warning
                    });
                }
                else
                {
                    it.Box = null;
                    row.Controls.Add(new Label { Text = it.Where, AutoSize = true, Location = new Point(Theme.S(30), Theme.S(8)), Font = Theme.UiBold, ForeColor = Theme.Muted });
                    row.Controls.Add(new Label
                    {
                        Text = "not found here (" + oldName + "). Use Find it for this one, or try another folder.",
                        AutoSize = true, Location = new Point(Theme.S(30), Theme.S(32)), Font = Theme.Small, ForeColor = Theme.Muted
                    });
                }
                rows.Controls.Add(row);
            }
            rows.ResumeLayout();
            Theme.Apply(rows);

            int found = items.Count(i => i.Found != null), sureCount = items.Count(i => i.Found != null && i.Score >= ProgramMatch.Sure);
            ShowStatus(found + " of " + items.Count + " found (" + sureCount + " sure, " + (found - sureCount) + " guesses) in " +
                       fileCount + " programs" + (fileCount >= MaxFiles ? " (stopped looking after " + MaxFiles + ")" : "") + ".", false);
            UpdateFixButton();
        }

        void UpdateFixButton()
        {
            int n = items.Count(i => i.Box != null && i.Box.Checked);
            fix.Text = n == 0 ? "Fix" : "Fix " + n;
            fix.ForeColor = n > 0 ? Theme.PrimaryText : Theme.Disabled;
        }

        void ShowStatus(string text, bool warn)
        {
            info.Text = text;
            info.ForeColor = warn ? Theme.Warning : Theme.Muted;
        }

        void Apply()
        {
            Fixes = items.Where(i => i.Box != null && i.Box.Checked && i.Found != null)
                         .Select(i => new KeyValuePair<Step, string>(i.Step, i.Found)).ToList();
            if (Fixes.Count == 0) return;
            DialogResult = DialogResult.OK;
        }
    }
}
