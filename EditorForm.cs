using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LauncherApp
{
    class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    // One program in the profile: reorder buttons, name, start condition, delay and its timeline bar.
    class StepRow : Panel
    {
        public static readonly int TimelineLeft = Theme.S(466);
        public static readonly int TimelineRightGap = Theme.S(44);

        public readonly Step Step;
        public readonly int Number;
        public readonly DoubleBufferedPanel Timeline = new DoubleBufferedPanel();
        public int StartTime;
        public int PrevStartTime;          // start of the program before this one (the delay counts from there)
        public int AxisMax = 30;
        public double Playhead = -1;
        public bool Dragging { get; private set; }
        public NumericUpDown DelayBox { get { return delay; } }

        ComboBox mode;
        NumericUpDown delay;
        TextBox name;

        // Sets the program's name (and so Step.Name) as if it was typed.
        public void SetName(string text)
        {
            name.Text = text ?? "";
        }

        public event EventHandler SelectRequested;
        public event EventHandler TimingChanged;
        public event EventHandler NameChanged;
        public event EventHandler MoveUp;
        public event EventHandler MoveDown;
        public event EventHandler RemoveRequested;

        readonly Button remove;

        static void Raise(EventHandler h, object sender)
        {
            if (h != null) h(sender, EventArgs.Empty);
        }

        public StepRow(Step step, int number, bool isLast)
        {
            Step = step;
            Number = number;
            Height = Theme.S(36);
            Margin = new Padding(0);
            BackColor = Theme.Surface;

            var up = ArrowButton(true, number > 1);
            up.Location = Theme.P(4, 7);
            var down = ArrowButton(false, !isLast);
            down.Location = Theme.P(28, 7);
            var num = new Label { Text = number.ToString(), AutoSize = true, Location = Theme.P(58, 10), ForeColor = Theme.Muted };
            name = new TextBox { Text = step.Name ?? "", Location = Theme.P(80, 7), Width = Theme.S(130) };
            mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = Theme.P(216, 7), Width = Theme.S(172) };
            mode.Items.AddRange(new object[] { "Immediately", "After delay", "When previous runs", "When previous exits" });
            delay = new NumericUpDown { Minimum = 0, Maximum = 3600, Location = Theme.P(394, 7), Width = Theme.S(64) };
            delay.Value = Math.Max(0, Math.Min(3600, step.DelaySeconds));
            mode.SelectedIndex = (int)step.Start;

            Timeline.Location = new Point(TimelineLeft, Theme.S(5));
            Timeline.Height = Theme.S(26);
            Timeline.Width = Theme.S(300);
            Timeline.BackColor = Theme.Track;
            Timeline.Paint += PaintTimeline;
            Timeline.Cursor = Cursors.SizeWE;

            remove = new Button { Text = "✕", Size = Theme.S(28, 22), Top = Theme.S(7) };

            Controls.Add(up);
            Controls.Add(down);
            Controls.Add(num);
            Controls.Add(name);
            Controls.Add(mode);
            Controls.Add(delay);
            Controls.Add(Timeline);
            Controls.Add(remove);

            up.Click += (s, e) => Raise(MoveUp, this);
            down.Click += (s, e) => Raise(MoveDown, this);
            remove.Click += (s, e) => Raise(RemoveRequested, this);
            name.TextChanged += (s, e) => { Step.Name = name.Text; Raise(NameChanged, this); };
            mode.SelectedIndexChanged += (s, e) =>
            {
                Step.Start = (StartMode)mode.SelectedIndex;
                UpdateDelayLook();
                Raise(TimingChanged, this);
            };
            // The delay box always works: typing a delay switches the program to "After delay".
            delay.ValueChanged += (s, e) =>
            {
                Step.DelaySeconds = (int)delay.Value;
                if (Step.Start != StartMode.AfterDelay && delay.Value > 0) mode.SelectedIndex = (int)StartMode.AfterDelay;
                UpdateDelayLook();
                Raise(TimingChanged, this);
            };

            // Drag the timeline bar to change when this program starts.
            Timeline.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                Dragging = true;
                Timeline.Capture = true;
                DragTo(e.X);
            };
            Timeline.MouseMove += (s, e) => { if (Dragging) DragTo(e.X); };
            Timeline.MouseUp += (s, e) =>
            {
                if (!Dragging) return;
                Dragging = false;
                Timeline.Capture = false;
                Raise(TimingChanged, this);   // lets the editor fit the time axis again
                Timeline.Invalidate();
            };

            name.Enter += (s, e) => Raise(SelectRequested, this);
            mode.Enter += (s, e) => Raise(SelectRequested, this);
            delay.Enter += (s, e) => Raise(SelectRequested, this);
            MouseDown += (s, e) => Raise(SelectRequested, this);
            Timeline.MouseDown += (s, e) => Raise(SelectRequested, this);
            num.MouseDown += (s, e) => Raise(SelectRequested, this);

            Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Divider))
                    e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            };

            UpdateDelayLook();
            LayoutChildren();
        }

        // Move up / move down: a drawn triangle, green when it can be used and dim when it can't (first row
        // up, last row down). Drawn by hand because Windows paints disabled button text invisibly dark here.
        static Button ArrowButton(bool up, bool enabled)
        {
            var b = new Button
            {
                Text = up ? "Move up" : "Move down",   // its name for screen readers; the arrow is painted over it
                Size = Theme.S(22, 22),
                Enabled = enabled,
                Tag = Theme.Link,
                AccessibleName = up ? "Move up" : "Move down"
            };
            bool hover = false;
            b.MouseEnter += (s, e) => { hover = true; b.Invalidate(); };
            b.MouseLeave += (s, e) => { hover = false; b.Invalidate(); };
            b.Paint += (s, e) =>
            {
                var g = e.Graphics;
                Color back = b.Enabled && hover ? Theme.Hover : (b.Parent != null ? b.Parent.BackColor : Theme.Surface);
                using (var bg = new SolidBrush(back))
                    g.FillRectangle(bg, b.ClientRectangle);
                float w = Theme.S(8f), h = Theme.S(5f);
                float cx = b.Width / 2f, cy = b.Height / 2f;
                var pts = up
                    ? new[] { new PointF(cx - w / 2, cy + h / 2), new PointF(cx + w / 2, cy + h / 2), new PointF(cx, cy - h / 2) }
                    : new[] { new PointF(cx - w / 2, cy - h / 2), new PointF(cx + w / 2, cy - h / 2), new PointF(cx, cy + h / 2) };
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var fill = new SolidBrush(b.Enabled ? Theme.Accent : Theme.Disabled))
                    g.FillPolygon(fill, pts);
            };
            return b;
        }

        // Delay in grey when it isn't used (Start is not "After delay"), but never disabled.
        public void UpdateDelayLook()
        {
            delay.ForeColor = Step.Start == StartMode.AfterDelay ? Theme.Text : Theme.Muted;
        }

        // Mouse at x in the timeline -> start time in whole seconds -> delay after the previous program.
        void DragTo(int x)
        {
            int w = Math.Max(1, Timeline.Width);
            int t = (int)Math.Round(Math.Max(0, Math.Min(x, w)) / (double)w * AxisMax);
            int d = Math.Max(0, Math.Min(3600, t - PrevStartTime));
            if (Number == 1 && d == 0)
            {
                delay.Value = 0;
                mode.SelectedIndex = (int)StartMode.Immediately;
            }
            else
            {
                mode.SelectedIndex = (int)StartMode.AfterDelay;
                delay.Value = d;
            }
        }

        public void SetSelected(bool selected)
        {
            BackColor = selected ? Theme.Selected : Theme.Surface;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        void LayoutChildren()
        {
            if (remove == null) return; // resize can fire from the constructor before the button exists
            int w = ClientSize.Width;
            remove.Left = w - Theme.S(36);
            Timeline.Left = TimelineLeft;
            Timeline.Width = Math.Max(Theme.S(50), w - TimelineLeft - TimelineRightGap);
        }

        void PaintTimeline(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = Timeline.Width, h = Timeline.Height;
            float pad = Theme.S(3f);
            float x = AxisMax > 0 ? (float)StartTime / AxisMax * w : 0;
            if (x > w - Theme.S(4)) x = w - Theme.S(4);
            // Only a wait for an earlier program is an estimate; the first step always starts at 0.
            bool estimated = Step.WaitsForPrevious && Number > 1;

            using (var fill = new SolidBrush(Theme.BarFill))
                g.FillRectangle(fill, x, pad, w - x, h - 2 * pad);
            using (var pen = new Pen(Theme.BarStripe, Theme.S(Dragging ? 5f : 3f)))
            {
                if (estimated && !Dragging) pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, x + Theme.S(1.5f), pad, x + Theme.S(1.5f), h - pad);
            }
            string label = (estimated ? "~" : "") + Theme.Seconds(StartTime);
            if (Dragging) label += Number == 1 ? "" : "   (+" + Theme.Seconds(Step.DelaySeconds) + " after #" + (Number - 1) + ")";
            // Label right of the start mark, or left of it when there's no room before the end.
            float labelW = g.MeasureString(label, Theme.Small).Width;
            float lx = x + Theme.S(8f) + labelW <= w ? x + Theme.S(8f) : Math.Max(0, x - Theme.S(6f) - labelW);
            using (var br = new SolidBrush(Dragging ? Theme.Bright : Theme.BarText))
                g.DrawString(label, Theme.Small, br, lx, Theme.S(5f));

            if (Playhead >= 0 && AxisMax > 0)
            {
                float px = (float)(Playhead / AxisMax * w);
                using (var p = new Pen(Theme.Playhead, Theme.S(2f)))
                    g.DrawLine(p, px, 0, px, h);
            }
        }
    }

    public class EditorForm : Form
    {
        // Possible spacings between time axis labels, in seconds.
        static readonly int[] AxisSteps = { 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 14400, 43200, 86400 };

        public Profile Result { get; private set; }
        public bool Deleted { get; private set; }

        readonly Profile profile;
        readonly List<StepRow> rows = new List<StepRow>();
        readonly Timer timer = new Timer { Interval = 40 };
        readonly ToolTip tips = new ToolTip();
        readonly string snapshot;

        TextBox nameBox, pathBox, argsBox;
        IconPicker iconBox;
        CheckBox adminBox, minimizedBox;
        NumericUpDown timeoutBox;
        FlowLayoutPanel timeoutRow;
        Label timeoutNote, detailsTitle, emptyLabel;
        TableLayoutPanel detailsForm;
        Button browseBtn;
        FlowLayoutPanel rowsPanel;
        DoubleBufferedPanel axis;
        Label totalLabel;

        int selected = -1;
        int axisMax = 30;
        int axisStep = 10;
        double playhead = -1;
        bool loadingDetails;

        public EditorForm(Profile p, bool isNew)
        {
            profile = p;
            Result = p;

            Text = isNew ? "New profile" : "Edit profile";
            Font = Theme.Ui;
            ClientSize = Theme.S(1000, 740);
            MinimumSize = Theme.S(860, 600);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            BuildUi();
            Theme.ApplyToForm(this);
            nameBox.Text = profile.Name ?? "";
            Shown += (s, e) => nameBox.Select(nameBox.TextLength, 0);   // cursor at the end, nothing highlighted
            iconBox.Selected = Icons.For(profile);
            iconBox.SelectedChanged += (s, e) => profile.Icon = iconBox.Selected;
            RebuildRows();
            snapshot = CurrentJson();

            timer.Tick += (s, e) => TickPreview();
            FormClosing += OnClosingCheckChanges;
            FormClosed += (s, e) =>
            {
                timer.Dispose();
                tips.Dispose();
            };
        }

        void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = Theme.Pad(12, 8, 12, 8) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(60)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(28)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(214)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(48)));
            Controls.Add(root);

            // Top bar: NAME and ICON on the left, PREVIEW and + ADD PROGRAM on the right.
            var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Func<string, Control, Control> field = (label, control) =>
            {
                var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Theme.Pad(0, 0, 16, 0) };
                stack.Controls.Add(new Label { Text = label, AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, Margin = Theme.Pad(0, 0, 0, 3) });
                control.Margin = new Padding(0);
                stack.Controls.Add(control);
                return stack;
            };
            var fields = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = new Padding(0) };
            nameBox = new TextBox { Width = Theme.S(260) };
            fields.Controls.Add(field(Theme.Up("Name"), nameBox));
            iconBox = new IconPicker { Width = Theme.S(200) };
            fields.Controls.Add(field(Theme.Up("Icon"), iconBox));
            top.Controls.Add(fields, 0, 0);

            var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Margin = new Padding(0) };
            var preview = new Button { Text = Theme.Pick("▶ Preview", "▶ PREVIEW"), AutoSize = true, MinimumSize = Theme.S(0, 32), Margin = Theme.Pad(0, 0, 8, 0) };
            preview.Click += (s, e) => StartPreview();
            tips.SetToolTip(preview, "Play the timeline: shows when each program would start.");
            var add = new Button { Text = AddLabel, AutoSize = true, MinimumSize = Theme.S(0, 32), Tag = Theme.Outline, Margin = new Padding(0) };
            add.Click += (s, e) => AddProgram();
            actions.Controls.Add(preview);
            actions.Controls.Add(add);
            top.Controls.Add(actions, 1, 0);
            root.Controls.Add(top, 0, 0);

            // Column headers + time axis
            var header = new Panel { Dock = DockStyle.Fill };
            header.Controls.Add(new Label { Text = Theme.Up("Program"), AutoSize = true, Font = Theme.Small, Location = Theme.P(80, 8), ForeColor = Theme.Muted });
            header.Controls.Add(new Label { Text = Theme.Up("Start"), AutoSize = true, Font = Theme.Small, Location = Theme.P(216, 8), ForeColor = Theme.Muted });
            header.Controls.Add(new Label { Text = Theme.Up("Delay (s)"), AutoSize = true, Font = Theme.Small, Location = Theme.P(394, 8), ForeColor = Theme.Muted });
            axis = new DoubleBufferedPanel { Location = new Point(StepRow.TimelineLeft, Theme.S(2)), Size = Theme.S(300, 24) };
            axis.Paint += PaintAxis;
            header.Controls.Add(axis);
            root.Controls.Add(header, 0, 1);

            // Rows
            rowsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Theme.Surface,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0)
            };
            rowsPanel.ClientSizeChanged += (s, e) => SyncWidths();
            root.Controls.Add(rowsPanel, 0, 2);

            // Details of the selected program: a framed section with a title, and either a clear
            // "nothing selected" message or a two-column form (program on the left, options on the right).
            // Nothing in it is ever disabled: Windows draws disabled text almost invisibly on this background.
            var frame = new Panel { Dock = DockStyle.Fill, Padding = Theme.Pad(14, 10, 14, 10), Margin = Theme.Pad(0, 8, 0, 0) };
            frame.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, frame.Width - 1, frame.Height - 1);
            };
            var section = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            detailsTitle = new Label { AutoSize = true, Font = Theme.UiBold, ForeColor = Theme.Terminal ? Theme.Accent : Theme.Bright, Margin = Theme.Pad(0, 0, 0, 8) };
            section.Controls.Add(detailsTitle, 0, 0);

            var body = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            emptyLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.Muted,
                Text = "No program selected.\nAdd one with \"" + AddLabel + "\" (top right), or click a program in the list above."
            };

            detailsForm = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            detailsForm.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            detailsForm.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

            // Left: program and arguments
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Theme.Pad(0, 0, 16, 0) };
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            left.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Func<string, Label> heading = t => new Label { Text = t, AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, Margin = Theme.Pad(0, 0, 0, 3) };

            pathBox = new TextBox { Dock = DockStyle.Fill, Margin = Theme.Pad(0, 0, 6, 10) };
            argsBox = new TextBox { Dock = DockStyle.Fill, Margin = Theme.Pad(0, 0, 0, 8) };
            browseBtn = new Button { Text = "Browse...", AutoSize = true, Margin = Theme.Pad(0, 0, 0, 10) };
            browseBtn.Click += (s, e) => Browse();
            pathBox.TextChanged += (s, e) =>
            {
                if (!loadingDetails && InRange(selected)) profile.Steps[selected].ProgramPath = pathBox.Text;
            };
            argsBox.TextChanged += (s, e) =>
            {
                if (!loadingDetails && InRange(selected)) profile.Steps[selected].Arguments = argsBox.Text;
            };
            var pathHeading = heading(Theme.Up("Program"));
            left.Controls.Add(pathHeading, 0, 0);
            left.SetColumnSpan(pathHeading, 2);
            left.Controls.Add(pathBox, 0, 1);
            left.Controls.Add(browseBtn, 1, 1);
            var argsHeading = heading(Theme.Up("Arguments"));
            left.Controls.Add(argsHeading, 0, 2);
            left.SetColumnSpan(argsHeading, 2);
            left.Controls.Add(argsBox, 0, 3);
            left.SetColumnSpan(argsBox, 2);
            var hint = new Label
            {
                Text = "{tools} = the tools folder, {app} = the launcher's folder: paths keep working on any drive letter. " +
                       "A .ps1 script you pick is set up to run through PowerShell.",
                AutoSize = true,
                MaximumSize = Theme.S(520, 0),
                ForeColor = Theme.Muted,
                Font = Theme.Small,
                Margin = new Padding(0)
            };
            left.Controls.Add(hint, 0, 4);
            left.SetColumnSpan(hint, 2);
            detailsForm.Controls.Add(left, 0, 0);

            // Right: options, each with a short explanation under it
            var right = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = Theme.Pad(16, 0, 0, 0), Margin = new Padding(0) };
            right.Paint += (s, e) =>
            {
                using (var pen = new Pen(Theme.Divider))
                    e.Graphics.DrawLine(pen, 0, 0, 0, right.Height);
            };
            Func<string, Label> note = t => new Label { Text = t, AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, Margin = Theme.Pad(22, 0, 0, 8) };
            right.Controls.Add(heading(Theme.Up("Options")));

            adminBox = new CheckBox { Text = "Run as administrator", AutoSize = true, Margin = Theme.Pad(0, 2, 0, 0) };
            adminBox.CheckedChanged += (s, e) =>
            {
                if (!loadingDetails && InRange(selected)) profile.Steps[selected].RunAsAdmin = adminBox.Checked;
            };
            right.Controls.Add(adminBox);
            right.Controls.Add(note(AppInfo.IsElevated
                ? "The launcher already runs as admin, so this program will too."
                : "Windows asks for permission (UAC) when it starts."));

            minimizedBox = new CheckBox { Text = "Start minimized", AutoSize = true, Margin = Theme.Pad(0, 2, 0, 0) };
            minimizedBox.CheckedChanged += (s, e) =>
            {
                if (!loadingDetails && InRange(selected)) profile.Steps[selected].StartMinimized = minimizedBox.Checked;
            };
            right.Controls.Add(minimizedBox);
            right.Controls.Add(note("Opens in the taskbar. Some programs ignore this."));

            // "Wait up to [60] s for the previous program": only shown when the Start setting uses it.
            timeoutRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Theme.Pad(0, 2, 0, 0) };
            timeoutRow.Controls.Add(new Label { Text = "Wait up to", AutoSize = true, Margin = Theme.Pad(0, 5, 6, 0) });
            timeoutBox = new NumericUpDown { Minimum = 1, Maximum = Step.MaxTimeoutSeconds, Width = Theme.S(70), Margin = Theme.Pad(0, 2, 6, 0) };
            timeoutBox.ValueChanged += (s, e) =>
            {
                if (!loadingDetails && InRange(selected)) profile.Steps[selected].TimeoutSeconds = (int)timeoutBox.Value;
            };
            timeoutRow.Controls.Add(timeoutBox);
            timeoutRow.Controls.Add(new Label { Text = "s for the previous program", AutoSize = true, Margin = Theme.Pad(0, 5, 0, 0) });
            right.Controls.Add(timeoutRow);
            timeoutNote = new Label
            {
                AutoSize = true,
                MaximumSize = Theme.S(360, 0),
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Margin = Theme.Pad(0, 2, 0, 0),
                Text = "Waiting time: used when Start is \"When previous runs\" or \"When previous exits\"."
            };
            right.Controls.Add(timeoutNote);
            detailsForm.Controls.Add(right, 1, 0);

            body.Controls.Add(detailsForm);
            body.Controls.Add(emptyLabel);
            section.Controls.Add(body, 0, 1);
            frame.Controls.Add(section);
            root.Controls.Add(frame, 0, 3);

            // Bottom bar
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var del = new Button { Text = "Delete profile", AutoSize = true, Anchor = AnchorStyles.Left, Tag = Theme.Stop };
            del.Click += (s, e) => DeleteProfile();
            totalLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Theme.Muted, Margin = Theme.Pad(16, 0, 0, 0) };
            var save = new Button { Text = "Save", Width = Theme.S(90), Height = Theme.S(30), Anchor = AnchorStyles.Right, Tag = Theme.Primary };
            save.Click += (s, e) => Save();
            var cancel = new Button { Text = "Cancel", Width = Theme.S(90), Height = Theme.S(30), Anchor = AnchorStyles.Right, DialogResult = DialogResult.Cancel };
            bottom.Controls.Add(del, 0, 0);
            bottom.Controls.Add(totalLabel, 1, 0);
            bottom.Controls.Add(cancel, 2, 0);
            bottom.Controls.Add(save, 3, 0);
            cancel.Margin = Theme.Pad(0, 3, 8, 3);
            root.Controls.Add(bottom, 0, 4);
            AcceptButton = save;
            CancelButton = cancel;
        }

        static string AddLabel { get { return Theme.Pick("+ Add program", "+ ADD PROGRAM"); } }

        bool InRange(int i)
        {
            return i >= 0 && i < profile.Steps.Count;
        }

        // ---- unsaved changes ----

        string CurrentJson()
        {
            var c = profile.Clone();
            c.Name = nameBox.Text.Trim();
            return ProfileStore.ToJson(c);
        }

        void OnClosingCheckChanges(object sender, FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK) return;
            if (CurrentJson() == snapshot) return;
            var r = MessageBox.Show(this, "Discard your changes to this profile?", "Launcher",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) e.Cancel = true;
        }

        // ---- rows ----

        int RowWidth()
        {
            return Math.Max(Theme.S(600), rowsPanel.ClientSize.Width);
        }

        void SyncWidths()
        {
            int w = RowWidth();
            foreach (var r in rows) if (r.Width != w) r.Width = w;
            if (emptyRowsLabel != null) emptyRowsLabel.Width = w;
            axis.Width = Math.Max(Theme.S(50), rowsPanel.ClientSize.Width - StepRow.TimelineLeft - StepRow.TimelineRightGap);
            Recalc();
        }

        Label emptyRowsLabel;

        void RebuildRows()
        {
            rowsPanel.SuspendLayout();
            foreach (var r in rows)
            {
                rowsPanel.Controls.Remove(r);
                r.Dispose();
            }
            rows.Clear();

            for (int i = 0; i < profile.Steps.Count; i++)
            {
                var row = new StepRow(profile.Steps[i], i + 1, i == profile.Steps.Count - 1);
                row.Width = RowWidth();
                tips.SetToolTip(row.Timeline, "Drag to set when this program starts (whole seconds after the program before it).");
                tips.SetToolTip(row.DelayBox, "Seconds to wait after the previous program started. Typing a delay switches Start to \"After delay\".");
                Theme.Apply(row);
                row.UpdateDelayLook();   // after the theme, which sets every number box to the normal text colour
                row.SelectRequested += (s, e) => SelectRow(rows.IndexOf((StepRow)s));
                row.TimingChanged += (s, e) => { Recalc(); UpdateOptionsEnabled(); };
                row.NameChanged += (s, e) => UpdateDetailsTitle();
                row.MoveUp += (s, e) => MoveStep(rows.IndexOf((StepRow)s), -1);
                row.MoveDown += (s, e) => MoveStep(rows.IndexOf((StepRow)s), 1);
                row.RemoveRequested += (s, e) => RemoveStep(rows.IndexOf((StepRow)s));
                rows.Add(row);
                rowsPanel.Controls.Add(row);
            }

            // Empty profile: say what to do instead of showing a blank list.
            if (emptyRowsLabel != null)
            {
                rowsPanel.Controls.Remove(emptyRowsLabel);
                emptyRowsLabel.Dispose();
                emptyRowsLabel = null;
            }
            if (profile.Steps.Count == 0)
            {
                emptyRowsLabel = new Label
                {
                    Text = "No programs yet.\nPress \"" + AddLabel + "\" (top right) to pick the first one from your tools folder.",
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Theme.Muted,
                    Size = new Size(RowWidth(), Theme.S(120)),
                    Margin = new Padding(0)
                };
                rowsPanel.Controls.Add(emptyRowsLabel);
            }
            rowsPanel.ResumeLayout();

            if (selected >= profile.Steps.Count) selected = profile.Steps.Count - 1;
            if (selected < 0 && profile.Steps.Count > 0) selected = 0;
            SyncWidths();
            SelectRow(selected);
        }

        // Picks the label spacing so labels don't overlap, then the axis length as a multiple of it.
        void ChooseAxis(int last, int dragStart)
        {
            int oldMax = axisMax, oldStep = axisStep;
            int width = Math.Max(1, axis.Width);
            // While a bar is dragged the scale holds still, so the bar stays under the mouse. It only grows when
            // the dragged bar itself gets near the right end (to drag further out); it fits again on release.
            int target = dragStart >= 0 ? (dragStart > oldMax * 0.9 ? dragStart : -1) : last;
            if (target < 0) return;
            foreach (int st in AxisSteps)
            {
                axisStep = st;
                axisMax = Math.Max(30, (target / st + 1) * st);
                // Labels like "2 min 30 s" (step under a minute on a long axis) need more room.
                int minGap = Theme.S(st < 60 && axisMax > 120 ? 84 : 60);
                if ((axisMax / st) * minGap <= width) break;
            }
            if (dragStart >= 0 && axisMax < oldMax)
            {
                axisMax = oldMax;
                axisStep = oldStep;
            }
        }

        void Recalc()
        {
            if (totalLabel == null) return; // layout events can fire while the window is still being built
            var times = profile.StartTimes();
            int last = times.Length > 0 ? times[times.Length - 1] : 0;
            int dragIndex = rows.FindIndex(r => r.Dragging);
            ChooseAxis(last, dragIndex >= 0 && dragIndex < times.Length ? times[dragIndex] : -1);

            for (int i = 0; i < rows.Count && i < times.Length; i++)
            {
                rows[i].StartTime = times[i];
                rows[i].PrevStartTime = i > 0 ? times[i - 1] : 0;
                rows[i].AxisMax = axisMax;
                rows[i].Timeline.Invalidate();
            }
            axis.Invalidate();

            totalLabel.Text = profile.Steps.Count == 0
                ? "No programs yet. Add one to begin."
                : "All programs started after about " + Theme.Seconds(last);
        }

        void SelectRow(int i)
        {
            selected = InRange(i) ? i : -1;
            for (int k = 0; k < rows.Count; k++) rows[k].SetSelected(k == selected);

            loadingDetails = true;
            bool ok = selected >= 0;
            var step = ok ? profile.Steps[selected] : null;
            detailsForm.Visible = ok;
            emptyLabel.Visible = !ok;
            pathBox.Text = ok ? (step.ProgramPath ?? "") : "";
            argsBox.Text = ok ? (step.Arguments ?? "") : "";
            adminBox.Checked = ok && step.RunAsAdmin;
            minimizedBox.Checked = ok && step.StartMinimized;
            timeoutBox.Value = ok ? step.EffectiveTimeout : Step.DefaultTimeoutSeconds;
            loadingDetails = false;
            UpdateOptionsEnabled();
            UpdateDetailsTitle();
        }

        void UpdateOptionsEnabled()
        {
            bool waits = InRange(selected) && selected > 0 && profile.Steps[selected].WaitsForPrevious;
            timeoutRow.Visible = waits;
            timeoutNote.Visible = !waits;
        }

        void UpdateDetailsTitle()
        {
            detailsTitle.Text = InRange(selected)
                ? Theme.Up("Selected program") + " · " + (selected + 1) + " · " + Theme.Up(profile.Steps[selected].DisplayName)
                : Theme.Up("Selected program");
        }

        void MoveStep(int i, int dir)
        {
            int j = i + dir;
            if (!InRange(i) || !InRange(j)) return;
            var tmp = profile.Steps[i];
            profile.Steps[i] = profile.Steps[j];
            profile.Steps[j] = tmp;
            selected = j;
            BeginInvoke((Action)RebuildRows);
        }

        void RemoveStep(int i)
        {
            if (!InRange(i)) return;
            profile.Steps.RemoveAt(i);
            selected = Math.Min(i, profile.Steps.Count - 1);
            BeginInvoke((Action)RebuildRows);
        }

        // ---- actions ----

        // Opens the program picker in the tools folder. Returns the chosen full path, or null.
        string PickProgram(string current)
        {
            string currentFull = null;
            if (!string.IsNullOrWhiteSpace(current))
            {
                string r = PathUtil.ResolveProgram(current);
                if (PathUtil.IsRooted(r)) currentFull = r;
            }
            // The current program is gone: open as "find it", with likely matches first.
            string lookingFor = currentFull != null && !PathUtil.Exists(currentFull) ? currentFull : null;
            using (var picker = new ProgramPicker(PathUtil.ToolsDir, currentFull, lookingFor))
                return picker.ShowDialog(this) == DialogResult.OK ? picker.SelectedPath : null;
        }

        static bool IsScript(string full)
        {
            return string.Equals(Path.GetExtension(full), ".ps1", StringComparison.OrdinalIgnoreCase);
        }

        // A .ps1 script can't be started directly, so it runs through PowerShell.
        static string ScriptArguments(string full)
        {
            return "-NoExit -ExecutionPolicy Bypass -File \"" + PathUtil.MakePortable(full) + "\"";
        }

        void AddProgram()
        {
            string full = PickProgram(null);
            if (full == null) return;

            bool script = IsScript(full);
            profile.Steps.Add(new Step
            {
                Name = Path.GetFileNameWithoutExtension(full),
                ProgramPath = script ? "powershell.exe" : PathUtil.MakePortable(full),
                Arguments = script ? ScriptArguments(full) : "",
                Start = profile.Steps.Count == 0 ? StartMode.Immediately : StartMode.AfterDelay,
                DelaySeconds = 2
            });
            selected = profile.Steps.Count - 1;
            RebuildRows();
            if (rows.Count > 0) rowsPanel.ScrollControlIntoView(rows[rows.Count - 1]);
        }

        void Browse()
        {
            if (!InRange(selected)) return;
            var step = profile.Steps[selected];
            string oldTarget = ProgramLocator.TargetOf(step);
            // For a PowerShell script step, look for the script rather than powershell.exe.
            string full = PickProgram(oldTarget != step.ProgramPath ? oldTarget : pathBox.Text);
            if (full == null) return;

            // The step was named after its old program (the default)? Then the name follows the new one.
            string oldStem = "";
            try { oldStem = Path.GetFileNameWithoutExtension(PathUtil.Expand(oldTarget).Trim().Trim('"')); } catch (ArgumentException) { }
            if (string.IsNullOrWhiteSpace(step.Name) || string.Equals(step.Name.Trim(), oldStem, StringComparison.OrdinalIgnoreCase))
                rows[selected].SetName(Path.GetFileNameWithoutExtension(full));

            if (IsScript(full))
            {
                pathBox.Text = "powershell.exe";
                argsBox.Text = ScriptArguments(full);
            }
            else
            {
                pathBox.Text = PathUtil.MakePortable(full);
            }
        }

        void Save()
        {
            string name = nameBox.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, "Give the profile a name.", "Launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
                nameBox.Focus();
                return;
            }
            for (int i = 0; i < profile.Steps.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(profile.Steps[i].ProgramPath))
                {
                    SelectRow(i);
                    MessageBox.Show(this, "Step " + (i + 1) + " has no program yet. Pick one with Browse.", "Launcher",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    pathBox.Focus();
                    return;
                }
            }
            profile.Name = name;
            DialogResult = DialogResult.OK;
        }

        void DeleteProfile()
        {
            var r = MessageBox.Show(this, "Delete the profile \"" + nameBox.Text + "\"?", "Launcher",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
            Deleted = true;
            DialogResult = DialogResult.OK;
        }

        // ---- timeline preview ----

        void StartPreview()
        {
            playhead = 0;
            timer.Start();
        }

        void TickPreview()
        {
            playhead += axisMax / 100.0;
            if (playhead > axisMax)
            {
                playhead = -1;
                timer.Stop();
            }
            foreach (var r in rows)
            {
                r.Playhead = playhead;
                r.Timeline.Invalidate();
            }
        }

        void PaintAxis(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = axis.Width;
            for (int t = 0; t <= axisMax; t += axisStep)
            {
                string txt = Theme.Seconds(t);
                var size = g.MeasureString(txt, Theme.Small);
                float x = (float)t / axisMax * w;
                float tx = t == 0 ? 0 : (t >= axisMax ? w - size.Width : x - size.Width / 2);
                using (var b = new SolidBrush(Theme.Muted))
                    g.DrawString(txt, Theme.Small, b, tx, Theme.S(4f));
            }
        }
    }
}
