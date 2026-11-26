using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace LauncherApp
{
    // What the title bar knows about the drive the launcher runs from (mockups N1/N2).
    class StickInfo
    {
        public string Root = "";          // "E:\"
        public string Letter = "";        // "E:"
        public string Label = "";         // "TESTKIT"
        public string Kind = "";          // "USB stick", "Drive", ...
        public string Format = "";        // "exFAT"
        public long Free, Total;
        public bool Removable;            // a USB stick (can be ejected)
        public bool SystemDrive;          // the PC's own Windows drive: not a stick at all

        public string Name { get { return (Label.Length > 0 ? Label + " (" + Letter + ")" : Kind + " (" + Letter + ")"); } }
        public double UsedFraction { get { return Total > 0 ? 1 - (double)Free / Total : 0; } }

        // Nearly full: under 1 GB or under 5 % free.
        public bool NearlyFull
        {
            get
            {
#if DEBUG
                // Debug builds only: LAUNCHER_FAKE_FULL=1 shows the "almost full" warning on any drive (for testing).
                if (Environment.GetEnvironmentVariable("LAUNCHER_FAKE_FULL") == "1") return true;
#endif
                return Total > 0 && (Free < 1L << 30 || Free < Total / 20);
            }
        }

        public static StickInfo Read()
        {
            try
            {
                string root = Path.GetPathRoot(PathUtil.Full(PathUtil.HomeDir));
                if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\")) return null;
                var d = new DriveInfo(root);
                var info = new StickInfo { Root = root, Letter = PathUtil.Trim(root), Removable = d.DriveType == DriveType.Removable };
                info.Kind = d.DriveType == DriveType.Removable ? "USB stick" : d.DriveType == DriveType.Fixed ? "Drive" : d.DriveType.ToString();
                string system = Path.GetPathRoot(Environment.SystemDirectory) ?? "";
                info.SystemDrive = string.Equals(system, root, StringComparison.OrdinalIgnoreCase);
                if (info.SystemDrive) info.Kind = "Local disk";
                if (d.IsReady)
                {
                    info.Label = d.VolumeLabel ?? "";
                    info.Format = d.DriveFormat ?? "";
                    info.Free = d.AvailableFreeSpace;
                    info.Total = d.TotalSize;
                }
                return info;
            }
            catch
            {
                return null;
            }
        }
    }

    // The colours and fonts of the title bar: square blocks in one calm colour on a darker bar, capitals in
    // Bahnschrift, green only for what is live (running, the stick's usage), red for close and a full stick.
    static class Lcars
    {
        public static Color Back, Block, Ink, InkDim, Accent, Red, Warn, Line;
        static Font label, small, big;

        public static void Use()
        {
            Back = Theme.StatusBar;
            Ink = Theme.Text;
            InkDim = Theme.Muted;
            Accent = Theme.Accent;
            Red = Theme.StatusProblem;
            Warn = Theme.Warning;
            Line = Theme.Divider;
            Block = Theme.Terminal ? Theme.Field : Theme.IsDark ? Hex(0x24272C) : Color.White;
        }

        // Bahnschrift SemiBold SemiCondensed comes with Windows 10 and 11; Segoe UI Semibold otherwise.
        public static Font Label { get { return label ?? (label = Make(11.25f)); } }   // 15 px
        public static Font Small { get { return small ?? (small = Make(9f, true)); } }  // 12 px
        public static Font Big { get { return big ?? (big = Make(14f)); } }             // 19 px

        static Font Make(float pt, bool light = false)
        {
            foreach (var name in light ? new[] { "Bahnschrift SemiCondensed", "Segoe UI" } : new[] { "Bahnschrift SemiBold SemiConden", "Bahnschrift SemiCondensed", "Segoe UI Semibold" })
            {
                try
                {
                    using (var family = new FontFamily(name))
                        if (family.IsStyleAvailable(FontStyle.Regular)) return new Font(name, pt);
                }
                catch (ArgumentException) { }
            }
            return new Font(SystemFonts.MessageBoxFont.FontFamily, pt, FontStyle.Bold);
        }

        static Color Hex(int rgb) { return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb((int)(a.R * t + b.R * (1 - t)), (int)(a.G * t + b.G * (1 - t)), (int)(a.B * t + b.B * (1 - t)));
        }

        public static Color Hover(Color c) { return Theme.IsDark ? Mix(Color.White, c, 0.09) : Mix(Color.Black, c, 0.06); }
        public static Color Down(Color c) { return Theme.IsDark ? Mix(Color.White, c, 0.16) : Mix(Color.Black, c, 0.12); }

        public static void Fill(Graphics g, Rectangle r, Color c)
        {
            using (var b = new SolidBrush(c)) g.FillRectangle(b, r);
        }

        public const TextFormatFlags Centre = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                                              TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

        // Paints a button as a block with a centred capital label: lighter under the mouse, more when pressed,
        // a dotted frame when it has the keyboard focus. "⋯" draws three dots; "(C)" puts the letter in a circle.
        public static void Attach(Button b, Func<string> text, Func<Color> ink = null)
        {
            bool hover = false, down = false, keyFocus = false;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.MouseEnter += (s, e) => { hover = true; b.Invalidate(); };
            b.MouseLeave += (s, e) => { hover = false; down = false; b.Invalidate(); };
            b.MouseDown += (s, e) => { down = true; b.Invalidate(); };
            b.MouseUp += (s, e) => { down = false; b.Invalidate(); };
            b.GotFocus += (s, e) => { keyFocus = Control.MouseButtons == MouseButtons.None; b.Invalidate(); };
            b.LostFocus += (s, e) => { keyFocus = false; b.Invalidate(); };
            b.Paint += (s, e) =>
            {
                var g = e.Graphics;
                var r = b.ClientRectangle;
                Fill(g, r, down ? Down(Block) : hover ? Hover(Block) : Block);
                // greyed out while disabled or busy (e.g. CHECK while a check runs sets ForeColor to Theme.Disabled)
                Color c = !b.Enabled || b.ForeColor == Theme.Disabled ? Theme.Disabled : ink == null ? Ink : ink();
                string t = text();
                if (t == "⋯") Dots(g, r, c);
                else if (t.Length == 3 && t[0] == '(' && t[2] == ')') Circled(g, r, t.Substring(1, 1), c);
                else TextRenderer.DrawText(g, t, Label, r, c, Centre);
                if (b.Focused && keyFocus) FocusFrame(g, r);
            };
        }

        // the menu sign: three round dots (the ⋯ character is too small in this font)
        public static void Dots(Graphics g, Rectangle r, Color c)
        {
            float d = Theme.S(4f), step = Theme.S(7f), cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(c))
                for (int i = -1; i <= 1; i++) g.FillEllipse(b, cx + i * step - d / 2, cy - d / 2, d, d);
            g.SmoothingMode = old;
        }

        // a letter inside a thin circle (the C / F button)
        public static void Circled(Graphics g, Rectangle r, string letter, Color c)
        {
            float d = Theme.S(24f), cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(c, Theme.S(1.6f))) g.DrawEllipse(pen, cx - d / 2, cy - d / 2, d, d);
            g.SmoothingMode = old;
            TextRenderer.DrawText(g, letter, Label, new Rectangle((int)(cx - d / 2), (int)(cy - d / 2), (int)d + 1, (int)d + 1), c, Centre);
        }

        public static void FocusFrame(Graphics g, Rectangle r)
        {
            var f = Rectangle.Inflate(r, -Theme.S(3), -Theme.S(3));
            using (var pen = new Pen(Ink, Theme.S(1.2f)) { DashStyle = DashStyle.Dot }) g.DrawRectangle(pen, f);
        }
    }

    // The window's own title bar: square blocks for LAUNCHER, the stick (name, free space, a usage strip), a warning
    // when the tools folder is missing, running / open as "2/3", an empty block that fills the gap, then CHECK, C / F
    // in a circle, LIGHT / DARK, the menu, and minimise / maximise / close. A thin line runs under it. Its empty parts let the mouse through to the window, which treats them as the caption (drag,
    // double-click to maximise, snapping to the screen edges).
    class TitleBar : Panel
    {
        public readonly bool Compact;
        public readonly StickButton Stick;
        public readonly Button WarnChip = new Button { Text = "", Visible = false, Cursor = Cursors.Hand };
        public readonly Button CheckButton;
        public readonly Button LookButton;
        public readonly Button LayoutButton;
        public readonly Button MenuButton = new Button { Text = "Menu" };
        public readonly CaptionButton MinButton = new CaptionButton(CaptionButton.Kinds.Minimize);
        public readonly CaptionButton MaxButton = new CaptionButton(CaptionButton.Kinds.Maximize);
        public readonly CaptionButton CloseButton = new CaptionButton(CaptionButton.Kinds.Close);

        public int Running, Open, Profiles;

        // the painted (not clickable) parts
        Rectangle nameBlock, countBlock, filler;

        static int Pad { get { return Theme.S(6); } }
        static int Gap { get { return Theme.S(4); } }
        static int BlockHeight { get { return Theme.S(40); } }

        // Called after the theme has styled the window: the bar keeps its own colours.
        public void ApplyColors()
        {
            Lcars.Use();
            BackColor = Lcars.Back;
            foreach (var b in new Control[] { CheckButton, LayoutButton, LookButton, MenuButton, WarnChip, Stick, MinButton, MaxButton, CloseButton })
            {
                if (b == null) continue;
                b.BackColor = Lcars.Back;
                b.ForeColor = Lcars.Ink;
                var button = b as Button;
                if (button != null)
                {
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderSize = 0;
                    button.FlatAppearance.MouseOverBackColor = Lcars.Back;
                    button.FlatAppearance.MouseDownBackColor = Lcars.Back;
                }
                b.Invalidate();
            }
            Invalidate();
        }

        public TitleBar(bool compact, Button lookButton, Button layoutButton)
        {
            Compact = compact;
            Lcars.Use();
            SuspendLayout();   // no layout until every part exists
            DoubleBuffered = true;
            ResizeRedraw = true;
            Height = Pad + BlockHeight + Pad + 1;
            BackColor = Lcars.Back;
            Stick = new StickButton(compact);
            Controls.Add(Stick);

            Lcars.Attach(WarnChip, () => Compact ? "!" : WarnChip.Text.ToUpperInvariant(), () => Lcars.Warn);
            Controls.Add(WarnChip);

            if (!compact)
            {
                CheckButton = new Button { Text = "Check", AutoSize = false };
                Lcars.Attach(CheckButton, () => "CHECK");
                Controls.Add(CheckButton);
            }
            LayoutButton = layoutButton;
            Lcars.Attach(LayoutButton, () => Compact ? "(F)" : "(C)");
            Controls.Add(LayoutButton);
            LookButton = lookButton;
            Lcars.Attach(LookButton, () => Theme.IsDark ? "LIGHT" : "DARK");
            Controls.Add(LookButton);
            Lcars.Attach(MenuButton, () => "⋯");
            Controls.Add(MenuButton);
            foreach (var b in new[] { MinButton, MaxButton, CloseButton }) Controls.Add(b);
            MinButton.Click += (s, e) => { var f = FindForm(); if (f != null) f.WindowState = FormWindowState.Minimized; };
            MaxButton.Click += (s, e) =>
            {
                var f = FindForm();
                if (f != null) f.WindowState = f.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            };
            CloseButton.Click += (s, e) => { var f = FindForm(); if (f != null) f.Close(); };
            ResumeLayout(false);
        }

        // Shown in the warning block, or null for none (e.g. "Tools folder not found · fix").
        public string Warning
        {
            get { return WarnChip.Visible ? WarnChip.Text : null; }
            set
            {
                bool show = !string.IsNullOrEmpty(value);
                if (show == WarnChip.Visible && (!show || WarnChip.Text == value)) return;
                WarnChip.Text = value ?? "";
                WarnChip.Visible = show;
                LayoutBar();
            }
        }

        public void SetState(StickInfo info, int running, int open, int profiles, bool maximized)
        {
            Stick.Info = info;
            Stick.SyncPulse();
            Running = running;
            Open = open;
            Profiles = profiles;
            MaxButton.Maximized = maximized;
            MaxButton.Invalidate();
            Stick.Invalidate();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutBar();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            LayoutBar();
        }

        void LayoutBar()
        {
            if (Stick == null || CloseButton == null || LookButton == null || LayoutButton == null || MenuButton == null) return;   // still being built
            int y = Pad, h = BlockHeight;
            int x = Width - Pad;

            // right to left: close, maximise, minimise, menu, light/dark, C/F, check
            var right = new System.Collections.Generic.List<Tuple<Control, int>>
            {
                Tuple.Create((Control)CloseButton, Compact ? 38 : 44),
                Tuple.Create((Control)MaxButton, Compact ? 28 : 34),
                Tuple.Create((Control)MinButton, Compact ? 28 : 34),
                Tuple.Create((Control)MenuButton, Compact ? 30 : 36),
                Tuple.Create((Control)LookButton, Compact ? 52 : 60),
                Tuple.Create((Control)LayoutButton, Compact ? 30 : 36),
            };
            if (CheckButton != null) right.Add(Tuple.Create((Control)CheckButton, 70));
            foreach (var it in right)
            {
                int w = Theme.S(it.Item2);
                x -= w;
                it.Item1.Bounds = new Rectangle(x, y, w, h);
                x -= Gap;
                if (it.Item1 == MinButton) x -= Theme.S(4);   // a little more room before the window buttons
            }
            int rightEdge = x + Gap;

            // left to right: LAUNCHER, stick, warning, counters, filler. In a narrow window the stick block shrinks
            // (down to 90 px) and then the LAUNCHER block goes, so nothing slides under CHECK.
            int warnW = 0;
            if (WarnChip.Visible)
            {
                if (Compact) warnW = Theme.S(30);
                else
                    using (var g = CreateGraphics())
                        warnW = TextRenderer.MeasureText(g, WarnChip.Text.ToUpperInvariant(), Lcars.Label, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.S(20);
            }
            int stickW = Theme.S(Compact ? 170 : 210), nameW = Compact ? 0 : Theme.S(128), minStick = Theme.S(90);
            Func<int, int> room = name => rightEdge - Gap - Pad - (name > 0 ? name + Gap : 0) - (warnW > 0 ? warnW + Gap : 0);
            if (nameW > 0 && room(nameW) < minStick) nameW = 0;
            stickW = Math.Max(minStick, Math.Min(stickW, room(nameW)));

            int left = Pad;
            nameBlock = nameW > 0 ? new Rectangle(left, y, nameW, h) : Rectangle.Empty;
            if (nameW > 0) left = nameBlock.Right + Gap;
            Stick.Bounds = new Rectangle(left, y, stickW, h);
            left = Stick.Right + Gap;
            if (warnW > 0)
            {
                WarnChip.Bounds = new Rectangle(left, y, warnW, h);
                left = WarnChip.Right + Gap;
            }
            countBlock = Rectangle.Empty;
            int counter = Theme.S(212);
            if (!Compact && rightEdge - left - counter - Gap >= Theme.S(24))
            {
                countBlock = new Rectangle(left, y, counter, h);
                left = countBlock.Right + Gap;
            }
            filler = new Rectangle(left, y, Math.Max(0, rightEdge - Gap - left), h);
            Invalidate();
        }

        // Clicks on the bar itself (not on its buttons) go to the window, so it can be dragged like a title bar.
        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84, HTTRANSPARENT = -1;
            if (m.Msg == WM_NCHITTEST)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            using (var b = new SolidBrush(Lcars.Back)) g.FillRectangle(b, ClientRectangle);

            if (!nameBlock.IsEmpty)
            {
                Lcars.Fill(g, nameBlock, Lcars.Block);
                Wordmark(g, nameBlock);
            }
            if (!countBlock.IsEmpty) Counter(g, countBlock);
            if (filler.Width > 0) Lcars.Fill(g, filler, Lcars.Block);
            using (var pen = new Pen(Lcars.Line)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }

        // LAUNCHER: bigger than the other labels, the L bigger still, slanted like italics (Bahnschrift has no
        // italic, so the letters are drawn as a shape and sheared). Both parts share one baseline.
        void Wordmark(Graphics g, Rectangle r)
        {
            var family = Lcars.Label.FontFamily;
            var style = Lcars.Label.Style;
            float big = Theme.S(25f), rest = Theme.S(19f);   // em sizes in pixels
            float ascBig = big * family.GetCellAscent(style) / family.GetEmHeight(style);
            float ascRest = rest * family.GetCellAscent(style) / family.GetEmHeight(style);
            using (var path = new GraphicsPath())
            using (var fmt = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                path.AddString("L", family, (int)style, big, new PointF(0, 0), fmt);
                float lWidth = path.GetBounds().Right + Theme.S(1.5f);
                path.AddString("AUNCHER", family, (int)style, rest, new PointF(lWidth, ascBig - ascRest), fmt);
                using (var shear = new Matrix(1, 0, -0.2f, 1, 0, 0)) path.Transform(shear);
                var bounds = path.GetBounds();
                using (var move = new Matrix())
                {
                    move.Translate(r.X + (r.Width - bounds.Width) / 2f - bounds.X, r.Y + (r.Height - bounds.Height) / 2f - bounds.Y);
                    path.Transform(move);
                }
                var old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(Lcars.Ink)) g.FillPath(b, path);
                g.SmoothingMode = old;
            }
        }

        // "▶ 2 RUNNING   ▭ 3 OPEN" (mockup P1 · C4): a play sign and a window sign, each with its number and word.
        // The play sign and the running number turn green while something runs.
        void Counter(Graphics g, Rectangle r)
        {
            Lcars.Fill(g, r, Lcars.Block);
            const TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            Color runInk = Running > 0 ? Lcars.Accent : Lcars.Ink;
            string run = Running.ToString(), open = Open.ToString();
            var rSize = TextRenderer.MeasureText(g, run, Lcars.Big, Size.Empty, f);
            var oSize = TextRenderer.MeasureText(g, open, Lcars.Big, Size.Empty, f);
            var w1 = TextRenderer.MeasureText(g, "RUNNING", Lcars.Small, Size.Empty, f);
            var w2 = TextRenderer.MeasureText(g, "OPEN", Lcars.Small, Size.Empty, f);
            int icon = Theme.S(12), gap = Theme.S(5), between = Theme.S(14);
            int total = icon + gap + rSize.Width + gap + w1.Width + between + Theme.S(14) + gap + oSize.Width + gap + w2.Width;
            int x = r.X + Math.Max(0, (r.Width - total) / 2);
            int top = r.Y + (r.Height - rSize.Height) / 2;
            int wordTop = top + (int)(rSize.Height * 0.78) - (int)(w1.Height * 0.78);
            float cy = r.Y + r.Height / 2f;

            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(runInk))   // play sign
                g.FillPolygon(b, new[] { new PointF(x + Theme.S(1f), cy - icon / 2f), new PointF(x + icon, cy), new PointF(x + Theme.S(1f), cy + icon / 2f) });
            g.SmoothingMode = old;
            x += icon + gap;
            TextRenderer.DrawText(g, run, Lcars.Big, new Point(x, top), runInk, f);
            x += rSize.Width + gap;
            TextRenderer.DrawText(g, "RUNNING", Lcars.Small, new Point(x, wordTop), Lcars.InkDim, f);
            x += w1.Width + between;

            using (var pen = new Pen(Lcars.Ink, Theme.S(1.5f)))   // window sign: a frame with a title line
            {
                float w = Theme.S(14f), h = Theme.S(11f), wy = cy - h / 2f;
                g.DrawRectangle(pen, x, wy, w, h);
                g.DrawLine(pen, x, wy + Theme.S(3.5f), x + w, wy + Theme.S(3.5f));
            }
            x += Theme.S(14) + gap;
            TextRenderer.DrawText(g, open, Lcars.Big, new Point(x, top), Lcars.Ink, f);
            x += oSize.Width + gap;
            TextRenderer.DrawText(g, "OPEN", Lcars.Small, new Point(x, wordTop), Lcars.InkDim, f);
        }
    }

    // The stick block (mockup P1 · S5): a small ring that fills with how much of the stick is used, then the
    // stick's name and free space, all centred on the block. The ring and the free space turn red when the stick
    // is nearly full. Clicking it opens the stick panel.
    class StickButton : Button
    {
        public StickInfo Info;
        public bool Warn;            // something is wrong (tools folder missing, not a stick); explained in the tooltip
        public bool Open;            // its panel is open
        readonly bool compact;
        bool hover, down, keyFocus;

        // Almost full (mockup P2 · W10): thin red rings ripple out from the ring, like a radar ping, two at a
        // time, each 2 s. Only while the stick is almost full and Windows' animations are on (else the ring is red).
        readonly Timer pulse = new Timer { Interval = 50 };
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        // Starts or stops the ripple (called whenever the stick's state is updated).
        public void SyncPulse()
        {
            bool on = NearlyFull && Theme.Animate && Visible;
            if (pulse.Enabled != on)
            {
                pulse.Enabled = on;
                Invalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) pulse.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnGotFocus(EventArgs e) { keyFocus = Control.MouseButtons == MouseButtons.None; base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { keyFocus = false; base.OnLostFocus(e); Invalidate(); }

        public StickButton(bool compact)
        {
            this.compact = compact;
            Text = "Stick";
            AccessibleName = "The stick: details and actions";
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            MouseEnter += (s, e) => { hover = true; Invalidate(); };
            MouseLeave += (s, e) => { hover = false; down = false; Invalidate(); };
            MouseDown += (s, e) => { down = true; Invalidate(); };
            MouseUp += (s, e) => { down = false; Invalidate(); };
            pulse.Tick += (s, e) => Invalidate();
        }

        public bool NearlyFull { get { return Info != null && Info.NearlyFull; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Lcars.Fill(g, ClientRectangle, down ? Lcars.Down(Lcars.Block) : hover || Open ? Lcars.Hover(Lcars.Block) : Lcars.Block);

            var text = new Rectangle(Theme.S(8), 0, Width - Theme.S(16), Height);
            string name = Info == null ? "LAUNCHER FOLDER"
                : compact ? (Info.Letter + " " + Info.Label).Trim().ToUpperInvariant()
                : ((Info.Label.Length > 0 ? Info.Label : Info.Kind) + " · " + Info.Letter).ToUpperInvariant();
            string free = Info != null && Info.Total > 0 ? (PortableEnv.FormatSize(Info.Free) + (compact ? "" : " free")).ToUpperInvariant() : "";
            bool ring = Info != null && Info.Total > 0;
            int ringD = Theme.S(18), ringGap = ring ? Theme.S(8) : 0, ringW = ring ? ringD : 0;

            const TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            var nSize = TextRenderer.MeasureText(g, name, Lcars.Label, Size.Empty, f);
            var fSize = free.Length > 0 ? TextRenderer.MeasureText(g, free, Lcars.Small, Size.Empty, f) : Size.Empty;
            int gap = free.Length > 0 ? Theme.S(8) : 0;
            if (ringW + ringGap + nSize.Width + gap + fSize.Width > text.Width) { free = ""; fSize = Size.Empty; gap = 0; }   // too long: no free space
            int x = text.X + Math.Max(0, (text.Width - ringW - ringGap - nSize.Width - gap - fSize.Width) / 2);
            int top = (Height - nSize.Height) / 2;

            if (ring)
            {
                var rr = new RectangleF(x + Theme.S(1.5f), (Height - ringD) / 2f + Theme.S(1.5f), ringD - Theme.S(3f), ringD - Theme.S(3f));
                var old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (pulse.Enabled)
                {
                    // two ripples, half a cycle apart: they grow to 2.6 x the ring and fade out
                    float cx = rr.X + rr.Width / 2f, cy = rr.Y + rr.Height / 2f, r0 = ringD / 2f;
                    double t = clock.Elapsed.TotalSeconds;
                    for (int k = 0; k < 2; k++)
                    {
                        double phase = ((t + k) % 2.0) / 2.0;
                        float rad = r0 * (float)(1 + 1.6 * phase);
                        int alpha = (int)(230 * (1 - phase));
                        using (var pen = new Pen(Color.FromArgb(alpha, Lcars.Red), Theme.S(1.5f)))
                            g.DrawEllipse(pen, cx - rad, cy - rad, rad * 2, rad * 2);
                    }
                }
                using (var track = new Pen(Lcars.Mix(Lcars.InkDim, Lcars.Block, 0.3), Theme.S(3f))) g.DrawEllipse(track, rr);
                float sweep = (float)(360 * Info.UsedFraction);
                if (sweep > 0)
                    using (var used = new Pen(NearlyFull ? Lcars.Red : Lcars.Accent, Theme.S(3f)))
                        g.DrawArc(used, rr, -90, Math.Max(sweep, 4f));
                g.SmoothingMode = old;
                x += ringW + ringGap;
            }
            int maxName = Math.Max(0, text.Right - x - gap - fSize.Width);
            TextRenderer.DrawText(g, name, Lcars.Label, new Rectangle(x, top, Math.Min(nSize.Width, maxName), nSize.Height), Lcars.Ink, f | TextFormatFlags.EndEllipsis);
            if (free.Length > 0)
            {
                int ft = top + (int)(nSize.Height * 0.78) - (int)(fSize.Height * 0.78);
                TextRenderer.DrawText(g, free, Lcars.Small, new Point(x + nSize.Width + gap, ft), NearlyFull ? Lcars.Red : Lcars.InkDim, f);
            }
            if (Focused && keyFocus) Lcars.FocusFrame(g, ClientRectangle);
        }
    }

    // Minimise, maximise / restore and close as square blocks; close turns red under the mouse.
    class CaptionButton : Button
    {
        public enum Kinds { Minimize, Maximize, Close }
        public readonly Kinds Kind;
        public bool Maximized;
        bool hover, down;

        public CaptionButton(Kinds kind)
        {
            Kind = kind;
            Text = kind == Kinds.Minimize ? "Minimize" : kind == Kinds.Maximize ? "Maximize" : "Close";
            TabStop = false;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            MouseEnter += (s, e) => { hover = true; Invalidate(); };
            MouseLeave += (s, e) => { hover = false; down = false; Invalidate(); };
            MouseDown += (s, e) => { down = true; Invalidate(); };
            MouseUp += (s, e) => { down = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            bool close = Kind == Kinds.Close;
            Color back = Lcars.Block, ink = Lcars.Ink;
            if (close && (hover || down)) { back = down ? Color.FromArgb(0x94, 0x1F, 0x1A) : Color.FromArgb(0xC4, 0x2B, 0x1C); ink = Color.White; }
            else if (down) back = Lcars.Down(back);
            else if (hover) back = Lcars.Hover(back);
            Lcars.Fill(g, ClientRectangle, back);
            float cx = Width / 2f, cy = Height / 2f, s = Theme.S(5f);
            using (var pen = new Pen(ink, Theme.S(1.3f)))
            {
                switch (Kind)
                {
                    case Kinds.Minimize:
                        g.DrawLine(pen, cx - s, cy, cx + s, cy);
                        break;
                    case Kinds.Maximize:
                        if (Maximized)
                        {
                            g.DrawRectangle(pen, cx - s, cy - s + Theme.S(2f), s * 2 - Theme.S(2f), s * 2 - Theme.S(2f));
                            g.DrawLines(pen, new[] { new PointF(cx - s + Theme.S(2f), cy - s), new PointF(cx + s, cy - s), new PointF(cx + s, cy + s - Theme.S(2f)) });
                        }
                        else g.DrawRectangle(pen, cx - s, cy - s, s * 2, s * 2);
                        break;
                    default:
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
                        g.DrawLine(pen, cx + s, cy - s, cx - s, cy + s);
                        break;
                }
            }
        }
    }
}
