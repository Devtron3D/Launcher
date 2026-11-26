using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using CancellationToken = System.Threading.CancellationToken;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LauncherApp
{
    // Key for the segment colours.
    class LegendBar : Panel
    {
        public LegendBar()
        {
            DoubleBuffered = true;
            Height = Theme.S(22);
            BackColor = Theme.Back;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            int x = 0, sz = Theme.S(8), y = (Height - sz) / 2;
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
            Action<string, Action<Rectangle>> item = (label, draw) =>
            {
                draw(new Rectangle(x, y, sz, sz));
                x += sz + Theme.S(5);
                TextRenderer.DrawText(g, label, Theme.Small, new Rectangle(x, 0, Theme.S(80), Height), Theme.Muted, flags);
                x += TextRenderer.MeasureText(g, label, Theme.Small, Size.Empty, flags).Width + Theme.S(12);
            };
            item(Theme.Up("Up"), r => { using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, r); });
            item(Theme.Up("Starting"), r => { using (var b = new SolidBrush(Theme.StatusBusy)) g.FillRectangle(b, r); });
            item(Theme.Up("Queued"), r => { using (var p = new Pen(Theme.Disabled)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1); });
            item(Theme.Up("Shared"), r => ProfileView.DrawShared(g, r));
            item(Theme.Up("Missing"), r => { using (var b = new SolidBrush(Theme.StatusProblem)) g.FillRectangle(b, r); });
        }
    }

    // The last 3 log lines (click for the full log), with [STOP ALL] and [CLOSE ALL].
    class LogPanel : Panel
    {
        public readonly Button StopAllButton = new Button { Text = Theme.Pick("Stop all", "[STOP ALL]") };
        public readonly Button CloseAllButton = new Button { Text = Theme.Pick("Close all", "[CLOSE ALL]") };
        public event EventHandler OpenFullLog;
        public IList<LogEntry> Entries = new List<LogEntry>();
        public int Warnings;

        int LinesBottom { get { return StopAllButton.Top - Theme.S(4); } }

        public LogPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Theme.Back;
            Height = Theme.S(132);
            Controls.Add(StopAllButton);
            Controls.Add(CloseAllButton);
            Cursor = Cursors.Hand;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int pad = Theme.S(10), h = Theme.S(30);
            int w = (Width - 3 * pad) / 2;
            int y = Height - pad - h;
            StopAllButton.Bounds = new Rectangle(pad, y, w, h);
            CloseAllButton.Bounds = new Rectangle(2 * pad + w, y, Width - 3 * pad - w, h);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Y < LinesBottom && OpenFullLog != null) OpenFullLog(this, EventArgs.Empty);
        }

        public static Color KindColor(string kind)
        {
            switch (kind)
            {
                case "start": case "done": case "ready": return Theme.Accent;
                case "wait": return Theme.StatusBusy;
                case "warn": case "fail": case "skip": return Theme.StatusProblem;
                case "shared": return Theme.Dim;
                default: return Theme.Muted;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            int pad = Theme.S(10), hh = Theme.S(26);
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                                          TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
            const TextFormatFlags measure = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            using (var pen = new Pen(Theme.Border))
            {
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                g.DrawLine(pen, 0, hh, Width, hh);
            }

            TextRenderer.DrawText(g, Theme.Up("Log"), Theme.Terminal ? Theme.Small : Theme.UiBold, new Rectangle(pad, 0, Theme.S(40), hh), Theme.Terminal ? Theme.Accent : Theme.Bright, flags);
            TextRenderer.DrawText(g, Theme.Up("last 3 · click for all"), Theme.Small, new Rectangle(pad + Theme.S(40), 0, Width / 2, hh), Theme.Muted, flags);
            if (Warnings > 0)
            {
                string w = Theme.Up(Warnings + (Warnings == 1 ? " warning" : " warnings"));
                int ww = TextRenderer.MeasureText(g, w, Theme.Small, Size.Empty, measure).Width;
                TextRenderer.DrawText(g, w, Theme.Small, new Rectangle(Width - pad - ww - 2, 0, ww + 2, hh), Theme.Warning, flags);
            }

            int y = hh + Theme.S(5), lineH = Theme.S(17);
            var last = Entries.Skip(Math.Max(0, Entries.Count - 3)).ToList();
            if (last.Count == 0)
            {
                TextRenderer.DrawText(g, Theme.Up("nothing yet"), Theme.Small, new Rectangle(pad, y, Width - 2 * pad, lineH), Theme.Disabled, flags);
                return;
            }
            foreach (var entry in last)
            {
                int x = pad;
                string time = entry.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                var f = Theme.MonoSmall;
                TextRenderer.DrawText(g, time, f, new Rectangle(x, y, Theme.S(70), lineH), Theme.Disabled, flags);
                x += TextRenderer.MeasureText(g, time + " ", f, Size.Empty, measure).Width + Theme.S(4);
                TextRenderer.DrawText(g, entry.Tag, f, new Rectangle(x, y, Theme.S(60), lineH), Theme.Dim, flags);
                x += Theme.S(56);
                TextRenderer.DrawText(g, entry.Kind, f, new Rectangle(x, y, Theme.S(50), lineH), KindColor(entry.Kind), flags);
                x += Theme.S(50);
                TextRenderer.DrawText(g, entry.Text, f, new Rectangle(x, y, Math.Max(0, Width - pad - x), lineH), Theme.Text, flags);
                y += lineH;
            }
        }
    }

    // The whole log, in a window you can scroll and copy from.
    class LogForm : Form
    {
        public LogForm(IList<LogEntry> entries)
        {
            Text = "Launch log";
            Font = Theme.Ui;
            ClientSize = Theme.S(700, 440);
            MinimumSize = Theme.S(420, 260);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;

            var text = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                WordWrap = false,
                Font = Theme.Mono,
                Text = entries.Count == 0
                    ? "Nothing logged yet."
                    : string.Join(Environment.NewLine, entries.Select(en =>
                        en.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + en.Tag.PadRight(6) + "  " + en.Kind.PadRight(6) + "  " + en.Text))
            };
            var copy = new Button { Text = "Copy", Width = Theme.S(90), Height = Theme.S(30) };
            copy.Click += (s, e) => { try { Clipboard.SetText(text.Text); } catch { } };
            var close = new Button { Text = "Close", Width = Theme.S(90), Height = Theme.S(30), Tag = Theme.Primary, DialogResult = DialogResult.OK, Margin = Theme.Pad(6, 3, 0, 3) };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = Theme.Pad(0, 8, 0, 0) };
            buttons.Controls.Add(close);
            buttons.Controls.Add(copy);
            var root = new Panel { Dock = DockStyle.Fill, Padding = Theme.Pad(12) };
            root.Controls.Add(text);
            root.Controls.Add(buttons);
            Controls.Add(root);
            AcceptButton = close;
            CancelButton = close;
            Theme.ApplyToForm(this);
            Shown += (s, e) =>
            {
                text.SelectionStart = text.TextLength;   // newest at the bottom, nothing highlighted
                text.ScrollToCaret();
                close.Focus();
            };
        }
    }

    public class MainForm : Form
    {
        readonly LaunchEngine engine = new LaunchEngine();
        readonly ToolTip tips = new ToolTip();
        readonly Timer refresh = new Timer { Interval = 1000 };
        // Moves the icons of busy profiles (about 20 times a second; only runs while one is busy).
        readonly Timer animation = new Timer { Interval = 50 };
        readonly Stopwatch animationClock = Stopwatch.StartNew();
        List<Profile> profiles;
        FlowLayoutPanel list;
        LogPanel logPanel;
        MainStatusBar statusBar;   // main layout only
        Button checkBtn;
        TitleBar titleBar;
        StickInfo stick;
        DateTime stickReadAt;
        bool closingPrograms;
        bool checkingPrograms;
        Task tempCleanup;                                   // emptying the temp folder; launches wait for it
        readonly List<string> runOnStart = new List<string>();   // --run "profile name"

        // args: --run "name" (any number of times) launches those profiles once the window is open.
        // emptyTemp: empty the temp folder on the stick if the setting says so (only when no other launcher
        // and none of its programs can be using it).
        public MainForm(string[] args, bool emptyTemp)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--run", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    runOnStart.Add(args[++i]);
                else if (args[i].StartsWith("--run=", StringComparison.OrdinalIgnoreCase))
                    runOnStart.Add(args[i].Substring(6));
            }

            // Settings first: they decide the look, where the tools folder is and where temp files go.
            string settingsWarning = SettingsStore.Load();
            Theme.Use(SettingsStore.Current.Theme);

            Text = "Launcher " + AppInfo.Version + (AppInfo.IsElevated ? " (Administrator)" : "");
            Font = Theme.Ui;
            SizeForLayout();
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Back;

            BuildUi();
            Theme.ApplyToForm(this);
            if (titleBar != null) titleBar.ApplyColors();

            string warning;
            profiles = ProfileStore.Load(out warning);
            RebuildRows();
            if (settingsWarning != null) warning = warning == null ? settingsWarning : settingsWarning + "\n\n" + warning;
            if (warning != null)
                Shown += (s, e) => MessageBox.Show(this, warning, "Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            engine.Changed += RefreshAll;
            ShowToolsFolderState();

            if (emptyTemp && SettingsStore.Current.EmptyTempOnStart)
            {
                tempCleanup = Task.Run(() => PortableEnv.EmptyTemp());
                tempCleanup.ContinueWith(t =>
                {
                    long freed = ((Task<long>)t).Result;
                    if (freed > 0) ShowStatus("Emptied the temp folder on the stick (" + PortableEnv.FormatSize(freed) + ")", EngineState.Idle);
                }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.FromCurrentSynchronizationContext());
            }
            if (runOnStart.Count > 0) Shown += (s, e) => BeginInvoke((Action)RunFromCommandLine);
            refresh.Tick += (s, e) => RefreshAll();
            refresh.Start();
            animation.Tick += (s, e) =>
            {
                double t = animationClock.Elapsed.TotalSeconds;
                foreach (var v in list.Controls.OfType<ProfileView>())
                    if (v.Active) v.Animate(t);
            };
            FormClosing += (s, e) => engine.CancelAll();
            FormClosed += (s, e) =>
            {
                refresh.Dispose();
                animation.Dispose();
                tips.Dispose();
            };
        }

        // Main = the wide window with profile tiles (mockup B); compact = the narrow list. Chosen in Settings.
        static bool IsMainLayout
        {
            get { return SettingsStore.Current.Layout != AppSettings.CompactLayout; }
        }

        void SizeForLayout()
        {
            MinimumSize = Size.Empty;
            ClientSize = IsMainLayout ? Theme.S(1000, 640) : Theme.S(480, 640);
            MinimumSize = IsMainLayout ? Theme.S(620, 480) : Theme.S(440, 540);
            if (IsHandleCreated) Theme.FitToScreen(this);
        }

        void BuildUi()
        {
            logPanel = null;
            statusBar = null;
            if (IsMainLayout) BuildMainUi();
            else BuildCompactUi();
        }

        // The light/dark switch: shows the look you get by clicking it.
        Button MakeLookButton()
        {
            string otherLook = Theme.IsDark ? "light" : "dark";
            var lookBtn = new Button { Text = Theme.IsDark ? "Light mode" : "Dark mode", Size = Theme.S(32, 32), Margin = Theme.Pad(0, 0, 6, 0) };
            Theme.MakeIconButton(lookBtn, (g, r, color) =>
            {
                int sz = Theme.S(18);
                Icons.Draw(g, otherLook == "light" ? "sun" : "moon", new RectangleF(r.X + (r.Width - sz) / 2f, r.Y + (r.Height - sz) / 2f, sz, sz), color, 1.75f);
            });
            // Deferred: the switch rebuilds the window, including this button.
            lookBtn.Click += (s, e) => BeginInvoke((Action)(() => SwitchLook(otherLook)));
            tips.SetToolTip(lookBtn, "Switch to " + otherLook + " mode.\nSettings → Look also has Match Windows and Green phosphor.");
            return lookBtn;
        }

        // C / F: switches between the compact and the full (main) layout. Shows the layout you get by clicking it.
        Button MakeLayoutButton()
        {
            bool toCompact = IsMainLayout;
            var b = new Button { Text = toCompact ? "Compact layout" : "Full layout", Size = Theme.S(32, 32) };
            Theme.MakeIconButton(b, (g, r, color) =>
            {
                var font = Theme.Terminal ? Theme.Mono : Theme.UiBold;
                TextRenderer.DrawText(g, toCompact ? "C" : "F", font, r, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            });
            b.Click += (s, e) => BeginInvoke((Action)(() => SwitchLayout(toCompact ? AppSettings.CompactLayout : AppSettings.MainLayout)));
            tips.SetToolTip(b, toCompact ? "C: switch to the compact layout (narrow list)" : "F: switch to the full layout (cards)");
            return b;
        }

        void SwitchLayout(string layout)
        {
            try { SettingsStore.SaveLayout(layout); }
            catch (Exception ex) { ShowStatus("Couldn't save the layout to " + PathUtil.SettingsFileName + ": " + ex.Message, EngineState.Problem); }
            ApplyTheme();
            SizeForLayout();
            RebuildRows();
            RefreshAll();
        }

        // Mockup B with the N1 title bar: the window's own title bar (stick, counters, Check, light/dark, menu),
        // the profiles as cards, and a status bar (what is happening, programs open, Stop all, Close launched programs).
        void BuildMainUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(48)));
            Controls.Add(root);

            list = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = true,
                Margin = new Padding(0),
                Padding = Theme.Pad(13)
            };
            list.ClientSizeChanged += (s, e) => SizeRows();
            root.Controls.Add(list, 0, 0);

            statusBar = new MainStatusBar { Dock = DockStyle.Fill, Margin = new Padding(0) };
            statusBar.OpenFullLog += (s, e) => ShowFullLog();
            statusBar.StopAllButton.Click += (s, e) => { if (engine.IsRunning) engine.CancelAll(); };
            statusBar.CloseAllButton.Click += (s, e) => CloseLaunched();
            tips.SetToolTip(statusBar, "Click for the whole log");
            tips.SetToolTip(statusBar.StopAllButton, "Stop every launch in progress. Programs that already started stay open.");
            tips.SetToolTip(statusBar.CloseAllButton, "Close every program the launcher started (asks first).");
            root.Controls.Add(statusBar, 0, 1);

            AddTitleBar(false);
        }

        // The window's own title bar (both layouts). Added last, so it docks at the very top.
        void AddTitleBar(bool compact)
        {
            titleBar = new TitleBar(compact, MakeLookButton(), MakeLayoutButton()) { Dock = DockStyle.Top };
            if (titleBar.CheckButton != null)
            {
                checkBtn = titleBar.CheckButton;
                checkBtn.Click += (s, e) => CheckPrograms(false);
                tips.SetToolTip(checkBtn, "Check programs: is every program where its profile says?\nFor each missing one you're asked where it is now.");
            }
            titleBar.MenuButton.Click += (s, e) => ShowAppMenu(titleBar.MenuButton);
            tips.SetToolTip(titleBar.MenuButton, "Settings, import profiles, the log, the launcher folder, restart as admin");
            titleBar.Stick.Click += (s, e) => ShowStickPanel();
            titleBar.WarnChip.Click += (s, e) => FixWarning();
            Controls.Add(titleBar);
            UpdateTitleBar(true);
        }

        // The title bar: stick (read every 5 s), warning, counters.
        void UpdateTitleBar(bool readStick)
        {
            if (titleBar == null || titleBar.IsDisposed) return;
            if (readStick || stick == null || (DateTime.Now - stickReadAt).TotalSeconds > 5)
            {
                stick = StickInfo.Read();
                stickReadAt = DateTime.Now;
            }
            string warn = !PathUtil.ToolsDirFound ? "Tools folder not found · fix" : null;
            bool notStick = stick != null && stick.SystemDrive;
            titleBar.Stick.Warn = warn != null || notStick;
            titleBar.Warning = warn == null ? null : Theme.Up(warn);
            if (warn != null) tips.SetToolTip(titleBar.WarnChip, warn + "\nClick for more.");
            int busy = list == null ? 0 : list.Controls.OfType<ProfileView>().Count(v => v.Active);
            titleBar.SetState(stick, busy, engine.IsClosing ? titleBar.Open : engine.OpenCount, profiles == null ? 0 : profiles.Count,
                              WindowState == FormWindowState.Maximized);
            tips.SetToolTip(titleBar.Stick, stick == null ? "The launcher's folder" :
                stick.Name + " · " + PortableEnv.FormatSize(stick.Free) + " free of " + PortableEnv.FormatSize(stick.Total) +
                (notStick ? "\nNot a USB stick: the launcher runs from the PC's own Windows drive, so its files are saved on this PC." : "") +
                (stick.NearlyFull ? "\nAlmost full." : "") +
                "\nClick for details and actions.");
        }

        void FixWarning()
        {
            if (!PathUtil.ToolsDirFound) { OpenSettings(); return; }
            if (stick != null && stick.SystemDrive)
                MessageBox.Show(this, "The launcher is running from " + stick.Name + ", the PC's own Windows drive, not from a USB stick.\n\n" +
                    "Its settings, profiles and the temp files of launched programs are then saved on this PC. " +
                    "Copy the launcher folder to the stick and start it from there to keep the PC clean.",
                    "Launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Main layout ⋯ menu.
        void ShowAppMenu(Control anchor)
        {
            var menu = new ContextMenuStrip();
            if (!IsMainLayout)
            {
                AddItem(menu, "Check programs", true, () => CheckPrograms(false));
                menu.Items.Add(new ToolStripSeparator());
            }
            AddItem(menu, "Settings...", true, OpenSettings);
            AddItem(menu, "Import profiles...", true, ImportProfiles);
            AddItem(menu, "Show the log", true, ShowFullLog);
            AddItem(menu, "Open launcher folder", true, OpenLauncherFolder);
            menu.Items.Add(new ToolStripSeparator());
            AddItem(menu, "Restart as admin", !AppInfo.IsElevated, RestartAsAdmin);
            AddItem(menu, AppInfo.IsElevated ? "Running as administrator" : "Running with normal rights", false, null);
            Theme.StyleMenu(menu);
            menu.Closed += (s, e) => BeginInvoke((Action)menu.Dispose);
            menu.Show(anchor, new Point(anchor.Width, anchor.Height), ToolStripDropDownDirection.BelowLeft);
        }

        ToolStripMenuItem AddItem(ContextMenuStrip menu, string text, bool enabled, Action act)
        {
            var item = new ToolStripMenuItem(Theme.Up(text)) { Enabled = enabled };
            if (act != null) item.Click += (s, e) => BeginInvoke(act);   // after the menu has closed
            menu.Items.Add(item);
            return item;
        }

        void ShowFullLog()
        {
            using (var f = new LogForm(engine.Log)) f.ShowDialog(this);
        }

        void OpenLauncherFolder()
        {
            try
            {
                if (File.Exists(ProfileStore.FilePath)) Process.Start("explorer.exe", "/select,\"" + ProfileStore.FilePath + "\"");
                else Process.Start("explorer.exe", "\"" + PathUtil.Full(PathUtil.HomeDir) + "\"");
            }
            catch (Exception ex)
            {
                ShowStatus("Couldn't open the launcher folder: " + ex.Message, EngineState.Problem);
            }
        }

        // The narrow window: the title bar, the profile list, the colour key and a 3-line log.
        void BuildCompactUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));            // profiles
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));                // legend
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(132)));  // log
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(16)));   // bottom margin
            Controls.Add(root);
            checkBtn = null;

            // Profiles, in a frame
            var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = Theme.Pad(16, 14, 16, 0) };
            frame.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, frame.Width - 1, frame.Height - 1);
            };
            list = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Margin = new Padding(0)
            };
            list.ClientSizeChanged += (s, e) => SizeRows();
            frame.Controls.Add(list);
            root.Controls.Add(frame, 0, 0);

            root.Controls.Add(new LegendBar { Dock = DockStyle.Fill, Margin = Theme.Pad(16, 6, 16, 6) }, 0, 1);

            logPanel = new LogPanel { Dock = DockStyle.Fill, Margin = Theme.Pad(16, 0, 16, 0) };
            logPanel.OpenFullLog += (s, e) => ShowFullLog();
            logPanel.StopAllButton.Click += (s, e) => { if (engine.IsRunning) engine.CancelAll(); };
            logPanel.CloseAllButton.Click += (s, e) => CloseLaunched();
            tips.SetToolTip(logPanel.StopAllButton, "Stop every launch in progress. Programs that already started stay open.");
            tips.SetToolTip(logPanel.CloseAllButton, "Close every program the launcher started (asks first).");
            root.Controls.Add(logPanel, 0, 2);

            AddTitleBar(true);
        }

        // ---- refresh ----

        void RefreshAll()
        {
            Theme.Animate = SettingsStore.Current.AnimateRunning && Theme.WindowsAnimations();
            var infos = engine.Runs().ToDictionary(i => i.Profile);
            if (!engine.IsClosing) engine.DropClosed();   // programs that closed no longer count as open
            foreach (var view in list.Controls.OfType<ProfileView>())
            {
                RunInfo info;
                infos.TryGetValue(view.Profile, out info);
                view.UpdateState(info, engine.LastResultFor(view.Profile), engine.OpenCountFor(view.Profile), engine.LaunchedAt(view.Profile));
            }
            bool anyBusy = Theme.Animate && list.Controls.OfType<ProfileView>().Any(v => v.Active);
            if (animation.Enabled != anyBusy) animation.Enabled = anyBusy;

            // Dimmed instead of disabled: Windows draws disabled button text almost invisibly on this background.
            Color stopColor = engine.IsRunning ? Theme.Text : Theme.Disabled;
            Color closeColor = closingPrograms ? Theme.Disabled : Theme.Text;
            if (logPanel != null)
            {
                logPanel.Entries = engine.Log;
                logPanel.Warnings = engine.WarningCount;
                logPanel.StopAllButton.ForeColor = stopColor;
                logPanel.CloseAllButton.ForeColor = closeColor;
                logPanel.Invalidate();
            }
            UpdateTitleBar(false);
            if (statusBar != null)
            {
                UpdateStatusBar(infos.Values.ToList());
                statusBar.StopAllButton.ForeColor = stopColor;
                statusBar.CloseAllButton.ForeColor = closeColor;
                statusBar.Invalidate();
            }
        }

        // Main layout status bar: the running launch ("Bench setup: step 4 of 10 · waiting 2 s"), or else the
        // last log line; on the right how many programs are open and any warnings.
        void UpdateStatusBar(List<RunInfo> running)
        {
            if (running.Count > 0)
            {
                var r = running[0];
                statusBar.Message = Theme.Up(r.Profile.Name + ": step " + Math.Min(r.Done + 1, r.Total) + " of " + r.Total + " · " + r.Activity +
                                             (running.Count > 1 ? " · " + (running.Count - 1) + " more running" : ""));
                statusBar.DotColor = Theme.StatusBusy;
            }
            else if (engine.Log.Count > 0)
            {
                var last = engine.Log[engine.Log.Count - 1];
                statusBar.Message = last.Tag + " · " + Theme.Up(last.Text);
                statusBar.DotColor = LogPanel.KindColor(last.Kind);
            }
            else
            {
                statusBar.Message = Theme.Up("Ready");
                statusBar.DotColor = Theme.Disabled;
            }
            string counts = "";
            if (!engine.IsClosing)
            {
                int open = engine.OpenCount;
                counts = open == 1 ? "1 program open" : open + " programs open";
            }
            int warnings = engine.WarningCount;
            if (warnings > 0) counts += (counts.Length > 0 ? " · " : "") + warnings + (warnings == 1 ? " warning" : " warnings");
            statusBar.Counts = Theme.Up(counts);
            statusBar.CountsWarn = warnings > 0;
        }

        // Messages from the launcher itself go to the log, tagged APP.
        void ShowStatus(string text, EngineState state)
        {
            string kind = state == EngineState.Problem ? "warn" : state == EngineState.Done ? "done" : "info";
            engine.Note("APP", kind, text);
        }

        void ShowStatus(string text, EngineState state, IList<string> details)
        {
            ShowStatus(text, state);
            if (details != null)
                foreach (var d in details) engine.Note("APP", state == EngineState.Problem ? "warn" : "info", d);
        }

        // ---- profile list ----

        void RebuildRows()
        {
            if (list == null || profiles == null) return;
            list.SuspendLayout();
            foreach (var c in list.Controls.Cast<Control>().ToList())
            {
                list.Controls.Remove(c);
                c.Dispose();
            }

            bool first = true, main = IsMainLayout;
            foreach (var p in profiles)
            {
                var profile = p;
                ProfileView row;
                if (main) row = new ProfileTile(profile) { Margin = Theme.Pad(7) };
                else row = new ProfileRow(profile) { First = first };
                first = false;
                row.RunButton.Click += (s, e) => Launch(profile);
                row.EditButton.Click += (s, e) => OpenEditor(profile, false);
                // Stop: during the launch it stops the launch; after it, it closes this profile's programs (asks first).
                row.StopButton.Click += (s, e) =>
                {
                    if (engine.IsRunningProfile(profile)) engine.Cancel(profile);
                    else CloseLaunched(profile);
                };
                var r0 = row;
                row.MoreButton.Click += (s, e) => ShowProfileMenu(r0, r0.MoreButton.PointToScreen(new Point(0, r0.MoreButton.Height)));
                row.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowProfileMenu(r0, r0.PointToScreen(e.Location)); };
                tips.SetToolTip(row.RunButton, "Launch " + profile.Name);
                tips.SetToolTip(row.EditButton, "Edit " + profile.Name);
                tips.SetToolTip(row.MoreButton, "More: duplicate, move, export, close its programs, delete");
                tips.SetToolTip(row.StopButton, "Stop: while it launches, stops the launch (programs that already started stay open).\n" +
                                                "After the launch, closes the programs " + profile.Name + " started (asks first).");
                Theme.Apply(row);
                list.Controls.Add(row);
            }

            if (main)
            {
                // the dashed "+ New profile" card
                var newTile = new NewProfileTile { Margin = Theme.Pad(7) };
                newTile.Click += (s, e) => OpenEditor(new Profile { Name = ProfileStore.FreeName("New profile", profiles) }, true);
                Theme.StyleButton(newTile);
                tips.SetToolTip(newTile, "Make a new profile. Import one from the ⋯ menu at the top right.");
                list.Controls.Add(newTile);
                list.ResumeLayout();
                SizeRows();
                RefreshAll();
                return;
            }

            var newBtn = new Button { Text = Theme.Pick("+ New profile", "[+] NEW PROFILE"), Tag = Theme.Link, AutoSize = true, Margin = new Padding(0) };
            newBtn.Click += (s, e) => OpenEditor(new Profile { Name = ProfileStore.FreeName("New profile", profiles) }, true);
            Theme.StyleButton(newBtn);
            var importBtn = new Button { Text = Theme.Pick("Import...", "[IMPORT]"), Tag = Theme.Link, AutoSize = true, Margin = Theme.Pad(6, 0, 0, 0) };
            importBtn.Click += (s, e) => ImportProfiles();
            Theme.StyleButton(importBtn);
            tips.SetToolTip(importBtn, "Add profiles from an exported profile file (or another stick's profiles.json).");
            var addRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Theme.Pad(6, 6, 0, 6), BackColor = Theme.Back };
            addRow.Controls.Add(newBtn);
            addRow.Controls.Add(importBtn);
            list.Controls.Add(addRow);
            list.ResumeLayout();

            SizeRows();
            RefreshAll();
        }

        void SizeRows()
        {
            if (IsMainLayout)
            {
                // As many columns of at least 210 px as fit, sharing the width.
                int gap = Theme.S(14);
                int avail = list.ClientSize.Width - list.Padding.Horizontal;
                int cols = Math.Max(1, avail / (Theme.S(210) + gap));
                int w = Math.Max(Theme.S(160), avail / cols - gap);
                list.SuspendLayout();
                foreach (Control c in list.Controls)
                    if ((c is ProfileTile || c is NewProfileTile) && c.Width != w) c.Width = w;
                list.ResumeLayout();
                return;
            }
            int width = list.ClientSize.Width;
            foreach (var r in list.Controls.OfType<ProfileRow>())
                if (r.Width != width) r.Width = width;
        }

        // ---- actions ----

        async void Launch(Profile p)
        {
            if (engine.IsRunningProfile(p)) return;
            if (checkingPrograms)
            {
                ShowStatus("Wait until the program check is done.", EngineState.Busy);
                return;
            }
            if (closingPrograms || engine.IsClosing)
            {
                ShowStatus("Wait until the launched programs are closed.", EngineState.Busy);
                return;
            }
            if (p.Steps.Count == 0)
            {
                ShowStatus(p.Name + " has no programs yet. Press Edit to add some.", EngineState.Problem);
                return;
            }
            // Before anything starts: ask about programs that aren't where the profile says.
            var skip = new HashSet<Step>();
            var locator = new ProgramLocator();
            bool changed = false;
            for (int i = 0; i < p.Steps.Count; i++)
            {
                var step = p.Steps[i];
                if (!ProgramLocator.IsMissing(step)) continue;
                var answer = AskForMissing(p, i, step, locator, true,
                    () => LookInFolder(p.Steps.Select(st => new KeyValuePair<Profile, Step>(p, st)), null) > 0 && !ProgramLocator.IsMissing(step));
                if (answer == MissingProgramForm.Choice.Use || answer == MissingProgramForm.Choice.Fixed) changed = true;
                else if (answer == MissingProgramForm.Choice.Skip) skip.Add(step);
                else
                {
                    if (changed) { SaveProfiles(); BeginInvoke((Action)RebuildRows); }
                    ShowStatus(p.Name + ": not launched", EngineState.Idle);
                    return;
                }
            }
            if (changed)
            {
                SaveProfiles();
                BeginInvoke((Action)RebuildRows);
            }
            if (skip.Count == p.Steps.Count)
            {
                ShowStatus(p.Name + ": not launched, all programs were skipped", EngineState.Problem);
                return;
            }

            try
            {
                // Don't start anything while the temp folder is still being emptied (it would lose its files).
                if (tempCleanup != null && !tempCleanup.IsCompleted)
                {
                    ShowStatus("Waiting until the temp folder on the stick is emptied...", EngineState.Busy);
                    try { await tempCleanup; } catch { }
                }
                if (engine.IsRunningProfile(p) || engine.IsClosing) return;
                await engine.RunAsync(p, skip, LaunchEngine.TagFor(p, profiles));
            }
            catch (Exception ex)
            {
                ShowStatus("Launch failed: " + ex.Message, EngineState.Problem);
            }
        }

        // Closes the programs the launcher started: all of them, or only the ones of one profile (programs that
        // another profile uses too are left open then).
        async void CloseLaunched(Profile only = null)
        {
            if (closingPrograms) return;
            int open = engine.OpenCount;   // also drops programs that already closed
            string whose = only == null ? "the launcher" : "\"" + only.Name + "\"";
            List<string> shared = new List<string>();
            if (only != null)
            {
                shared = engine.SharedOpenFor(only);
                open = engine.OpenCountFor(only) - shared.Count;
            }
            string sharedNote = shared.Count == 0 ? ""
                : "\n\nLeft open because another profile uses them too: " + string.Join(", ", shared) + ".";
            if (open == 0)
            {
                if (only == null) engine.CancelAll();
                else engine.Cancel(only);
                ShowStatus("No programs " + whose + " started are open" + (shared.Count > 0 ? " (except shared ones)" : ""), EngineState.Idle);
                return;
            }

            var answer = MessageBox.Show(this,
                "Close the " + open + " program(s) " + whose + " started?\n\nThey are asked to close normally first." + sharedNote,
                "Launcher", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK) return;

            closingPrograms = true;
            try
            {
                ShowStatus("Closing programs " + whose + " started...", EngineState.Busy);
                var left = await engine.RequestCloseAsync(5000, only);
                if (left.Count == 0)
                {
                    ShowStatus("Closed the programs " + whose + " started", EngineState.Done);
                    return;
                }

                var force = MessageBox.Show(this,
                    "These programs are still open:\n\n- " + string.Join("\n- ", left) +
                    "\n\nForce them to close? Unsaved work in them will be lost.",
                    "Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (force != DialogResult.Yes)
                {
                    ShowStatus(left.Count + " launched program(s) left open", EngineState.Idle);
                    return;
                }

                ShowStatus("Forcing programs to close...", EngineState.Busy);
                var stuck = await engine.KillRemainingAsync(only);
                if (stuck.Count == 0)
                    ShowStatus("Closed the programs " + whose + " started", EngineState.Done);
                else
                    ShowStatus("Couldn't close " + stuck.Count + " program(s)", EngineState.Problem,
                        stuck.Select(n => n + (AppInfo.IsElevated ? "" : " (it may be running as administrator; restart the launcher as admin to close it)"))
                             .ToList());
            }
            catch (Exception ex)
            {
                ShowStatus("Closing failed: " + ex.Message, EngineState.Problem);
            }
            finally
            {
                closingPrograms = false;
                RefreshAll();
            }
        }

        void RestartAsAdmin()
        {
            if (engine.IsRunning || engine.OpenCount > 0)
            {
                var r = MessageBox.Show(this,
                    "Programs the launcher started will stay open, but the new launcher window won't be able to close them.\n\nRestart anyway?",
                    "Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, Program.RestartedArg)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = PathUtil.Full(PathUtil.ExeDir)
                });
                Close();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // UAC prompt cancelled: keep running as is.
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't restart as administrator:\n" + ex.Message, "Launcher",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void OpenEditor(Profile original, bool isNew)
        {
            if (!isNew && engine.IsRunningProfile(original)) return;   // edit after the launch is done
            using (var ed = new EditorForm(original.Clone(), isNew))
            {
                if (ed.ShowDialog(this) != DialogResult.OK) return;

                if (isNew)
                {
                    if (!ed.Deleted) profiles.Add(ed.Result);
                }
                else
                {
                    int i = profiles.IndexOf(original);
                    if (i >= 0)
                    {
                        if (ed.Deleted) profiles.RemoveAt(i);
                        else profiles[i] = ed.Result;
                        engine.ProfileReplaced(original, ed.Deleted ? null : ed.Result);
                    }
                }
                SaveProfiles();
            }
            // Rebuild after the click that opened the editor has finished (its button is replaced).
            BeginInvoke((Action)RebuildRows);
        }

        // Rebuild the window in the look chosen in Settings (other windows pick it up when they open).
        void ApplyTheme()
        {
            Theme.Use(SettingsStore.Current.Theme);
            SuspendLayout();
            foreach (var c in Controls.Cast<Control>().ToList())
            {
                Controls.Remove(c);
                c.Dispose();
            }
            tips.RemoveAll();
            Font = Theme.Ui;
            BackColor = Theme.Back;
            BuildUi();
            Theme.ApplyToForm(this);
            if (titleBar != null) titleBar.ApplyColors();
            ResumeLayout();
            Invalidate(true);
        }

        // ---- profile menu (the ⋯ button, or right-click on a profile) ----

        void ShowProfileMenu(ProfileView row, Point screen)
        {
            var p = row.Profile;
            int i = profiles.IndexOf(p);
            if (i < 0) return;
            bool running = engine.IsRunningProfile(p);
            engine.DropClosed();   // so the count below is current
            int open = engine.OpenCountFor(p);

            var menu = new ContextMenuStrip();
            Func<string, bool, Action, ToolStripMenuItem> add = (text, enabled, act) => AddItem(menu, text, enabled, act);
            add("Edit...", !running, () => OpenEditor(p, false));
            add("Duplicate", true, () => DuplicateProfile(p));
            menu.Items.Add(new ToolStripSeparator());
            add("Move up", i > 0, () => MoveProfile(p, -1));
            add("Move down", i < profiles.Count - 1, () => MoveProfile(p, 1));
            menu.Items.Add(new ToolStripSeparator());
            add("Export to a file...", true, () => ExportProfile(p));
            add(open > 0 ? "Close its programs (" + open + " open)" : "Close its programs (none open)", open > 0, () => CloseLaunched(p));
            menu.Items.Add(new ToolStripSeparator());
            add("Delete...", !running, () => DeleteProfile(p)).Tag = "danger";
            Theme.StyleMenu(menu);
            menu.Closed += (s, e) => BeginInvoke((Action)menu.Dispose);
            menu.Show(screen);
        }

        void MoveProfile(Profile p, int dir)
        {
            int i = profiles.IndexOf(p), j = i + dir;
            if (i < 0 || j < 0 || j >= profiles.Count) return;
            profiles[i] = profiles[j];
            profiles[j] = p;
            SaveProfiles();
            RebuildRows();
        }

        void DuplicateProfile(Profile p)
        {
            int i = profiles.IndexOf(p);
            if (i < 0) return;
            var copy = p.Clone();
            copy.Name = ProfileStore.FreeName(p.Name + " (copy)", profiles);
            profiles.Insert(i + 1, copy);
            SaveProfiles();
            RebuildRows();
            ShowStatus("Duplicated \"" + p.Name + "\" as \"" + copy.Name + "\"", EngineState.Done);
        }

        void DeleteProfile(Profile p)
        {
            if (engine.IsRunningProfile(p) || !profiles.Contains(p)) return;
            var r = MessageBox.Show(this, "Delete the profile \"" + p.Name + "\"?\n\nPrograms it started stay open.", "Launcher",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) return;
            profiles.Remove(p);
            engine.ProfileReplaced(p, null);
            SaveProfiles();
            RebuildRows();
            ShowStatus("Deleted \"" + p.Name + "\"", EngineState.Idle);
        }

        void ExportProfile(Profile p)
        {
            string path;
            try
            {
                path = ProfileStore.Export(p);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't export \"" + p.Name + "\":\n" + ex.Message, "Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            ShowStatus("Exported \"" + p.Name + "\" to " + path, EngineState.Done);
            var r = MessageBox.Show(this, "\"" + p.Name + "\" was saved as:\n" + path +
                "\n\nCopy that file to another stick and use Import there. Open the folder now?",
                "Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (r == DialogResult.Yes)
                try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
        }

        void ImportProfiles()
        {
            string start = Directory.Exists(ProfileStore.ExportDir) ? ProfileStore.ExportDir : PathUtil.HomeDir;
            string own1 = ProfileStore.FilePath, own2 = SettingsStore.FilePath;
            string file;
            using (var picker = new ProgramPicker(start, null, null, "Import a profile",
                f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !PathUtil.SamePath(f, own1) && !PathUtil.SamePath(f, own2),
                "profile files"))
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                file = picker.SelectedPath;
            }

            List<Profile> found;
            string warning;
            try
            {
                found = ProfileStore.ReadFile(file, out warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't import " + Path.GetFileName(file) + ":\n" + ex.Message, "Launcher",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (found.Count == 0)
            {
                MessageBox.Show(this, "There are no profiles in " + Path.GetFileName(file) + "." + (warning == null ? "" : "\n\n" + warning),
                    "Launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            foreach (var p in found)
            {
                p.Name = ProfileStore.FreeName(p.Name, profiles);
                profiles.Add(p);
            }
            SaveProfiles();
            RebuildRows();
            int missing = found.Sum(p => p.Steps.Count(st => ProgramLocator.IsMissing(st)));
            ShowStatus("Imported " + string.Join(", ", found.Select(p => "\"" + p.Name + "\"")) +
                       (missing > 0 ? " (" + missing + " program(s) not found here; press Check)" : ""),
                       missing > 0 ? EngineState.Problem : EngineState.Done);
            if (warning != null)
                MessageBox.Show(this, warning, "Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // Launcher.exe --run "Bench setup": launches the named profiles once the window is open.
        void RunFromCommandLine()
        {
            foreach (var name in runOnStart)
            {
                var p = profiles.FirstOrDefault(x => string.Equals((x.Name ?? "").Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (p == null) ShowStatus("--run: there is no profile named \"" + name + "\"", EngineState.Problem);
                else Launch(p);
            }
        }

        // ---- the window's own title bar ----
        // The normal Windows frame stays (resizing, shadow, rounded corners, snapping); only its caption is given
        // to the window's content, where the TitleBar draws the launcher's own. Its empty parts count as caption.

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct NCCALCSIZE_PARAMS { public RECT R0, R1, R2; public IntPtr Pos; }

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int index);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // recalculate the frame now that the caption belongs to the content
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0020 /* FRAMECHANGED */ | 0x0001 | 0x0002 | 0x0004 | 0x0010);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (titleBar != null && !titleBar.IsDisposed) UpdateTitleBar(false);   // maximise / restore symbol
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84;
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                var before = (NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(NCCALCSIZE_PARAMS));
                int top = before.R0.Top;
                base.WndProc(ref m);   // Windows' borders on the left, right and bottom
                var after = (NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(NCCALCSIZE_PARAMS));
                // no caption; a maximised window hangs over the screen edge by its frame, so keep that part out
                after.R0.Top = top + (WindowState == FormWindowState.Maximized ? GetSystemMetrics(33) + GetSystemMetrics(92) : 0);
                Marshal.StructureToPtr(after, m.LParam, false);
                return;
            }
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == 1 /* HTCLIENT */ && titleBar != null)
                {
                    long lp = m.LParam.ToInt64();
                    var pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                    if (WindowState != FormWindowState.Maximized && pt.Y < Theme.S(5))
                        m.Result = (IntPtr)(pt.X < Theme.S(12) ? 13 /* HTTOPLEFT */ : pt.X > ClientSize.Width - Theme.S(12) ? 14 /* HTTOPRIGHT */ : 12 /* HTTOP */);
                    else if (pt.Y < titleBar.Height)
                        m.Result = (IntPtr)2;   // HTCAPTION: drag, double-click, snap
                }
                return;
            }
            base.WndProc(ref m);
        }

        // ---- the stick panel (click on the stick in the title bar) ----

        void ShowStickPanel()
        {
            stick = StickInfo.Read();
            stickReadAt = DateTime.Now;
            var info = stick;
            int w = Theme.S(360), pad = Theme.S(16);
            var panel = new Panel { BackColor = Theme.Surface, Width = w, Padding = new Padding(pad) };
            int y = pad;
            const TextFormatFlags f = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

            Func<string, Font, Color, int, Label> label = (text, font, color, height) =>
            {
                var l = new Label { Text = text, Font = font, ForeColor = color, AutoSize = false, AutoEllipsis = true, Location = new Point(pad, y), Size = new Size(w - 2 * pad, height), BackColor = Theme.Surface };
                panel.Controls.Add(l);
                y += height;
                return l;
            };
            label(info == null ? Theme.Up("Launcher folder") : Theme.Up(info.Name), Theme.TileTitle, Theme.Bright, Theme.S(24));
            label(info == null ? PathUtil.Full(PathUtil.HomeDir)
                  : Theme.Up(info.Kind + (info.Format.Length > 0 ? " · " + info.Format : "") + (info.SystemDrive ? " · the PC's own drive" : "")),
                  Theme.Small, Theme.Muted, Theme.S(20));
            y += Theme.S(8);

            // usage: tools, temp, the rest, free
            long tools = -1, temp = -1;
            var bar = new Panel { Location = new Point(pad, y), Size = new Size(w - 2 * pad, Theme.S(38)), BackColor = Theme.Surface };
            Color toolsColor = Color.FromArgb(0x79, 0xA8, 0xF0), tempColor = Theme.StatusBusy;
            bar.Paint += (s, e) =>
            {
                var g = e.Graphics;
                int bw = bar.Width, bh = Theme.S(8);
                using (var b = new SolidBrush(Theme.Track == Theme.Back ? Theme.Hover : Theme.Track)) g.FillRectangle(b, 0, 0, bw, bh);
                if (info != null && info.Total > 0)
                {
                    double used = info.Total - info.Free;
                    int x = 0;
                    Action<double, Color> seg = (bytes, c) =>
                    {
                        int sw = (int)Math.Round(bw * Math.Max(0, bytes) / info.Total);
                        using (var b = new SolidBrush(c)) g.FillRectangle(b, x, 0, sw, bh);
                        x += sw;
                    };
                    if (tools >= 0) seg(tools, toolsColor);
                    if (temp >= 0) seg(temp, tempColor);
                    seg(used - Math.Max(0, tools) - Math.Max(0, temp), info.NearlyFull ? Theme.StatusProblem : Theme.Disabled);
                    TextRenderer.DrawText(g, Theme.Up(PortableEnv.FormatSize(info.Free) + " free of " + PortableEnv.FormatSize(info.Total)), Theme.Small,
                        new Rectangle(0, bh + Theme.S(6), bw, Theme.S(16)), info.NearlyFull ? Theme.StatusProblem : Theme.Muted, f);
                    string parts = tools < 0 ? Theme.Up("adding up...") : Theme.Up("tools " + PortableEnv.FormatSize(tools) + "    temp " + PortableEnv.FormatSize(Math.Max(0, temp)));
                    var tf = f | TextFormatFlags.Right;
                    TextRenderer.DrawText(g, parts, Theme.Small, new Rectangle(0, bh + Theme.S(6), bw, Theme.S(16)), Theme.Muted, tf);
                    if (tools >= 0)
                    {
                        // little colour keys in front of "tools" and "temp"
                        const TextFormatFlags m = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
                        int tw = TextRenderer.MeasureText(g, parts, Theme.Small, Size.Empty, m).Width;
                        int tempW = TextRenderer.MeasureText(g, Theme.Up("temp " + PortableEnv.FormatSize(Math.Max(0, temp))), Theme.Small, Size.Empty, m).Width;
                        int ky = bh + Theme.S(10), k = Theme.S(7);
                        using (var b = new SolidBrush(toolsColor)) g.FillRectangle(b, bw - tw - k - Theme.S(4), ky, k, k);
                        using (var b = new SolidBrush(tempColor)) g.FillRectangle(b, bw - tempW - k - Theme.S(4), ky, k, k);
                    }
                }
                else TextRenderer.DrawText(g, Theme.Up("Free space unknown"), Theme.Small, new Rectangle(0, bh + Theme.S(6), bw, Theme.S(16)), Theme.Muted, f);
            };
            panel.Controls.Add(bar);
            y += bar.Height + Theme.S(4);

            Action<string, string> row = (k, v) =>
            {
                panel.Controls.Add(new Label { Text = Theme.Up(k), Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, Location = new Point(pad, y), Size = new Size(Theme.S(100), Theme.S(20)), BackColor = Theme.Surface });
                panel.Controls.Add(new Label { Text = v, Font = Theme.MonoSmall, ForeColor = Theme.Text, AutoSize = false, AutoEllipsis = true, Location = new Point(pad + Theme.S(104), y), Size = new Size(w - 2 * pad - Theme.S(104), Theme.S(20)), BackColor = Theme.Surface });
                y += Theme.S(20);
            };
            row("Tools folder", PathUtil.ToolsDirFound ? PathUtil.Full(PathUtil.ToolsDir) : "not found");
            row("Launcher data", PathUtil.Full(PathUtil.HomeDir));
            row("Running as", AppInfo.IsElevated ? "administrator" : "normal rights");
            y += Theme.S(8);
            var line = new Panel { Location = new Point(pad, y), Size = new Size(w - 2 * pad, 1), BackColor = Theme.Divider };
            panel.Controls.Add(line);
            y += Theme.S(8);

            var drop = new ToolStripDropDown { Padding = new Padding(1), AutoClose = true };
            Func<string, Color, Action, Button> action = (text, color, act) =>
            {
                var b = new Button { Text = Theme.Up(text), Tag = Theme.Link, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(pad - Theme.S(6), y), Size = new Size(w - 2 * pad + Theme.S(12), Theme.S(32)) };
                b.Click += (s, e) => { drop.Close(); BeginInvoke(act); };
                panel.Controls.Add(b);
                y += Theme.S(32);
                Theme.StyleButton(b);
                b.BackColor = Theme.Surface;
                b.ForeColor = color;
                return b;
            };
            if (info != null) action(info.Removable ? "Open the stick in Explorer" : "Open the drive in Explorer", Theme.Text, () => { try { Process.Start("explorer.exe", "\"" + info.Root + "\""); } catch { } });
            action("Change the tools folder...", Theme.Text, OpenSettings);
            var emptyBtn = action("Empty the temp folder", Theme.Text, EmptyTempFromPanel);
            if (info != null && info.Removable) action("Close everything and eject the stick", Theme.Warning, EjectStick);
            panel.Height = y + pad;

            var host = new ToolStripControlHost(panel) { Margin = new Padding(0), Padding = new Padding(0), AutoSize = false, Size = panel.Size };
            drop.Items.Add(host);
            Theme.StyleDropDown(drop);
            titleBar.Stick.Open = true;
            titleBar.Stick.Invalidate();
            drop.Closed += (s, e) =>
            {
                if (titleBar != null && !titleBar.IsDisposed) { titleBar.Stick.Open = false; titleBar.Stick.Invalidate(); }
                BeginInvoke((Action)drop.Dispose);
            };
            drop.Show(titleBar.Stick, new Point(0, titleBar.Stick.Height + Theme.S(4)));

            // add up the tools folder and the temp folder in the background (a stick can be slow)
            Task.Run(() =>
            {
                long tmp = PortableEnv.TempSize();
                long tl = PathUtil.ToolsDirFound ? PortableEnv.FolderSize(PathUtil.ToolsDir) : 0;
                string tempDir = PathUtil.Trim(PortableEnv.TempDir) + "\\";
                if (PathUtil.ToolsDirFound && tempDir.StartsWith(PathUtil.Trim(PathUtil.ToolsDir) + "\\", StringComparison.OrdinalIgnoreCase)) tl = Math.Max(0, tl - tmp);
                return new[] { tl, tmp };
            }).ContinueWith(t =>
            {
                if (panel.IsDisposed) return;
                tools = t.Result[0];
                temp = t.Result[1];
                emptyBtn.Text = Theme.Up("Empty the temp folder (" + PortableEnv.FormatSize(temp) + ")");
                bar.Invalidate();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        async void EmptyTempFromPanel()
        {
            if (engine.OpenCount > 0)
            {
                var r = MessageBox.Show(this, "Programs the launcher started are still open. Files they have open are skipped, but they may still " +
                    "need other files in the temp folder.\n\nEmpty it anyway?", "Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) return;
            }
            ShowStatus("Emptying the temp folder on the stick...", EngineState.Busy);
            long freed = await Task.Run(() => PortableEnv.EmptyTemp());
            ShowStatus("Emptied the temp folder on the stick (" + PortableEnv.FormatSize(freed) + " freed)", EngineState.Done);
            UpdateTitleBar(true);
        }

        // Closes the programs the launcher started, then quits and has Windows eject the stick. The eject is left
        // to a small helper started from the PC's own drive: Windows can't eject a stick while the launcher itself
        // still runs from it, so the helper waits until the launcher has quit.
        async void EjectStick()
        {
            var info = StickInfo.Read();
            if (info == null || !info.Removable) return;
            var answer = MessageBox.Show(this, "Close every program the launcher started, then eject " + info.Name + " and close the launcher?\n\n" +
                "The programs are asked to close normally first.", "Launcher", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK) return;

            engine.CancelAll();
            if (engine.OpenCount > 0)
            {
                closingPrograms = true;
                try
                {
                    ShowStatus("Closing launched programs before ejecting...", EngineState.Busy);
                    var left = await engine.RequestCloseAsync(5000);
                    if (left.Count > 0)
                    {
                        var force = MessageBox.Show(this, "These programs are still open:\n\n- " + string.Join("\n- ", left) +
                            "\n\nForce them to close so the stick can be ejected? Unsaved work in them will be lost.",
                            "Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                        if (force != DialogResult.Yes) { ShowStatus("Not ejected: programs are still open", EngineState.Problem); return; }
                        var stuck = await engine.KillRemainingAsync();
                        if (stuck.Count > 0) { ShowStatus("Not ejected: couldn't close " + string.Join(", ", stuck), EngineState.Problem); return; }
                    }
                }
                finally
                {
                    closingPrograms = false;
                }
            }

            try
            {
                string ps = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
                string cmd = "Wait-Process -Id " + Process.GetCurrentProcess().Id + " -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 800; " +
                             "(New-Object -ComObject Shell.Application).Namespace(17).ParseName('" + info.Letter + "').InvokeVerb('Eject')";
                var psi = new ProcessStartInfo(ps, "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"" + cmd + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Environment.SystemDirectory   // not on the stick
                };
                foreach (var name in new[] { "TEMP", "TMP", "APPDATA", "LOCALAPPDATA" })
                {
                    string v = PortableEnv.Original(name);
                    if (!string.IsNullOrEmpty(v)) psi.EnvironmentVariables[name] = v;   // the PC's own folders, not the stick
                }
                using (Process.Start(psi)) { }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't start the eject: " + ex.Message, "Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Close();
        }

        void SwitchLook(string look)
        {
            try
            {
                SettingsStore.SaveTheme(look);
            }
            catch (Exception ex)
            {
                // Still switch for this session; it just won't be remembered.
                ShowStatus("Couldn't save the look to " + PathUtil.SettingsFileName + ": " + ex.Message, EngineState.Problem);
            }
            ApplyTheme();
            RebuildRows();
            RefreshAll();
        }

        void SaveProfiles()
        {
            try
            {
                ProfileStore.Save(profiles);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't save profiles.json:\n" + ex.Message, "Launcher",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void ShowToolsFolderState()
        {
            if (!PathUtil.ToolsDirFound)
                ShowStatus("Tools folder not found: " + PathUtil.Full(PathUtil.ToolsDir) + ". Choose it in Settings.", EngineState.Problem);
        }

        void OpenSettings()
        {
            using (var f = new SettingsForm(engine.OpenCount > 0))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                if (f.Warning != null)
                    MessageBox.Show(this, f.Warning, "Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (f.ThemeChanged || f.LayoutChanged) ApplyTheme();
                if (f.LayoutChanged) SizeForLayout();
                RebuildRows();
                if (!PathUtil.ToolsDirFound)
                {
                    ShowToolsFolderState();
                    return;
                }
                ShowStatus("Settings saved. Tools folder: " + PathUtil.Full(PathUtil.ToolsDir), EngineState.Idle);
                if (f.ToolsFolderChanged)
                {
                    var r = MessageBox.Show(this, "The tools folder changed. Check all profiles now and fix the paths of programs that are in a different place?",
                        "Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r == DialogResult.Yes) CheckPrograms(true);
                }
            }
        }

        // Shows the "Program not found" window for one step. When you pick a location, the step's path is
        // updated (as {tools}\... or {app}\... when possible) and the move is remembered for the next ones.
        MissingProgramForm.Choice AskForMissing(Profile p, int index, Step step, ProgramLocator locator, bool launching,
                                                Func<bool> lookInFolder = null)
        {
            string target = ProgramLocator.TargetOf(step);   // the program, or the script of a PowerShell step
            string expected = ProgramLocator.Expected(target);
            string suggestion = locator.Suggest(target);
            using (var f = new MissingProgramForm(p.Name, index + 1, step.DisplayName, expected, suggestion, launching, lookInFolder))
            {
                f.ShowDialog(this);
                if (f.Result == MissingProgramForm.Choice.Use && f.ChosenPath != null)
                {
                    locator.Learn(expected, f.ChosenPath);
                    ProgramLocator.SetTarget(step, PathUtil.MakePortable(f.ChosenPath));
                }
                return f.Result;
            }
        }

        // "Look in a folder": looks for the missing ones among these steps in a folder you choose (the tools folder
        // first), at most 2 levels of subfolders deep, and updates the steps you confirm. Saves the profiles.
        // Returns how many were fixed; fixedLog (if given) gets a line per fix.
        int LookInFolder(IEnumerable<KeyValuePair<Profile, Step>> steps, List<string> fixedLog)
        {
            var items = new List<FolderFixForm.Item>();
            foreach (var kv in steps)
            {
                if (!ProgramLocator.IsMissing(kv.Value)) continue;
                int index = kv.Key.Steps.IndexOf(kv.Value);
                items.Add(new FolderFixForm.Item
                {
                    Where = "\"" + kv.Key.Name + "\", step " + (index + 1) + " · " + kv.Value.DisplayName,
                    Step = kv.Value,
                    Expected = ProgramLocator.Expected(ProgramLocator.TargetOf(kv.Value))
                });
            }
            if (items.Count == 0) return 0;
            using (var f = new FolderFixForm(items, PathUtil.ToolsDirFound ? PathUtil.ToolsDir : PathUtil.HomeDir))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return 0;
                foreach (var fix in f.Fixes)
                {
                    ProgramLocator.SetTarget(fix.Key, PathUtil.MakePortable(fix.Value));
                    if (fixedLog != null)
                    {
                        var it = items.First(i => i.Step == fix.Key);
                        fixedLog.Add(it.Where + " -> " + ProgramLocator.TargetOf(fix.Key));
                    }
                }
                SaveProfiles();
                ShowStatus("Look in a folder: " + f.Fixes.Count + " program path(s) updated", EngineState.Done);
                return f.Fixes.Count;
            }
        }

        // Checks that every program in every profile is where the profile says. Nothing is searched by itself: you
        // can look for all missing ones in a folder you choose (2 levels deep), or go through them one at a time.
        void CheckPrograms(bool alwaysShowResult)
        {
            if (checkingPrograms) return;
            if (engine.IsRunning)
            {
                ShowStatus("Wait until the launches are done, or press Stop all.", EngineState.Busy);
                return;
            }
            checkingPrograms = true;
            if (checkBtn != null) checkBtn.ForeColor = Theme.Disabled;
            try
            {
                var steps = profiles.SelectMany(p => p.Steps.Select((st, i) => new { Profile = p, Step = st, Index = i })).ToList();
                var missingSteps = steps.Where(x => ProgramLocator.IsMissing(x.Step)).ToList();

                if (missingSteps.Count == 0)
                {
                    string ok = "All " + steps.Count + " programs are where the profiles say.";
                    ShowStatus(ok, EngineState.Done);
                    if (alwaysShowResult) MessageBox.Show(this, ok, "Launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var ask = MessageBox.Show(this,
                    missingSteps.Count + " program(s) aren't where the profiles say:\n\n- " +
                    string.Join("\n- ", missingSteps.Select(x => "\"" + x.Profile.Name + "\": " + x.Step.DisplayName)) +
                    "\n\nYes: look for all of them in the tools folder (2 levels of subfolders deep; you can pick another folder there).\n" +
                    "No: go through them one at a time.\nCancel: leave them for now.",
                    "Launcher", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                var fixedList = new List<string>();
                var allMissing = missingSteps.Select(x => new KeyValuePair<Profile, Step>(x.Profile, x.Step)).ToList();
                if (ask == DialogResult.Yes)
                {
                    LookInFolder(allMissing, fixedList);
                    int left = missingSteps.Count(x => ProgramLocator.IsMissing(x.Step));
                    if (left == 0 || MessageBox.Show(this, left + " program(s) are still missing. Go through them one at a time?",
                            "Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        ask = DialogResult.Cancel;
                }
                if (ask == DialogResult.Cancel && fixedList.Count > 0)
                {
                    RebuildRows();
                    var left2 = missingSteps.Where(x => ProgramLocator.IsMissing(x.Step)).Select(x => "Still missing: \"" + x.Profile.Name + "\": " + x.Step.DisplayName).ToList();
                    ShowStatus(fixedList.Count + " program path(s) updated" + (left2.Count > 0 ? ", " + left2.Count + " still missing" : ""),
                        left2.Count > 0 ? EngineState.Problem : EngineState.Done,
                        fixedList.Select(d => "Updated: " + d).Concat(left2).ToList());
                    return;
                }
                if (ask == DialogResult.Cancel)
                {
                    ShowStatus(missingSteps.Count + " program(s) not found", EngineState.Problem,
                        missingSteps.Select(x => "\"" + x.Profile.Name + "\": " + x.Step.DisplayName + " (" + ProgramLocator.Expected(ProgramLocator.TargetOf(x.Step)) + ")").ToList());
                    return;
                }

                var locator = new ProgramLocator();
                var stillMissing = new List<string>();
                bool stopped = false;
                foreach (var x in missingSteps)
                {
                    string where = "\"" + x.Profile.Name + "\": " + x.Step.DisplayName;
                    if (stopped || !ProgramLocator.IsMissing(x.Step))
                    {
                        if (stopped) stillMissing.Add(where);
                        continue;
                    }
                    var step = x.Step;
                    var answer = AskForMissing(x.Profile, x.Index, x.Step, locator, false,
                        () => LookInFolder(allMissing, fixedList) > 0 && !ProgramLocator.IsMissing(step));
                    if (answer == MissingProgramForm.Choice.Use) fixedList.Add(where + " -> " + ProgramLocator.TargetOf(x.Step));
                    else if (answer == MissingProgramForm.Choice.Fixed) { }   // LookInFolder already listed it
                    else
                    {
                        stillMissing.Add(where);
                        if (answer == MissingProgramForm.Choice.Stop) stopped = true;
                    }
                }
                if (fixedList.Count > 0) SaveProfiles();
                RebuildRows();

                var details = fixedList.Select(d => "Updated: " + d).Concat(stillMissing.Select(d => "Still missing: " + d)).ToList();
                string summary = fixedList.Count + " program path(s) updated" +
                                 (stillMissing.Count > 0 ? ", " + stillMissing.Count + " still missing" : "");
                ShowStatus(summary, stillMissing.Count > 0 ? EngineState.Problem : EngineState.Done, details);
            }
            catch (Exception ex)
            {
                ShowStatus("Checking programs failed: " + ex.Message, EngineState.Problem);
            }
            finally
            {
                checkingPrograms = false;
                if (checkBtn != null) checkBtn.ForeColor = Theme.Text;
            }
        }
    }
}
