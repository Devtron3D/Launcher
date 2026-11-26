using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LauncherApp
{
    // All colours and fonts live here, plus DPI scaling and the helper that styles controls.
    // Three looks: Dark and Light (neutral colours, normal text) and Green phosphor (green on black,
    // monospace, CAPITALS and [BRACKETS]). "System" follows Windows' own dark/light app setting.
    static class Theme
    {
        public const string SystemTheme = "system", DarkTheme = "dark", LightTheme = "light", PhosphorTheme = "phosphor";

        // The look in use: dark, light or phosphor ("system" is resolved when it is applied).
        public static string Current { get; private set; }
        public static bool IsDark { get; private set; }
        public static bool Terminal { get; private set; }   // phosphor: capitals, brackets, monospace

        // ---- colours ----
        public static Color Back { get; private set; }          // window background
        public static Color Surface { get; private set; }       // panels and lists
        public static Color StatusBar { get; private set; }     // title bar (Windows 11)
        public static Color Field { get; private set; }         // text boxes
        public static Color Hover { get; private set; }
        public static Color Pressed { get; private set; }
        public static Color Border { get; private set; }        // frames
        public static Color BorderDashed { get; private set; }
        public static Color Divider { get; private set; }       // row separators
        public static Color Text { get; private set; }
        public static Color Bright { get; private set; }        // names, headings
        public static Color Dim { get; private set; }           // secondary buttons
        public static Color Muted { get; private set; }         // labels, details
        public static Color Disabled { get; private set; }
        public static Color Accent { get; private set; }        // green text, icons and segments
        public static Color AccentHover { get; private set; }
        public static Color AccentPressed { get; private set; }
        public static Color AccentText { get; private set; }
        public static Color PrimaryBack { get; private set; }   // filled main buttons (Run, Save)
        public static Color PrimaryHover { get; private set; }
        public static Color PrimaryPressed { get; private set; }
        public static Color PrimaryText { get; private set; }
        public static Color StopHover { get; private set; }
        public static Color StopPressed { get; private set; }
        public static Color Selected { get; private set; }
        public static Color Track { get; private set; }
        public static Color BarFill { get; private set; }
        public static Color BarStripe { get { return Accent; } }
        public static Color BarText { get; private set; }
        public static Color Playhead { get; private set; }
        public static Color StatusBusy { get; private set; }    // amber: starting, waiting
        public static Color StatusDone { get { return Accent; } }
        public static Color StatusProblem { get; private set; } // red: missing, failed
        public static Color Warning { get; private set; }       // amber text, Stop buttons
        public static Color OkDim { get; private set; }         // "found" segments of idle profiles
        public static Color RunningBack { get; private set; }   // row of a running profile

        // ---- fonts ----
        // Segoe UI and Consolas are part of every Windows 10/11, so nothing has to be carried on the stick.
        // Sizes are in points, so they follow the screen DPI on their own.
        public static Font Ui { get; private set; }
        public static Font UiBold { get; private set; }
        public static Font Small { get; private set; }
        public static Font Tiny { get; private set; }
        public static Font Heading { get; private set; }
        public static Font TileTitle { get; private set; }       // section headings
        public static Font Mono { get; private set; }            // times, numbers, the log
        public static Font MonoSmall { get; private set; }

        static Theme()
        {
            Use(DarkTheme);
        }

        static Color C(int rgb)
        {
            return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        }

        // Switch the look. Windows created afterwards use it; the main window rebuilds itself.
        public static void Use(string name)
        {
            name = (name ?? SystemTheme).ToLowerInvariant();
            if (name != DarkTheme && name != LightTheme && name != PhosphorTheme)
                name = WindowsUsesLightApps() ? LightTheme : DarkTheme;
            Current = name;
            Terminal = name == PhosphorTheme;
            IsDark = name != LightTheme;

            if (name == PhosphorTheme)
            {
                Back = C(0x0A0F0B); Surface = C(0x0D140F); StatusBar = C(0x070A08); Field = C(0x0F1812);
                Hover = C(0x12301A); Pressed = C(0x1A4024); Border = C(0x2A4030); BorderDashed = C(0x24372A); Divider = C(0x1D2B20);
                Text = C(0xCFE8D4); Bright = C(0xE6F5E9); Dim = C(0x9FC4A8); Muted = C(0x7FA588); Disabled = C(0x4F6A55);
                Accent = C(0x5BE37D); AccentHover = C(0x7CF09A); AccentPressed = C(0x46BE66); AccentText = C(0x0A0F0B);
                PrimaryBack = Accent; PrimaryHover = AccentHover; PrimaryPressed = AccentPressed; PrimaryText = AccentText;
                StopHover = C(0x30220E); StopPressed = C(0x463012);
                Selected = C(0x12301A); Track = C(0x0F1812); BarFill = C(0x143A1E); BarText = C(0x8FF0A6); Playhead = C(0xFFB15C);
                StatusBusy = C(0xFFB15C); StatusProblem = C(0xFF6B6B); Warning = C(0xFFB15C); OkDim = C(0x3D8F55); RunningBack = C(0x0D1A10);
                SetFonts("Consolas", "Consolas", "Consolas", FontStyle.Bold, 12f);
            }
            else if (name == LightTheme)
            {
                Back = C(0xF5F6F8); Surface = C(0xFFFFFF); StatusBar = C(0xE9EBEE); Field = C(0xFFFFFF);
                Hover = C(0xEAEEF2); Pressed = C(0xDDE2E7); Border = C(0xD0D5DB); BorderDashed = C(0xE4E7EB); Divider = C(0xE4E7EB);
                Text = C(0x1F2328); Bright = C(0x0D1117); Dim = C(0x3D444D); Muted = C(0x59636E); Disabled = C(0x8C959F);
                Accent = C(0x1A7F37); AccentHover = C(0x116329); AccentPressed = C(0x0F5323); AccentText = C(0xFFFFFF);
                PrimaryBack = C(0x1A7F37); PrimaryHover = C(0x157030); PrimaryPressed = C(0x0F5F28); PrimaryText = C(0xFFFFFF);
                StopHover = C(0xFFF4DC); StopPressed = C(0xFCE7BA);
                Selected = C(0xEEF7F0); Track = C(0xF0F2F4); BarFill = C(0xDCF1E2); BarText = C(0x116329); Playhead = C(0x9A6700);
                StatusBusy = C(0xBF8700); StatusProblem = C(0xCF222E); Warning = C(0x9A6700); OkDim = C(0x4CA866); RunningBack = C(0xEEF7F0);
                SetFonts("Segoe UI", "Segoe UI Semibold", "Consolas", FontStyle.Regular, 13f);
            }
            else
            {
                Back = C(0x17191C); Surface = C(0x202328); StatusBar = C(0x111316); Field = C(0x121417);
                Hover = C(0x2A2E34); Pressed = C(0x33383F); Border = C(0x30343A); BorderDashed = C(0x2A2E33); Divider = C(0x2A2E33);
                Text = C(0xE6E8EB); Bright = C(0xF5F7FA); Dim = C(0xC3C8CE); Muted = C(0x9BA1A8); Disabled = C(0x5A616B);
                Accent = C(0x3FB950); AccentHover = C(0x56D364); AccentPressed = C(0x2EA043); AccentText = C(0x0D1117);
                PrimaryBack = C(0x238636); PrimaryHover = C(0x2EA043); PrimaryPressed = C(0x1A7F37); PrimaryText = C(0xFFFFFF);
                StopHover = C(0x2B2414); StopPressed = C(0x3A3018);
                Selected = C(0x1C2620); Track = C(0x17191C); BarFill = C(0x1D3524); BarText = C(0x7EE2A0); Playhead = C(0xD29922);
                StatusBusy = C(0xD29922); StatusProblem = C(0xF85149); Warning = C(0xD29922); OkDim = C(0x2E7D3E); RunningBack = C(0x1C2620);
                SetFonts("Segoe UI", "Segoe UI Semibold", "Consolas", FontStyle.Regular, 13f);
            }
        }

        static void SetFonts(string text, string bold, string mono, FontStyle boldStyle, float heading)
        {
            Ui = new Font(text, 9.75f);
            UiBold = new Font(bold, 9.75f, boldStyle);
            Small = new Font(text, 8.25f);
            Tiny = new Font(text, 7f);
            Heading = new Font(bold, heading, boldStyle == FontStyle.Regular ? FontStyle.Regular : FontStyle.Bold);
            TileTitle = new Font(bold, 10.5f, boldStyle == FontStyle.Regular ? FontStyle.Regular : FontStyle.Bold);
            Mono = new Font(mono, 9.75f);
            MonoSmall = new Font(mono, 8.25f);
        }

        // Windows' "Choose your app mode" setting (only read, never written).
        static bool WindowsUsesLightApps()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 1;
                }
            }
            catch
            {
                return false;
            }
        }

        // Icons of running profiles move: the setting is on and Windows' "Animation effects" are on.
        // Set by the main window on each refresh.
        public static bool Animate { get; set; }

        [DllImport("user32.dll")]
        static extern bool SystemParametersInfo(int action, int param, ref bool value, int ini);

        // Windows' Settings › Accessibility › Visual effects › "Animation effects" (only read).
        public static bool WindowsAnimations()
        {
            try
            {
                bool on = true;
                return !SystemParametersInfo(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, ref on, 0) || on;
            }
            catch
            {
                return true;
            }
        }

        // Text in the current look: "Run" / "[ RUN ]" style pairs, and capitals for the phosphor look.
        public static string Pick(string normal, string terminal) { return Terminal ? terminal : normal; }
        public static string Up(string s) { return Terminal ? (s ?? "").ToUpperInvariant() : s; }

        // ---- DPI ----
        // The app is system-DPI aware (see app.manifest). Layout numbers in the code are written for
        // 100 % scaling (96 DPI) and go through S() so they grow with the Windows scale setting.

        static readonly float scale = ReadScale();

        static float ReadScale()
        {
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                    return Math.Max(1f, g.DpiX / 96f);
            }
            catch
            {
                return 1f;
            }
        }

        public static int S(int px) { return (int)Math.Round(px * scale); }
        public static float S(float px) { return px * scale; }
        public static Size S(int w, int h) { return new Size(S(w), S(h)); }
        public static Point P(int x, int y) { return new Point(S(x), S(y)); }
        public static Padding Pad(int all) { return new Padding(S(all)); }
        public static Padding Pad(int left, int top, int right, int bottom)
        {
            return new Padding(S(left), S(top), S(right), S(bottom));
        }

        // 45 -> "45 s", 150 -> "2 min 30 s", 600 -> "10 min"
        public static string Seconds(int t)
        {
            if (t < 120) return t + " s";
            if (t % 60 == 0) return (t / 60) + " min";
            return (t / 60) + " min " + (t % 60) + " s";
        }

        // ---- window chrome ----

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        // Text boxes that already re-set their margins when their window handle is recreated.
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBox, object> marginBoxes =
            new System.Runtime.CompilerServices.ConditionalWeakTable<TextBox, object>();

        static int ColorRef(Color c)
        {
            return c.R | (c.G << 8) | (c.B << 16);
        }

        public static void ApplyToForm(Form f)
        {
            f.BackColor = Back;
            f.ForeColor = Text;

            Action darkTitle = () =>
            {
                try
                {
                    int on = IsDark ? 1 : 0;   // dark or light title bar
                    // 20 = Windows 10 20H1 and newer / Windows 11, 19 = older Windows 10 builds
                    if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0)
                        DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
                    // Windows 11 only (ignored elsewhere): title bar, title text and border colours.
                    int caption = ColorRef(StatusBar), text = ColorRef(Muted), border = ColorRef(Border);
                    DwmSetWindowAttribute(f.Handle, 35, ref caption, 4);
                    DwmSetWindowAttribute(f.Handle, 36, ref text, 4);
                    DwmSetWindowAttribute(f.Handle, 34, ref border, 4);
                }
                catch { }
            };
            if (f.IsHandleCreated) darkTitle();
            else
            {
                f.HandleCreated += (s, e) => darkTitle();
                f.Load += (s, e) => FitToScreen(f);
            }

            Apply(f);
        }

        // Shrinks a window that is bigger than the screen it opens on (small laptop screens, or 125-150 %
        // scaling), and moves it fully onto that screen, so its bottom buttons are never below the taskbar.
        public static void FitToScreen(Form f)
        {
            if (f.AutoSize) return;   // sizes itself to its contents, which are small
            try
            {
                var wa = Screen.FromControl(f).WorkingArea;
                int w = Math.Min(f.Width, wa.Width), h = Math.Min(f.Height, wa.Height);
                f.MinimumSize = new Size(Math.Min(f.MinimumSize.Width, w), Math.Min(f.MinimumSize.Height, h));
                if (w != f.Width || h != f.Height) f.Size = new Size(w, h);
                f.Location = new Point(Math.Max(wa.Left, Math.Min(f.Left, wa.Right - w)),
                                       Math.Max(wa.Top, Math.Min(f.Top, wa.Bottom - h)));
            }
            catch { }
        }

        // Button roles, set with Tag: "primary" = filled green (the main action), "link" = text only.
        public const string Primary = "primary";
        public const string Link = "link";
        public const string Stop = "stop";   // amber outline: stops something that is running
        public const string Outline = "outline";   // green outline: an important action that isn't the window's main one

        // Styles every control inside root (not root itself). Labels and panels follow the
        // form's colours automatically because they inherit them.
        public static void Apply(Control root)
        {
            foreach (Control c in root.Controls)
            {
                var button = c as Button;
                var text = c as TextBox;
                var combo = c as ComboBox;
                var number = c as NumericUpDown;
                var listBox = c as ListBox;
                var check = c as CheckBox;

                if (button != null)
                {
                    StyleButton(button);
                }
                else if (text != null)
                {
                    text.BackColor = Field;
                    text.ForeColor = Text;
                    if (text.BorderStyle != BorderStyle.None)
                    {
                        text.BorderStyle = BorderStyle.FixedSingle;
                        // A little room between the border and the text (Windows puts it right against the border).
                        var tb = text;
                        Action setMargins = () =>
                        {
                            int m = S(4);
                            try
                            {
                                SendMessage(tb.Handle, 0xD3 /* EM_SETMARGINS */, (IntPtr)3, (IntPtr)((m << 16) | m));
                                // Windows only moves text that is already there after a fresh layout: re-select to trigger one.
                                int start = tb.SelectionStart, length = tb.SelectionLength;
                                tb.Select(0, 0);
                                tb.Select(start, length);
                                tb.Invalidate();
                            }
                            catch { }
                        };
                        object seen;
                        if (!marginBoxes.TryGetValue(tb, out seen))
                        {
                            marginBoxes.Add(tb, null);
                            tb.HandleCreated += (s, e) => setMargins();
                            tb.FontChanged += (s, e) => { if (tb.IsHandleCreated) setMargins(); };   // a new font resets them
                        }
                        if (tb.IsHandleCreated) setMargins();
                    }
                }
                else if (combo != null)
                {
                    // Windows' own drop-down look: its dark variant (the one the file dialog uses) in the dark
                    // looks, the normal one in light. It draws the frame and arrow; we draw the item text.
                    combo.FlatStyle = FlatStyle.Standard;
                    combo.BackColor = Field;
                    combo.ForeColor = Text;
                    var themed = combo;
                    Action setComboTheme = () =>
                    {
                        try { SetWindowTheme(themed.Handle, IsDark ? "DarkMode_CFD" : "Explorer", null); }
                        catch { }
                    };
                    if (combo.IsHandleCreated) setComboTheme();
                    combo.HandleCreated += (s, e) => setComboTheme();
                    // Windows draws drop-down lists white whatever BackColor says, so draw the items ourselves.
                    if (combo.DrawMode == DrawMode.Normal && !"themed".Equals(combo.Tag))
                    {
                        combo.Tag = "themed";
                        combo.DrawMode = DrawMode.OwnerDrawFixed;
                        combo.ItemHeight = combo.Font.Height + S(4);
                        var box = combo;
                        box.DrawItem += (s, e) =>
                        {
                            if (e.Index < 0) return;
                            bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                            using (var bg = new SolidBrush(sel ? Hover : Field))
                                e.Graphics.FillRectangle(bg, e.Bounds);
                            TextRenderer.DrawText(e.Graphics, box.GetItemText(box.Items[e.Index]), box.Font,
                                new Rectangle(e.Bounds.X + S(4), e.Bounds.Y, e.Bounds.Width - S(4), e.Bounds.Height),
                                box.Enabled ? Text : Disabled,
                                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                        };
                    }
                }
                else if (number != null)
                {
                    number.BackColor = Field;
                    number.ForeColor = Text;
                    number.BorderStyle = BorderStyle.FixedSingle;
                }
                else if (listBox != null)
                {
                    listBox.BackColor = Field;
                    listBox.ForeColor = Text;
                    listBox.BorderStyle = BorderStyle.FixedSingle;
                    DarkScrollBars(listBox);
                }
                else if (check != null && !"themed".Equals(check.Tag))
                {
                    // Draw the box ourselves: a green square when ticked, so on and off are easy to tell apart.
                    check.Tag = "themed";
                    check.FlatStyle = FlatStyle.Flat;
                    check.FlatAppearance.BorderSize = 0;
                    check.FlatAppearance.MouseOverBackColor = check.BackColor;
                    check.FlatAppearance.MouseDownBackColor = check.BackColor;
                    var box = check;
                    box.Paint += (s, e) =>
                    {
                        int sz = S(13);
                        int y = (box.Height - sz) / 2;
                        using (var bg = new SolidBrush(Field))
                            e.Graphics.FillRectangle(bg, 0, y, sz, sz);
                        using (var pen = new Pen(box.Enabled ? Accent : Disabled))
                            e.Graphics.DrawRectangle(pen, 0, y, sz - 1, sz - 1);
                        if (box.Checked)
                            using (var fill = new SolidBrush(box.Enabled ? Accent : Disabled))
                                e.Graphics.FillRectangle(fill, S(3), y + S(3), sz - 2 * S(3), sz - 2 * S(3));
                    };
                    box.CheckedChanged += (s, e) => box.Invalidate();
                    box.EnabledChanged += (s, e) => box.Invalidate();
                }
                else if (c is GroupBox)
                {
                    // Flat, so the title uses our colour instead of the light theme's.
                    ((GroupBox)c).FlatStyle = FlatStyle.Flat;
                    c.ForeColor = Muted;
                }
                else if (c is FlowLayoutPanel || c is ScrollableControl && ((ScrollableControl)c).AutoScroll)
                {
                    DarkScrollBars(c);
                }

                Apply(c);
            }
        }

        public static void StyleButton(Button b)
        {
            string role = b.Tag as string;
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            if (Terminal && b.Text != null) b.Text = b.Text.ToUpperInvariant();   // phosphor: every button in capitals
            if (role == Primary)
            {
                b.BackColor = PrimaryBack;
                b.ForeColor = PrimaryText;
                b.Font = UiBold;
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = PrimaryHover;
                b.FlatAppearance.MouseDownBackColor = PrimaryPressed;
            }
            else if (role == Outline)
            {
                b.BackColor = Back;
                b.ForeColor = Accent;
                b.Font = UiBold;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = Accent;
                b.FlatAppearance.MouseOverBackColor = Hover;
                b.FlatAppearance.MouseDownBackColor = Pressed;
            }
            else if (role == Stop)
            {
                b.BackColor = Back;
                b.ForeColor = Warning;
                b.Font = UiBold;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = Warning;
                b.FlatAppearance.MouseOverBackColor = StopHover;
                b.FlatAppearance.MouseDownBackColor = StopPressed;
            }
            else if (role == Link)
            {
                b.BackColor = Back;
                b.ForeColor = Dim;
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = Hover;
                b.FlatAppearance.MouseDownBackColor = Pressed;
            }
            else
            {
                b.BackColor = Back;
                b.ForeColor = Text;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = Border;
                b.FlatAppearance.MouseOverBackColor = Hover;
                b.FlatAppearance.MouseDownBackColor = Pressed;
            }
        }

        // An icon-only button. It keeps real Text (that's its name for screen readers and keyboard users),
        // but paints the icon over it, with the same hover and pressed colours as the other buttons.
        public static void MakeIconButton(Button b, Action<Graphics, Rectangle, Color> drawIcon)
        {
            bool hover = false, down = false;
            b.MouseEnter += (s, e) => { hover = true; b.Invalidate(); };
            b.MouseLeave += (s, e) => { hover = false; down = false; b.Invalidate(); };
            b.MouseDown += (s, e) => { down = true; b.Invalidate(); };
            b.MouseUp += (s, e) => { down = false; b.Invalidate(); };
            b.GotFocus += (s, e) => b.Invalidate();
            b.LostFocus += (s, e) => b.Invalidate();
            b.Paint += (s, e) =>
            {
                var g = e.Graphics;
                Color back = !b.Enabled ? b.BackColor
                    : down ? b.FlatAppearance.MouseDownBackColor
                    : hover ? b.FlatAppearance.MouseOverBackColor
                    : b.BackColor;
                if (back.IsEmpty) back = b.BackColor;
                using (var bg = new SolidBrush(back))
                    g.FillRectangle(bg, b.ClientRectangle);
                if (b.FlatAppearance.BorderSize > 0)
                    using (var pen = new Pen(b.Focused ? Accent : b.FlatAppearance.BorderColor))
                        g.DrawRectangle(pen, 0, 0, b.Width - 1, b.Height - 1);
                drawIcon(g, b.ClientRectangle, b.Enabled ? Text : Disabled);
            };
        }

        // A right-click / "more" menu in the current look.
        public static void StyleMenu(ContextMenuStrip menu)
        {
            menu.Renderer = new MenuRenderer();
            menu.BackColor = Surface;
            menu.ForeColor = Text;
            menu.Font = Ui;
            menu.ShowImageMargin = false;
            foreach (ToolStripItem item in menu.Items)
            {
                item.ForeColor = Text;
                item.Padding = new Padding(S(4), S(3), S(4), S(3));
            }
        }

        // A pop-up panel (like the icon grid) in the current look, with a thin border.
        public static void StyleDropDown(ToolStripDropDown d)
        {
            d.Renderer = new MenuRenderer();
            d.BackColor = Surface;
        }

        class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Surface; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color MenuItemBorder { get { return Hover; } }
            public override Color MenuItemSelected { get { return Hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Hover; } }
            public override Color MenuItemPressedGradientBegin { get { return Pressed; } }
            public override Color MenuItemPressedGradientEnd { get { return Pressed; } }
            public override Color SeparatorDark { get { return Divider; } }
            public override Color SeparatorLight { get { return Divider; } }
            public override Color ImageMarginGradientBegin { get { return Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Surface; } }
            public override Color ImageMarginGradientEnd { get { return Surface; } }
        }

        class MenuRenderer : ToolStripProfessionalRenderer
        {
            public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                // Windows greys disabled items with its own (light theme) colour; use ours.
                e.TextColor = !e.Item.Enabled ? Disabled
                    : "danger".Equals(e.Item.Tag) ? StatusProblem
                    : Text;
                base.OnRenderItemText(e);
            }
        }

        static void DarkScrollBars(Control c)
        {
            Action set = () =>
            {
                try { SetWindowTheme(c.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null); }
                catch { }
            };
            if (c.IsHandleCreated) set();
            else c.HandleCreated += (s, e) => set();
        }
    }
}
