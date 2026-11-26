using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace LauncherApp
{
    // What a profile row (compact layout) and a profile tile (main layout) have in common: the buttons, the
    // live state, the segment bar (one segment per program) and the status line.
    // Idle: segments show found / missing. Running: segments show each program's live state, Run becomes Stop.
    abstract class ProfileView : Panel
    {
        public readonly Profile Profile;
        public readonly Button EditButton = new Button { Text = "Edit", Tag = Theme.Link };
        public readonly Button RunButton;
        // While busy: "Running · 01:12" with a light sweeping across; pointing at it turns it into Stop.
        public readonly BusyButton StopButton = new BusyButton();
        public readonly Button MoreButton = new Button { Text = "More", Tag = Theme.Link };

        protected readonly bool[] missing;
        protected RunInfo info;
        protected string lastResult;
        protected int openCount;
        DateTime? launchedAt;

        protected const TextFormatFlags LineFlags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        protected ProfileView(Profile p, string runText)
        {
            Profile = p;
            RunButton = new Button { Text = runText, Tag = Theme.Primary };
            missing = p.Steps.Select(st => ProgramLocator.IsMissing(st)).ToArray();
            DoubleBuffered = true;
            ResizeRedraw = true;
            StopButton.Visible = false;
            Theme.MakeIconButton(EditButton, (g, r, c) =>
            {
                int sz = Theme.S(14);
                Icons.Draw(g, "pencil", new RectangleF(r.X + (r.Width - sz) / 2f, r.Y + (r.Height - sz) / 2f, sz, sz), Theme.Dim, 2f);
            });
            Theme.MakeIconButton(MoreButton, (g, r, c) =>
            {
                int sz = Theme.S(16);
                Icons.Draw(g, "dots", new RectangleF(r.X + (r.Width - sz) / 2f, r.Y + (r.Height - sz) / 2f, sz, sz), Theme.Dim, 2f);
            });
            Controls.Add(EditButton);
            Controls.Add(MoreButton);
            Controls.Add(RunButton);
            Controls.Add(StopButton);
        }

        public bool Running { get { return info != null; } }
        public int MissingCount { get { return missing.Count(m => m); } }

        // Background behind the content (the running colour while it runs).
        protected abstract Color Fill { get; }
        protected abstract void LayoutButtons();

        public void UpdateState(RunInfo runInfo, string last, int open, DateTime? launched)
        {
            info = runInfo;
            lastResult = last;
            openCount = open;
            launchedAt = launched;
            RunButton.Visible = !Active;
            EditButton.Visible = !Running;     // editing waits until the launch is done
            StopButton.Visible = Active;
            StopButton.Launching = Running;
            StopButton.Elapsed = launched.HasValue ? DateTime.Now - launched.Value : (TimeSpan?)null;
            LayoutButtons();
            foreach (var b in new[] { EditButton, MoreButton })
            {
                if (b.BackColor != Fill) b.BackColor = Fill;
                b.FlatAppearance.MouseOverBackColor = Theme.Hover;
            }
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutButtons();
        }

        // "Prime95 stress test", or PRIME95_STRESS_TEST in the phosphor look.
        public static string DisplayName(string name)
        {
            string n = (name ?? "").Trim();
            if (Theme.Terminal) n = Regex.Replace(n, @"\s+", "_").ToUpperInvariant();
            return n.Length == 0 ? Theme.Up("Untitled") : n;
        }

        public static string Clock(TimeSpan t)
        {
            return t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.ToString(@"mm\:ss") : t.ToString(@"mm\:ss");
        }

        // Width the segment bar needs when it may use at most maxWidth.
        protected int SegmentsWidth(int maxWidth, out int segW)
        {
            int n = Profile.Steps.Count, gap = Theme.S(2);
            segW = n == 0 ? 0 : Math.Max(Theme.S(3), Math.Min(Theme.S(12), (maxWidth - gap * (n - 1)) / n));
            return n == 0 ? 0 : n * segW + (n - 1) * gap;
        }

        protected void DrawSegments(Graphics g, int x, int y, int segW, int h)
        {
            int gap = Theme.S(2);
            for (int i = 0; i < Profile.Steps.Count; i++)
            {
                DrawSegment(g, new Rectangle(x, y, segW, h), i);
                x += segW + gap;
            }
        }

        // Running: "▶ 00:12 · 2/3 · waiting 2 s". Idle: "3 steps · ~6 s · OK · last: stopped · 2 open".
        protected void DrawStatusLine(Graphics g, Rectangle line)
        {
            int n = Profile.Steps.Count;
            if (info != null)
            {
                string text = "▶ " + Clock(info.Elapsed) + " · " + info.Done + "/" + info.Total + " · " + Theme.Up(info.Activity);
                if (info.SharedNames.Count > 0) text += " · " + Theme.Up(string.Join(", ", info.SharedNames) + " shared");
                TextRenderer.DrawText(g, text, Theme.Small, line, Theme.Accent, LineFlags);
                return;
            }
            int miss = MissingCount;
            var parts = new List<KeyValuePair<string, Color>>
            {
                new KeyValuePair<string, Color>(Theme.Up(n + (n == 1 ? " step" : " steps") + " · ~" + Theme.Seconds(Profile.EstimatedSeconds()) + " · "), Theme.Muted),
                new KeyValuePair<string, Color>(Theme.Up(n == 0 ? "empty" : miss > 0 ? miss + " missing" : "OK"), miss > 0 ? Theme.StatusProblem : Theme.Muted)
            };
            if (lastResult != null && lastResult != "OK")
                parts.Add(new KeyValuePair<string, Color>(Theme.Up(" · last: " + lastResult),
                    lastResult.ToLowerInvariant().Contains("problem") ? Theme.StatusProblem : Theme.Muted));
            if (openCount > 0)
                parts.Add(new KeyValuePair<string, Color>(Theme.Up(" · " + openCount + " open"), Theme.Accent));
            const TextFormatFlags measure = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            int x = line.X;
            foreach (var part in parts)
            {
                if (x >= line.Right) break;
                TextRenderer.DrawText(g, part.Key, Theme.Small, new Rectangle(x, line.Y, line.Right - x, line.Height), part.Value, LineFlags);
                x += TextRenderer.MeasureText(g, part.Key, Theme.Small, Size.Empty, measure).Width;
                if (part.Key.EndsWith(" ")) x += TextRenderer.MeasureText(g, " ", Theme.Small, Size.Empty, measure).Width;
            }
        }

        void DrawSegment(Graphics g, Rectangle r, int i)
        {
            if (info == null)
            {
                FillRect(g, r, missing[i] ? Theme.StatusProblem : Theme.OkDim);
                return;
            }
            var state = i < info.States.Length ? info.States[i] : StepState.Queued;
            switch (state)
            {
                case StepState.Up: FillRect(g, r, Theme.Accent); break;
                case StepState.Starting: FillRect(g, r, Theme.StatusBusy); break;
                case StepState.Failed: FillRect(g, r, Theme.StatusProblem); break;
                case StepState.Exited: FillRect(g, r, Theme.Disabled); break;
                case StepState.Skipped: Outline(g, r, Theme.StatusProblem); break;
                case StepState.Shared: DrawShared(g, r); break;
                default: Outline(g, r, Theme.Disabled); break;
            }
        }

        static void FillRect(Graphics g, Rectangle r, Color c)
        {
            using (var b = new SolidBrush(c)) g.FillRectangle(b, r);
        }

        static void Outline(Graphics g, Rectangle r, Color c)
        {
            var inner = Rectangle.Inflate(r, 0, -Theme.S(1));
            using (var p = new Pen(c)) g.DrawRectangle(p, inner.X, inner.Y, inner.Width - 1, inner.Height - 1);
        }

        // Striped: this program is used by more than one running profile (started once).
        public static void DrawShared(Graphics g, Rectangle r)
        {
            using (var b = new HatchBrush(HatchStyle.WideUpwardDiagonal, Theme.Accent, Theme.BarFill))
                g.FillRectangle(b, r);
            using (var p = new Pen(Theme.Accent))
                g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        // A rectangle with rounded corners (square in the phosphor look).
        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static int Radius { get { return Theme.Terminal ? 0 : Theme.S(8); } }

        // The profile's icon on its own small tile: grey in Dark (the drawings' dark
        // outline needs it), white with a border in Light, dark green in Green phosphor.
        // While the profile is busy (launching, or programs it started are open) its icon moves and the
        // whole card or row breathes a soft green glow. Off when the setting or Windows' animation effects are
        // off; then the card only gets a steady green border.
        public bool Active { get { return Running || openCount > 0; } }
        protected double animTime;

        public void Animate(double t)
        {
            animTime = t;
            StopButton.AnimTime = t;
            if (launchedAt.HasValue) StopButton.Elapsed = DateTime.Now - launchedAt.Value;
            Invalidate();
            StopButton.Invalidate();
        }

        // The glow along the inside of the card's edge: a green border and rings that fade inwards.
        protected void DrawGlow(Graphics g, Rectangle card, int radius)
        {
            var mode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool moving = Theme.Animate;
            double glow = moving ? 0.5 + 0.5 * Math.Sin(animTime * 3) : 0.6;
            int r = Theme.Terminal ? 0 : radius;
            if (moving)
                for (int i = 6; i >= 1; i--)
                {
                    int inset = (int)(Theme.S(1.6f) * i);
                    int alpha = (int)(255 * (0.08 + 0.20 * glow) * (1 - i / 7.0));
                    using (var path = Rounded(Rectangle.Inflate(card, -inset, -inset), Math.Max(0, r - inset)))
                    using (var pen = new Pen(Color.FromArgb(alpha, Theme.Accent), Theme.S(2f)))
                        g.DrawPath(pen, path);
                }
            using (var path = Rounded(card, r))
            using (var pen = new Pen(Color.FromArgb((int)(255 * (0.5 + 0.5 * glow)), Theme.Accent), Theme.S(1.5f)))
                g.DrawPath(pen, path);
            g.SmoothingMode = mode;
        }

        protected void DrawIcon(Graphics g, Rectangle box, int iconSize, int radius)
        {
            var mode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool moving = Active && Theme.Animate;
            Color tile = Theme.Terminal ? Theme.BarFill : Theme.IsDark ? Color.FromArgb(0x2A, 0x2F, 0x36) : Color.White;
            Color border = Theme.Terminal ? Theme.Border : Theme.IsDark ? Color.FromArgb(0x36, 0x3B, 0x42) : Theme.Border;
            int r = Theme.Terminal ? 0 : radius;
            using (var path = Rounded(box, r))
            {
                using (var b = new SolidBrush(tile)) g.FillPath(b, path);
                using (var pen = new Pen(border)) g.DrawPath(pen, path);
            }
            var iconRect = new RectangleF(box.X + (box.Width - iconSize) / 2f, box.Y + (box.Height - iconSize) / 2f, iconSize, iconSize);
            if (moving) ProfileIconArt.DrawRunning(g, Icons.For(Profile), iconRect, animTime);
            else Icons.DrawProfile(g, Icons.For(Profile), iconRect);
            g.SmoothingMode = mode;
        }
    }

    // Compact layout: one line per profile. Icon, name and status line, segments, [edit][more][Run].
    class ProfileRow : ProfileView
    {
        public bool First;

        public ProfileRow(Profile p) : base(p, Theme.Pick("▶ Run", "[ RUN ]"))
        {
            Height = Theme.S(52);
            Margin = new Padding(0);
            BackColor = Theme.Back;
            EditButton.Size = Theme.S(28, 28);
            MoreButton.Size = Theme.S(28, 28);
            RunButton.Size = Theme.S(78, 28);
            StopButton.Size = Theme.S(124, 28);
            LayoutButtons();
        }

        protected override Color Fill { get { return Running ? Theme.RunningBack : Theme.Back; } }

        protected override void LayoutButtons()
        {
            if (BackColor != Fill) BackColor = Fill;
            int pad = Theme.S(8);
            int top = (Height - RunButton.Height) / 2;
            RunButton.Location = new Point(Width - pad - RunButton.Width, top);
            StopButton.Location = new Point(Width - pad - StopButton.Width, top);
            // [edit][more][Run], or [edit][more][Running] while busy ([more][Running] during the launch)
            int mainLeft = Active ? StopButton.Left : RunButton.Left;
            MoreButton.Location = new Point(mainLeft - MoreButton.Width - Theme.S(4), top);
            EditButton.Location = new Point(MoreButton.Left - EditButton.Width, top);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            if (!First)
                using (var pen = new Pen(Theme.Divider))
                    g.DrawLine(pen, 0, 0, Width, 0);
            if (Active) DrawGlow(g, new Rectangle(0, 0, Width - 1, Height - 1), 0);

            int pad = Theme.S(8);
            int icon = Theme.S(34);   // the tile; the drawing inside is 26 px
            DrawIcon(g, new Rectangle(pad, (Height - icon) / 2, icon, icon), Theme.S(26), Theme.S(7));

            // segments, right-aligned before the buttons
            int buttonsLeft = EditButton.Visible ? EditButton.Left : MoreButton.Left;
            int segRight = buttonsLeft - Theme.S(10);
            int segW;
            int segWidth = SegmentsWidth(Theme.S(84), out segW);
            if (segWidth > 0) DrawSegments(g, segRight - segWidth, (Height - Theme.S(9)) / 2, segW, Theme.S(9));

            // name and status line
            int left = pad + icon + Theme.S(10);
            int right = segRight - segWidth - Theme.S(10);
            if (right <= left) return;
            TextRenderer.DrawText(g, DisplayName(Profile.Name), Theme.Terminal ? Theme.Ui : Theme.UiBold,
                new Rectangle(left, Theme.S(9), right - left, Theme.S(18)), Theme.Bright, LineFlags);
            DrawStatusLine(g, new Rectangle(left, Theme.S(29), right - left, Theme.S(15)));
        }
    }

    // Main layout: a card per profile. Icon (top left) and segments (top right), name, status line,
    // and [▶ Launch][edit][more] at the bottom ([■ Stop][more] while it runs).
    class ProfileTile : ProfileView
    {
        public static int TileHeight { get { return Theme.S(158); } }

        public ProfileTile(Profile p) : base(p, Theme.Pick("▶  Launch", "[ LAUNCH ]"))
        {
            Height = TileHeight;
            Width = Theme.S(220);
            BackColor = Theme.Back;   // around the rounded corners
            EditButton.Size = Theme.S(32, 32);
            MoreButton.Size = Theme.S(32, 32);
            RunButton.Height = Theme.S(32);
            StopButton.Height = Theme.S(32);
            LayoutButtons();
        }

        protected override Color Fill { get { return Running ? Theme.RunningBack : Theme.Surface; } }

        protected override void LayoutButtons()
        {
            int pad = Theme.S(14), gap = Theme.S(6);
            int y = Height - pad - RunButton.Height;
            MoreButton.Location = new Point(Width - pad - MoreButton.Width, y);
            EditButton.Location = new Point(MoreButton.Left - gap - EditButton.Width, y);
            int mainRight = (Running ? MoreButton.Left : EditButton.Left) - gap;
            RunButton.Bounds = new Rectangle(pad, y, Math.Max(Theme.S(60), mainRight - pad), RunButton.Height);
            StopButton.Bounds = RunButton.Bounds;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var card = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Rounded(card, Radius))
            {
                using (var b = new SolidBrush(Fill)) g.FillPath(b, path);
                if (!Active) using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
            }
            if (Active) DrawGlow(g, card, Radius);

            int pad = Theme.S(14);
            var box = new Rectangle(pad, pad, Theme.S(42), Theme.S(42));
            DrawIcon(g, box, Theme.S(32), Theme.S(9));
            g.SmoothingMode = SmoothingMode.None;

            // segments, top right
            int segW;
            int segWidth = SegmentsWidth(Math.Min(Theme.S(120), Width - box.Right - 2 * pad), out segW);
            if (segWidth > 0) DrawSegments(g, Width - pad - segWidth, box.Y + (box.Height - Theme.S(9)) / 2, segW, Theme.S(9));

            // name and status line
            int textW = Width - 2 * pad;
            int y = box.Bottom + Theme.S(10);
            TextRenderer.DrawText(g, DisplayName(Profile.Name), Theme.TileTitle,
                new Rectangle(pad, y, textW, Theme.S(20)), Theme.Bright, LineFlags);
            DrawStatusLine(g, new Rectangle(pad, y + Theme.S(24), textW, Theme.S(16)));
        }
    }

    // The dashed "+ New profile" card at the end of the tiles.
    class NewProfileTile : Button
    {
        bool hover;

        public NewProfileTile()
        {
            Text = Theme.Pick("+  New profile", "[+] NEW PROFILE");
            Tag = Theme.Link;
            Height = ProfileTile.TileHeight;
            Width = Theme.S(220);
            Cursor = Cursors.Hand;
            MouseEnter += (s, e) => { hover = true; Invalidate(); };
            MouseLeave += (s, e) => { hover = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Theme.Back)) g.FillRectangle(b, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = ProfileView.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), ProfileView.Radius))
            {
                if (hover) using (var b = new SolidBrush(Theme.Hover)) g.FillPath(b, path);
                using (var pen = new Pen(Focused ? Theme.Accent : Theme.Disabled) { DashStyle = DashStyle.Dash })
                    g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Text, Theme.Ui, ClientRectangle, hover ? Theme.Bright : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    // The editor's icon choice. Closed it looks like a drop-down (small icon, name, arrow); clicking it opens a
    // grid of big icon tiles with their names under them, 5 across. Picking one closes the grid.
    class IconPicker : Button
    {
        const int Columns = 5;
        string selected = "terminal";
        bool hover;

        public event EventHandler SelectedChanged;

        public string Selected
        {
            get { return selected; }
            set { selected = value; Invalidate(); }   // no event: that is only for the user's choice
        }

        public IconPicker()
        {
            Text = "Icon";
            AccessibleName = "Icon";
            Height = Theme.S(28);
            Width = Theme.S(200);
            Cursor = Cursors.Hand;
            MouseEnter += (s, e) => { hover = true; Invalidate(); };
            MouseLeave += (s, e) => { hover = false; Invalidate(); };
            Click += (s, e) => ShowGrid();
        }

        static Color TileColor { get { return Theme.Terminal ? Theme.BarFill : Theme.IsDark ? Color.FromArgb(0x2A, 0x2F, 0x36) : Color.White; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(hover ? Theme.Hover : Theme.Field)) g.FillRectangle(b, ClientRectangle);
            using (var pen = new Pen(Focused ? Theme.Accent : Theme.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            int tile = Height - Theme.S(6);
            var r = new Rectangle(Theme.S(4), (Height - tile) / 2, tile, tile);
            using (var b = new SolidBrush(TileColor)) g.FillRectangle(b, r);
            Icons.DrawProfile(g, selected, new RectangleF(r.X + Theme.S(2), r.Y + Theme.S(2), tile - Theme.S(4), tile - Theme.S(4)));
            int arrowW = Theme.S(22);
            TextRenderer.DrawText(g, Theme.Up(Icons.Label(selected)), Theme.Ui,
                new Rectangle(r.Right + Theme.S(8), 0, Width - r.Right - Theme.S(8) - arrowW, Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            // the drop-down arrow
            float cx = Width - arrowW / 2f, cy = Height / 2f, w = Theme.S(4f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Dim, Theme.S(1.5f)))
                g.DrawLines(pen, new[] { new PointF(cx - w, cy - w / 2), new PointF(cx, cy + w / 2), new PointF(cx + w, cy - w / 2) });
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.F4) { ShowGrid(); e.Handled = true; }
        }

        void ShowGrid()
        {
            int cellW = Theme.S(92), cellH = Theme.S(100), gap = Theme.S(4), pad = Theme.S(8);
            int rows = (Icons.ProfileIcons.Length + Columns - 1) / Columns;
            var panel = new Panel
            {
                BackColor = Theme.Surface,
                Size = new Size(pad * 2 + Columns * cellW + (Columns - 1) * gap, pad * 2 + rows * cellH + (rows - 1) * gap),
                Margin = new Padding(0)
            };
            var drop = new ToolStripDropDown { Padding = new Padding(1), AutoClose = true };
            Button first = null;
            for (int i = 0; i < Icons.ProfileIcons.Length; i++)
            {
                string name = Icons.ProfileIcons[i];
                var tile = new IconTile(name, name == selected)
                {
                    Bounds = new Rectangle(pad + (i % Columns) * (cellW + gap), pad + (i / Columns) * (cellH + gap), cellW, cellH)
                };
                tile.Click += (s, e) =>
                {
                    selected = name;
                    Invalidate();
                    drop.Close();
                    if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
                };
                panel.Controls.Add(tile);
                if (name == selected || first == null) first = tile;
            }
            var host = new ToolStripControlHost(panel) { Margin = new Padding(0), Padding = new Padding(0), AutoSize = false, Size = panel.Size };
            drop.Items.Add(host);
            Theme.StyleDropDown(drop);
            drop.Closed += (s, e) => BeginInvoke((Action)(() => { drop.Dispose(); Focus(); }));
            drop.Show(this, new Point(0, Height));
            if (first != null) first.Focus();
        }

        // One icon in the grid: the icon big on its tile, the name under it. The current one has a green frame.
        class IconTile : Button
        {
            readonly string name;
            readonly bool current;
            bool over;

            public IconTile(string name, bool current)
            {
                this.name = name;
                this.current = current;
                Text = Icons.Label(name);   // for screen readers; drawn by OnPaint
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                Cursor = Cursors.Hand;
                MouseEnter += (s, e) => { over = true; Invalidate(); };
                MouseLeave += (s, e) => { over = false; Invalidate(); };
                GotFocus += (s, e) => Invalidate();
                LostFocus += (s, e) => Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                using (var b = new SolidBrush(over || Focused ? Theme.Hover : Theme.Surface)) g.FillRectangle(b, ClientRectangle);
                int tile = Theme.S(64);
                var r = new Rectangle((Width - tile) / 2, Theme.S(6), tile, tile);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = ProfileView.Rounded(r, Theme.Terminal ? 0 : Theme.S(10)))
                {
                    using (var b = new SolidBrush(TileColor)) g.FillPath(b, path);
                    using (var pen = new Pen(current ? Theme.Accent : (Theme.IsDark && !Theme.Terminal ? Color.FromArgb(0x36, 0x3B, 0x42) : Theme.Border), current ? Theme.S(2f) : 1f))
                        g.DrawPath(pen, path);
                }
                int icon = Theme.S(50);
                Icons.DrawProfile(g, name, new RectangleF(r.X + (tile - icon) / 2f, r.Y + (tile - icon) / 2f, icon, icon));
                TextRenderer.DrawText(g, Theme.Up(Icons.Label(name)), Theme.Small,
                    new Rectangle(0, r.Bottom + Theme.S(4), Width, Height - r.Bottom - Theme.S(4)),
                    current ? Theme.Accent : Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    // The button of a busy profile: "Running · 01:12" with a band of light sweeping across.
    // Pointing at it (or keyboard focus) turns it into a red "Stop". The click is handled by the main window:
    // during the launch it stops the launch, after it it closes the programs the profile started.
    class BusyButton : Button
    {
        bool hover;
        public double AnimTime;
        public TimeSpan? Elapsed;
        public bool Launching;

        public BusyButton()
        {
            Text = "Stop";
            AccessibleName = "Stop";
            Cursor = Cursors.Hand;
            Visible = false;
            MouseEnter += (s, e) => { hover = true; Invalidate(); };
            MouseLeave += (s, e) => { hover = false; Invalidate(); };
            GotFocus += (s, e) => Invalidate();
            LostFocus += (s, e) => Invalidate();
        }

        bool ShowsStop { get { return hover || Focused; } }

        static Color Mix(Color a, Color b, double f)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * f), (int)(a.G + (b.G - a.G) * f), (int)(a.B + (b.B - a.B) * f));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color parentBack = Parent != null ? Parent.BackColor : Theme.Back;
            using (var b = new SolidBrush(parentBack)) g.FillRectangle(b, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Theme.Terminal ? 0 : Theme.S(6);
            const TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

            Color red = Theme.StatusProblem;
            Color stopText = Theme.IsDark ? Mix(red, Color.White, 0.25) : red;
            string label;
            Color back, border, text;
            if (ShowsStop)
            {
                back = Mix(Theme.Surface, red, Theme.IsDark ? 0.16 : 0.10);
                border = red;
                text = stopText;
                label = Theme.Pick("Stop", "[■ STOP]");
            }
            else
            {
                back = Theme.Terminal ? Theme.BarFill : Mix(Theme.Surface, Theme.Accent, Theme.IsDark ? 0.12 : 0.10);
                border = Theme.Terminal ? Theme.Accent : Mix(Theme.Surface, Theme.Accent, 0.6);
                text = Theme.Terminal ? Theme.Accent : Theme.IsDark ? Theme.BarText : Theme.AccentHover;
                string time = Elapsed.HasValue ? " · " + ProfileView.Clock(Elapsed.Value) : "";
                label = Theme.Up((Launching ? "Launching" : "Running") + time);
            }

            using (var path = ProfileView.Rounded(r, radius))
            {
                using (var b = new SolidBrush(back)) g.FillPath(b, path);
                if (!ShowsStop && Theme.Animate)
                {
                    // the band of light: 30 % of the width, sweeping left to right
                    var clip = g.Save();
                    g.SetClip(path);
                    float w = Width * 0.3f;
                    float x = (float)((AnimTime * 0.45 % 1.3) - 0.3) * Width;
                    using (var band = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(x - 1, 0, w + 2, Height),
                               Color.FromArgb(0, Theme.Accent), Color.FromArgb(0, Theme.Accent), 0f))
                    {
                        var blend = new System.Drawing.Drawing2D.ColorBlend
                        {
                            Colors = new[] { Color.FromArgb(0, Theme.Accent), Color.FromArgb(Theme.IsDark ? 70 : 55, Theme.Accent), Color.FromArgb(0, Theme.Accent) },
                            Positions = new[] { 0f, 0.5f, 1f }
                        };
                        band.InterpolationColors = blend;
                        g.FillRectangle(band, x, 0, w, Height);
                    }
                    g.Restore(clip);
                }
                using (var pen = new Pen(border, Theme.S(1.5f))) g.DrawPath(pen, path);
            }

            Font font = Theme.UiBold;
            int textW = TextRenderer.MeasureText(g, label, font, Size.Empty, f).Width;
            int markW = ShowsStop && !Theme.Terminal ? Theme.S(10) + Theme.S(7) : 0;
            int x0 = (Width - textW - markW) / 2;
            if (markW > 0)
                using (var b = new SolidBrush(stopText)) g.FillRectangle(b, x0, (Height - Theme.S(10)) / 2, Theme.S(10), Theme.S(10));
            TextRenderer.DrawText(g, label, font, new Rectangle(x0 + markW, 0, Width - x0 - markW, Height), text, f);
        }
    }
}
