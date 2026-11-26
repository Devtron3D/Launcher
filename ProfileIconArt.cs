using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LauncherApp
{
    // The detailed profile icons (mockups I2 for Dark, I4 for Light; drawn in green for Green phosphor).
    // Each is a small drawing on a 32x32 grid in four layers: a main fill, a second fill, the outline and fine
    // detail lines. The shapes are written as SVG path data and turned into GDI+ paths here, so they stay sharp at
    // any size and DPI and no image files are needed.
    static class ProfileIconArt
    {
        class Art
        {
            public string F1, F2, L1, L2;
            public Color C1, C2;   // main and second colour (Dark and Light looks)
        }

        static Color Hex(int rgb) { return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

        static readonly Dictionary<string, Art> Arts = new Dictionary<string, Art>
        {
            { "thermometer", new Art { C1 = Hex(0xE6EDF3), C2 = Hex(0xE5534B),
                F1 = "M13 21V6a3 3 0 0 1 6 0v15a5 5 0 1 1-6 0Z", F2 = "M15 10h2v12.3a3 3 0 1 1-2 0Z",
                L1 = "M13 21V6a3 3 0 0 1 6 0v15a5 5 0 1 1-6 0Z", L2 = "M21.5 7h3 M21.5 10h2 M21.5 13h3 M21.5 16h2 M21.5 19h3" } },
            { "chip", new Art { C1 = Hex(0x57606A), C2 = Hex(0x79A8F0),
                F1 = "M8 8h16v16H8Z", F2 = "M11.5 11.5h9v9h-9Z", L1 = "M8 8h16v16H8Z M11.5 11.5h9v9h-9Z",
                L2 = "M11 4v4 M14.3 4v4 M17.7 4v4 M21 4v4 M11 24v4 M14.3 24v4 M17.7 24v4 M21 24v4 M4 11h4 M4 14.3h4 M4 17.7h4 M4 21h4 M24 11h4 M24 14.3h4 M24 17.7h4 M24 21h4 M13.6 13.6h.01 M14 18.5l4.5-4.5" } },
            { "network", new Art { C1 = Hex(0x2BB5A0), C2 = Hex(0xD7FFF6),
                F1 = "M11.5 3h9v8h-9Z M2.5 20h9v7h-9Z M20.5 20h9v7h-9Z", F2 = "M13.5 5h5v2h-5Z M4.5 22h5v3h-5Z M22.5 22h5v3h-5Z",
                L1 = "M11.5 3h9v8h-9Z M2.5 20h9v7h-9Z M20.5 20h9v7h-9Z", L2 = "M16 11v4.5 M7 20v-4.5h18V20 M14 9.3h.01 M16 9.3h.01 M18 9.3h.01 M5 29h4 M23 29h4" } },
            { "flame", new Art { C1 = Hex(0xF0883E), C2 = Hex(0xFFD166),
                F1 = "M16 29c-5.5 0-9-3.8-9-8.8 0-4.4 3-6.8 4.5-9.8.6 2.3 1.9 3.8 3.4 4.6C14.6 10.7 16.6 6.6 19.5 3c.5 5 5.5 8.3 5.5 15.3C25 24.6 21.5 29 16 29Z",
                F2 = "M16 29c-2.8 0-4.6-2-4.6-4.5 0-2.4 1.7-3.8 2.8-5.6 1.3 2.1 5.4 3.3 5.4 6.2 0 2.3-1.6 3.9-3.6 3.9Z",
                L1 = "M16 29c-5.5 0-9-3.8-9-8.8 0-4.4 3-6.8 4.5-9.8.6 2.3 1.9 3.8 3.4 4.6C14.6 10.7 16.6 6.6 19.5 3c.5 5 5.5 8.3 5.5 15.3C25 24.6 21.5 29 16 29Z",
                L2 = "M21 12.5c1.1 1.6 1.6 3.3 1.6 5 M16 29c-2.8 0-4.6-2-4.6-4.5 0-2.4 1.7-3.8 2.8-5.6 1.3 2.1 5.4 3.3 5.4 6.2 0 2.3-1.6 3.9-3.6 3.9Z" } },
            { "gauge", new Art { C1 = Hex(0xE6EDF3), C2 = Hex(0xE5534B),
                F1 = "M5 25a13 13 0 1 1 22 0Z", F2 = "M24.6 9.4l1.8-1.6a13 13 0 0 1 2.5 6.6l-2.4.3a10.6 10.6 0 0 0-1.9-5.3Z",
                L1 = "M5 25a13 13 0 1 1 22 0Z M3 25h26",
                L2 = "M16 7.5V10 M8.6 10.6l1.8 1.8 M23.4 10.6l-1.8 1.8 M5.5 18H8 M24 18h2.5 M16 19l5.5-6.5 M14 19a2 2 0 1 0 4 0a2 2 0 1 0-4 0" } },
            { "disk", new Art { C1 = Hex(0x8B949E), C2 = Hex(0xD0D7DE),
                F1 = "M5 3h22v26H5Z", F2 = "M8.5 13a7.5 7.5 0 1 0 15 0a7.5 7.5 0 1 0-15 0Z",
                L1 = "M5 3h22v26H5Z M8.5 13a7.5 7.5 0 1 0 15 0a7.5 7.5 0 1 0-15 0",
                L2 = "M15 13a1 1 0 1 0 2 0a1 1 0 1 0-2 0 M23 25l-6-11 M9 25.5h6 M7.5 5.5h.01 M24.5 5.5h.01 M7.5 26.5h.01 M24.5 26.5h.01" } },
            { "monitor", new Art { C1 = Hex(0x4B535D), C2 = Hex(0x58A6FF),
                F1 = "M3 5h26v17H3Z", F2 = "M5 7h22v13H5Z", L1 = "M3 5h26v17H3Z M13.5 22l-1 5 M18.5 22l1 5 M9 27h14",
                L2 = "M7 16.5l4-4 3 3 4-5 3 3 4-2.5 M16 21h.01" } },
            { "bolt", new Art { C1 = Hex(0xFFD166), C2 = Hex(0xFFF3C4),
                F1 = "M19 2L6 18h8.5L12 30l14-17h-8.5L19 2Z", F2 = "M18.5 4.5L8.5 17h4.5Z",
                L1 = "M19 2L6 18h8.5L12 30l14-17h-8.5L19 2Z", L2 = "M25.5 4l1.8-1.8 M27 8.5h2.5 M4.5 25.5l-1.8 1.8 M3 22h-1" } },
            { "terminal", new Art { C1 = Hex(0x4B535D), C2 = Hex(0x3FB950),
                F1 = "M3 5h26v22H3Z", F2 = "M3 5h26v4H3Z M17 18.5h4.5v2.5H17Z", L1 = "M3 5h26v22H3Z M3 9h26",
                L2 = "M8 13.5l3.5 3.2L8 20 M6 7h.01 M8.5 7h.01 M11 7h.01 M7 24h18" } },
            { "gpu", new Art { C1 = Hex(0x4B535D), C2 = Hex(0x7EE2A0),
                F1 = "M2 8h24v14H2Z", F2 = "M5.5 15a4 4 0 1 0 8 0a4 4 0 1 0-8 0Z M14.5 15a4 4 0 1 0 8 0a4 4 0 1 0-8 0Z",
                L1 = "M2 8h24v14H2Z M26 6v20 M26 6h3 M26 26h3 M6 22v3h12v-3 M5.5 15a4 4 0 1 0 8 0a4 4 0 1 0-8 0 M14.5 15a4 4 0 1 0 8 0a4 4 0 1 0-8 0",
                L2 = "M9.5 11v8 M5.5 15h8 M18.5 11v8 M14.5 15h8 M8 25v-1.5 M10 25v-1.5 M12 25v-1.5 M14 25v-1.5 M16 25v-1.5 M23.5 10.5h.01 M23.5 19.5h.01" } },
            { "memory", new Art { C1 = Hex(0x1F8A47), C2 = Hex(0x2D333B),
                F1 = "M2 9h28v12H2Z", F2 = "M5 12h4v6H5Z M11 12h4v6h-4Z M17 12h4v6h-4Z M23 12h4v6h-4Z", L1 = "M2 9h28v12H2Z M15 21v-2h2v2",
                L2 = "M4 21v3 M6.5 21v3 M9 21v3 M11.5 21v3 M20.5 21v3 M23 21v3 M25.5 21v3 M28 21v3 M2 11h1 M29 11h1" } },
            { "fan", new Art { C1 = Hex(0x4B535D), C2 = Hex(0x79A8F0),
                F1 = "M3 3h26v26H3Z",
                F2 = "M16 14c0-4 1-7.5 4.5-7.5 2.5 0 2.5 3.5-1 6Z M18 16c4 0 7.5 1 7.5 4.5 0 2.5-3.5 2.5-6-1Z M16 18c0 4-1 7.5-4.5 7.5-2.5 0-2.5-3.5 1-6Z M14 16c-4 0-7.5-1-7.5-4.5 0-2.5 3.5-2.5 6 1Z",
                L1 = "M3 3h26v26H3Z M5.5 16a10.5 10.5 0 1 0 21 0a10.5 10.5 0 1 0-21 0",
                L2 = "M6 6h.01 M26 6h.01 M6 26h.01 M26 26h.01 M14 16a2 2 0 1 0 4 0a2 2 0 1 0-4 0 M16 14c0-4 1-7.5 4.5-7.5 2.5 0 2.5 3.5-1 6 M18 16c4 0 7.5 1 7.5 4.5 0 2.5-3.5 2.5-6-1 M16 18c0 4-1 7.5-4.5 7.5-2.5 0-2.5-3.5 1-6 M14 16c-4 0-7.5-1-7.5-4.5 0-2.5 3.5-2.5 6 1" } },
            { "battery", new Art { C1 = Hex(0xE6EDF3), C2 = Hex(0x3FB950),
                F1 = "M3 9h24v14H3Z", F2 = "M6 12h4v8H6Z M11 12h4v8h-4Z M16 12h4v8h-4Z", L1 = "M3 9h24v14H3Z M27 13h2.5v6H27",
                L2 = "M23.3 13l-1.8 3.3h2.4L22 19.5" } },
            { "tools", new Art { C1 = Hex(0xC9D1D9), C2 = Hex(0xE5534B),
                F1 = "M19.6 8.4a1.3 1.3 0 0 0 0 1.9l2.1 2.1a1.3 1.3 0 0 0 1.9 0l5-5a8 8 0 0 1-10.6 10.6l-9.2 9.2a2.8 2.8 0 0 1-4-4l9.2-9.2a8 8 0 0 1 10.6-10.6Z",
                F2 = "M20.5 20.5l6 6a1.9 1.9 0 0 1-2.7 2.7l-6-6Z",
                L1 = "M19.6 8.4a1.3 1.3 0 0 0 0 1.9l2.1 2.1a1.3 1.3 0 0 0 1.9 0l5-5a8 8 0 0 1-10.6 10.6l-9.2 9.2a2.8 2.8 0 0 1-4-4l9.2-9.2a8 8 0 0 1 10.6-10.6Z M20.5 20.5l6 6a1.9 1.9 0 0 1-2.7 2.7l-6-6",
                L2 = "M6.5 25.5h.01 M3.5 3.5l2.5 1 13 13 M3.5 3.5l1 2.5" } },
            { "stopwatch", new Art { C1 = Hex(0xE6EDF3), C2 = Hex(0xD29922),
                F1 = "M6 18a10 10 0 1 0 20 0a10 10 0 1 0-20 0Z", F2 = "M13 2.5h6v3h-6Z M24.5 8.2l1.9-1.9 1.8 1.8-1.9 1.9Z M16 18v-8a8 8 0 0 1 8 8Z",
                L1 = "M6 18a10 10 0 1 0 20 0a10 10 0 1 0-20 0 M16 5.5V8 M13 2.5h6v3h-6Z",
                L2 = "M16 10v1.5 M24 18h-1.5 M16 26v-1.5 M8 18h1.5 M16 18l4.5-4.5 M21.7 12.3l1-1 M10.3 23.7l1-1" } },
        };

        public static bool Has(string name) { return name != null && Arts.ContainsKey(name); }

        // Draws a profile icon into r in the current look. Below about 22 px the fine detail lines are left out
        // (they would only blur) and the outline gets heavier.
        public static void Draw(Graphics g, string name, RectangleF r)
        {
            Art art;
            if (name == null || !Arts.TryGetValue(name, out art)) art = Arts["terminal"];

            Color c1, c2, ink, ink2;
            if (Theme.Terminal)
            {
                c1 = Theme.Field;
                c2 = Color.FromArgb(102, Theme.Accent);
                ink = Theme.Accent;
                ink2 = Theme.BarText;
            }
            else
            {
                c1 = art.C1;
                c2 = art.C2;
                ink = Theme.IsDark ? Hex(0x0D1117) : Hex(0x1F2328);
                ink2 = ink;
            }

            float size = Math.Min(r.Width, r.Height);
            bool small = size < Theme.S(22f);
            bool medium = !small && size < Theme.S(36f);
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TranslateTransform(r.X + (r.Width - size) / 2f, r.Y + (r.Height - size) / 2f);
            g.ScaleTransform(size / 32f, size / 32f);
            using (var b1 = new SolidBrush(c1)) g.FillPath(b1, Path(art.F1));
            using (var b2 = new SolidBrush(c2)) g.FillPath(b2, Path(art.F2));
            var cap = Theme.Terminal ? LineCap.Square : LineCap.Round;
            var join = Theme.Terminal ? LineJoin.Miter : LineJoin.Round;
            using (var p1 = new Pen(ink, small ? 2.4f : medium ? 1.7f : 1.4f) { StartCap = cap, EndCap = cap, LineJoin = join })
                g.DrawPath(p1, Path(art.L1));
            if (!small)
                using (var p2 = new Pen(ink2, medium ? 1.3f : 1.05f) { StartCap = cap, EndCap = cap, LineJoin = join })
                    g.DrawPath(p2, Path(art.L2));
            g.Restore(state);
        }

        // ---- running: the same drawings, moving (mockup K1) ----

        static readonly string[] Pins =
        {
            "M11 4v4", "M14.3 4v4", "M17.7 4v4", "M21 4v4", "M24 11h4", "M24 14.3h4", "M24 17.7h4", "M24 21h4",
            "M21 24v4", "M17.7 24v4", "M14.3 24v4", "M11 24v4", "M4 21h4", "M4 17.7h4", "M4 14.3h4", "M4 11h4"
        };
        static readonly string[] MemoryChips = { "M5 12h4v6H5Z", "M11 12h4v6h-4Z", "M17 12h4v6h-4Z", "M23 12h4v6h-4Z" };
        static readonly string[] BatteryLevels = { "", "M6 12h4v8H6Z", "M6 12h4v8H6Z M11 12h4v8h-4Z", "M6 12h4v8H6Z M11 12h4v8h-4Z M16 12h4v8h-4Z" };

        // Draws a profile icon at moment t (seconds) of its running motion: fans spin, the mercury rises and falls,
        // the flame flickers, the needle swings, the disk turns, memory chips light up in turn, and so on.
        public static void DrawRunning(Graphics g, string name, RectangleF r, double t)
        {
            Art art;
            if (name == null || !Arts.TryGetValue(name, out art)) { name = "terminal"; art = Arts[name]; }

            Color c1, c2, ink, ink2, hi, green;
            if (Theme.Terminal)
            {
                c1 = Theme.Field; c2 = Color.FromArgb(102, Theme.Accent); ink = Theme.Accent; ink2 = Theme.BarText; hi = Theme.BarText; green = Theme.Accent;
            }
            else
            {
                c1 = art.C1; c2 = art.C2; ink = Theme.IsDark ? Hex(0x0D1117) : Hex(0x1F2328); ink2 = ink; hi = Hex(0x7EE2A0); green = Hex(0x3FB950);
            }
            float size = Math.Min(r.Width, r.Height);
            bool small = size < Theme.S(22f), medium = !small && size < Theme.S(36f);
            float w1 = small ? 2.4f : medium ? 1.7f : 1.4f, w2 = medium ? 1.3f : 1.05f;
            var cap = Theme.Terminal ? LineCap.Square : LineCap.Round;
            var join = Theme.Terminal ? LineJoin.Miter : LineJoin.Round;

            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TranslateTransform(r.X + (r.Width - size) / 2f, r.Y + (r.Height - size) / 2f);
            g.ScaleTransform(size / 32f, size / 32f);

            Func<Color, double, Color> A = (c, a) => Color.FromArgb(Math.Max(0, Math.Min(255, (int)(c.A * a))), c);
            Action<Matrix, Action> with = (m, draw) =>
            {
                var s = g.Save();
                if (m != null) g.MultiplyTransform(m);
                draw();
                g.Restore(s);
                if (m != null) m.Dispose();
            };
            Action<string, Color, Matrix> fill = (d, c, m) => with(m, () => { using (var b = new SolidBrush(c)) g.FillPath(b, Path(d)); });
            Action<string, Color, float, Matrix> line = (d, c, w, m) => with(m, () =>
            {
                using (var p = new Pen(c, w) { StartCap = cap, EndCap = cap, LineJoin = join }) g.DrawPath(p, Path(d));
            });
            Action<string, Matrix> outline = (d, m) => line(d, ink, w1, m);
            Action<string, Matrix> detail = (d, m) => { if (!small) line(d, ink2, w2, m); else if (m != null) m.Dispose(); };
            Action<float, float, float, float, Color, float> seg = (x1, y1, x2, y2, c, w) =>
            {
                using (var p = new Pen(c, w) { StartCap = cap, EndCap = cap }) g.DrawLine(p, x1, y1, x2, y2);
            };
            Func<float, float, float, Matrix> rotate = (deg, cx, cy) => { var m = new Matrix(); m.RotateAt(deg, new PointF(cx, cy)); return m; };
            Func<float, float, float, float, Matrix> scaleAt = (sx, sy, cx, cy) =>
            {
                var m = new Matrix();
                m.Translate(cx, cy);
                m.Scale(sx, sy);
                m.Translate(-cx, -cy);
                return m;
            };
            double sin(double v) { return Math.Sin(v); }
            const double Rad = Math.PI / 180;

            switch (name)
            {
                case "thermometer":
                {
                    float top = (float)(20 - (0.5 + 0.5 * sin(t * 1.4)) * 10);
                    fill(art.F1, c1, null);
                    var s = g.Save();
                    g.IntersectClip(new RectangleF(0, top, 32, 32 - top));   // only the mercury below the level
                    fill(art.F2, c2, null);
                    g.Restore(s);
                    outline(art.L1, null);
                    detail(art.L2, null);
                    break;
                }
                case "chip":
                {
                    int i = (int)(t * 8) % 16;
                    fill(art.F1, c1, null);
                    fill(art.F2, A(c2, 0.55 + 0.45 * (0.5 + 0.5 * sin(t * 3))), null);
                    outline(art.L1, null);
                    detail(string.Join(" ", Pins), null);
                    if (!small) line(Pins[i] + " " + Pins[(i + 1) % 16], green, w2 * 1.5f, null);
                    break;
                }
                case "gpu":
                {
                    float a = (float)(t * 260 % 360);
                    fill(art.F1, c1, null);
                    fill(art.F2, c2, null);
                    outline(art.L1, null);
                    detail("M9.5 11v8 M5.5 15h8", rotate(a, 9.5f, 15));
                    detail("M18.5 11v8 M14.5 15h8", rotate(a, 18.5f, 15));
                    detail("M8 25v-1.5 M10 25v-1.5 M12 25v-1.5 M14 25v-1.5 M16 25v-1.5 M23.5 10.5h.01 M23.5 19.5h.01", null);
                    break;
                }
                case "memory":
                    fill(art.F1, c1, null);
                    fill(art.F2, c2, null);
                    fill(MemoryChips[(int)(t * 3) % 4], hi, null);
                    outline(art.L1, null);
                    detail(art.L2, null);
                    break;
                case "network":
                {
                    fill(art.F1, c1, null);
                    fill(art.F2, c2, null);
                    outline(art.L1, null);
                    detail("M16 11v4.5 M7 20v-4.5h18V20 M5 29h4 M23 29h4", null);
                    // a data dot runs from the server down and out to both screens
                    double p = t * 0.7 % 1;
                    foreach (float side in new[] { 7f, 25f })
                    {
                        float[][] pts = { new[] { 16f, 11f }, new[] { 16f, 15.5f }, new[] { side, 15.5f }, new[] { side, 20f } };
                        double[] lens = { 4.5, Math.Abs(side - 16), 4.5 };
                        double d = p * (lens[0] + lens[1] + lens[2]);
                        float x = side, y = 20;
                        for (int k = 0; k < 3; k++)
                        {
                            if (d <= lens[k]) { double f = d / lens[k]; x = (float)(pts[k][0] + (pts[k + 1][0] - pts[k][0]) * f); y = (float)(pts[k][1] + (pts[k + 1][1] - pts[k][1]) * f); break; }
                            d -= lens[k];
                        }
                        using (var b = new SolidBrush(hi)) g.FillEllipse(b, x - 1.3f, y - 1.3f, 2.6f, 2.6f);
                        using (var pen = new Pen(ink, 0.6f)) g.DrawEllipse(pen, x - 1.3f, y - 1.3f, 2.6f, 2.6f);
                    }
                    if (!small) line((int)(t * 4) % 2 == 0 ? "M14 9.3h.01 M18 9.3h.01" : "M16 9.3h.01", hi, 1.4f, null);
                    break;
                }
                case "flame":
                {
                    float sy = (float)(1 + 0.07 * sin(t * 7) + 0.03 * sin(t * 13)), sx = (float)(1 - 0.03 * sin(t * 7));
                    float iy = (float)(1 + 0.12 * sin(t * 9 + 1)), ix = (float)(1 - 0.05 * sin(t * 11));
                    fill(art.F1, c1, scaleAt(sx, sy, 16, 29));
                    fill(art.F2, c2, scaleAt(ix, iy, 16, 29));
                    detail(art.F2, scaleAt(ix, iy, 16, 29));
                    outline(art.F1, scaleAt(sx, sy, 16, 29));
                    detail("M21 12.5c1.1 1.6 1.6 3.3 1.6 5", scaleAt(sx, sy, 16, 29));
                    break;
                }
                case "gauge":
                {
                    double ang = (-90 + 72 * sin(t * 1.6)) * Rad;
                    fill(art.F1, c1, null);
                    fill(art.F2, c2, null);
                    outline(art.L1, null);
                    detail("M16 7.5V10 M8.6 10.6l1.8 1.8 M23.4 10.6l-1.8 1.8 M5.5 18H8 M24 18h2.5", null);
                    seg(16, 19, (float)(16 + 7.5 * Math.Cos(ang)), (float)(19 + 7.5 * Math.Sin(ang)), ink, small ? w1 : 1.6f);
                    using (var b = new SolidBrush(ink)) g.FillEllipse(b, 14.6f, 17.6f, 2.8f, 2.8f);
                    break;
                }
                case "disk":
                {
                    double a = t * 300 * Rad;
                    fill(art.F1, c1, null);
                    fill(art.F2, c2, null);
                    outline(art.L1, null);
                    seg(16, 13, (float)(16 + 6.5 * Math.Cos(a)), (float)(13 + 6.5 * Math.Sin(a)), Theme.Terminal ? ink2 : c1, w1);
                    detail("M15 13a1 1 0 1 0 2 0a1 1 0 1 0-2 0 M9 25.5h6 M7.5 5.5h.01 M24.5 5.5h.01 M7.5 26.5h.01 M24.5 26.5h.01", null);
                    line("M23 25l-6-11", ink, w1, rotate((float)(9 * sin(t * 2.5)), 23, 25));
                    break;
                }
                case "monitor":
                {
                    fill(art.F1, c1, null);
                    fill(art.F2, c2, null);
                    outline(art.L1, null);
                    var pts = new List<PointF>();
                    for (float x = 7; x <= 25.01f; x += 1.5f)
                        pts.Add(new PointF(x, (float)(14 + 2.8 * sin(x * 0.7 - t * 4) + 0.8 * sin(x * 1.9 - t * 7))));
                    using (var p = new Pen(ink2, small ? w1 : 1.1f) { LineJoin = join }) g.DrawLines(p, pts.ToArray());
                    if (!small) line("M16 21h.01", green, 1.4f, null);
                    break;
                }
                case "bolt":
                {
                    double flash = Math.Max(0, sin(t * 5));
                    line(art.F1, A(c1, 0.45 * flash), 3.5f, null);   // glow behind
                    fill(art.F1, c1, null);
                    fill(art.F2, A(c2, 0.4 + 0.6 * flash), null);
                    outline(art.L1, null);
                    if (!small) line(art.L2, A(ink2, 0.2 + 0.8 * Math.Abs(sin(t * 4.3))), w2, null);
                    break;
                }
                case "fan":
                {
                    float a = (float)(t * 320 % 360);
                    fill(art.F1, c1, null);
                    fill(art.F2, c2, rotate(a, 16, 16));
                    outline(art.L1, null);
                    detail(art.F2, rotate(a, 16, 16));
                    detail("M6 6h.01 M26 6h.01 M6 26h.01 M26 26h.01 M14 16a2 2 0 1 0 4 0a2 2 0 1 0-4 0", null);
                    break;
                }
                case "battery":
                {
                    fill(art.F1, c1, null);
                    string cells = BatteryLevels[(int)(t * 1.6) % 4];
                    if (cells.Length > 0) fill(cells, c2, null);
                    outline(art.L1, null);
                    if (!small) line(art.L2, A(ink2, 0.35 + 0.65 * Math.Abs(sin(t * 3))), w2, null);
                    break;
                }
                case "tools":
                {
                    float a = (float)(14 * sin(t * 3.2));
                    fill(art.F1, c1, rotate(a, 16, 16));
                    fill(art.F2, c2, null);
                    outline(art.F1, rotate(a, 16, 16));
                    outline("M20.5 20.5l6 6a1.9 1.9 0 0 1-2.7 2.7l-6-6", null);
                    detail("M6.5 25.5h.01 M3.5 3.5l2.5 1 13 13 M3.5 3.5l1 2.5", null);
                    break;
                }
                case "stopwatch":
                {
                    float sweep = (float)(t * 70 % 360);
                    double a = (-90 + sweep) * Rad;
                    fill(art.F1, c1, null);
                    if (sweep > 1) using (var b = new SolidBrush(A(c2, 0.55))) g.FillPie(b, 8, 10, 16, 16, -90, sweep);
                    fill("M13 2.5h6v3h-6Z M24.5 8.2l1.9-1.9 1.8 1.8-1.9 1.9Z", c2, null);
                    outline(art.L1, null);
                    detail("M16 10v1.5 M24 18h-1.5 M16 26v-1.5 M8 18h1.5", null);
                    seg(16, 18, (float)(16 + 6.5 * Math.Cos(a)), (float)(18 + 6.5 * Math.Sin(a)), ink, small ? w1 : 1.5f);
                    break;
                }
                default:   // terminal: text types, the cursor blinks
                {
                    float len = (float)(t * 5 % 12);
                    bool on = (int)(t * 2.5) % 2 == 0;
                    fill(art.F1, c1, null);
                    fill("M3 5h26v4H3Z", c2, null);
                    using (var b = new SolidBrush(A(c2, on ? 1 : 0.15))) g.FillRectangle(b, 13.5f + len, 18.5f, 3.5f, 2.5f);
                    outline(art.L1, null);
                    detail("M8 13.5l3.5 3.2L8 20 M6 7h.01 M8.5 7h.01 M11 7h.01", null);
                    if (len > 0.5f && !small) seg(13, 19.75f, 13 + len, 19.75f, Theme.Terminal ? ink2 : Hex(0xD7FFF6), 1.3f);
                    break;
                }
            }
            g.Restore(state);
        }

        // ---- SVG path data -> GraphicsPath ----

        static readonly Dictionary<string, GraphicsPath> cache = new Dictionary<string, GraphicsPath>();
        static readonly Regex Token = new Regex(@"[MmLlHhVvCcAaZz]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?");

        // Supports what the drawings use: M L H V C A Z, upper and lower case, with repeated arguments.
        static GraphicsPath Path(string d)
        {
            GraphicsPath gp;
            if (cache.TryGetValue(d ?? "", out gp)) return gp;
            gp = new GraphicsPath(FillMode.Winding);
            var tokens = new List<string>();
            foreach (Match m in Token.Matches(d ?? "")) tokens.Add(m.Value);

            int i = 0;
            char cmd = 'M';
            float x = 0, y = 0, sx = 0, sy = 0;
            Func<float> num = () => float.Parse(tokens[i++], CultureInfo.InvariantCulture);
            while (i < tokens.Count)
            {
                if (char.IsLetter(tokens[i][0]) && tokens[i] != "e" && tokens[i] != "E") cmd = tokens[i++][0];
                bool rel = char.IsLower(cmd);
                switch (char.ToUpperInvariant(cmd))
                {
                    case 'M':
                    {
                        float nx = num(), ny = num();
                        if (rel) { nx += x; ny += y; }
                        gp.StartFigure();
                        x = sx = nx; y = sy = ny;
                        cmd = rel ? 'l' : 'L';   // more pairs after a move are lines
                        break;
                    }
                    case 'L':
                    {
                        float nx = num(), ny = num();
                        if (rel) { nx += x; ny += y; }
                        gp.AddLine(x, y, nx, ny);
                        x = nx; y = ny;
                        break;
                    }
                    case 'H':
                    {
                        float nx = num();
                        if (rel) nx += x;
                        gp.AddLine(x, y, nx, y);
                        x = nx;
                        break;
                    }
                    case 'V':
                    {
                        float ny = num();
                        if (rel) ny += y;
                        gp.AddLine(x, y, x, ny);
                        y = ny;
                        break;
                    }
                    case 'C':
                    {
                        float x1 = num(), y1 = num(), x2 = num(), y2 = num(), nx = num(), ny = num();
                        if (rel) { x1 += x; y1 += y; x2 += x; y2 += y; nx += x; ny += y; }
                        gp.AddBezier(x, y, x1, y1, x2, y2, nx, ny);
                        x = nx; y = ny;
                        break;
                    }
                    case 'A':
                    {
                        float rx = num(), ry = num();
                        num();   // rotation (the drawings use 0)
                        bool large = num() != 0, sweep = num() != 0;
                        float nx = num(), ny = num();
                        if (rel) { nx += x; ny += y; }
                        AddArc(gp, x, y, rx, ry, large, sweep, nx, ny);
                        x = nx; y = ny;
                        break;
                    }
                    case 'Z':
                        gp.CloseFigure();
                        x = sx; y = sy;
                        break;
                    default:
                        i++;   // unknown: skip
                        break;
                }
            }
            cache[d ?? ""] = gp;
            return gp;
        }

        // An SVG arc (no rotation) as Bézier curves, following the SVG spec's endpoint-to-centre conversion.
        static void AddArc(GraphicsPath gp, float x1, float y1, float rx, float ry, bool large, bool sweep, float x2, float y2)
        {
            rx = Math.Abs(rx); ry = Math.Abs(ry);
            if (rx < 1e-4f || ry < 1e-4f || (Math.Abs(x1 - x2) < 1e-5f && Math.Abs(y1 - y2) < 1e-5f))
            {
                gp.AddLine(x1, y1, x2, y2);
                return;
            }
            double dx = (x1 - x2) / 2.0, dy = (y1 - y2) / 2.0;
            double lambda = dx * dx / (rx * rx) + dy * dy / (ry * ry);
            if (lambda > 1) { rx *= (float)Math.Sqrt(lambda); ry *= (float)Math.Sqrt(lambda); }
            double num = rx * rx * ry * ry - rx * rx * dy * dy - ry * ry * dx * dx;
            double den = rx * rx * dy * dy + ry * ry * dx * dx;
            double coef = (large != sweep ? 1 : -1) * Math.Sqrt(Math.Max(0, num / den));
            double cxp = coef * rx * dy / ry, cyp = coef * -ry * dx / rx;
            double cx = cxp + (x1 + x2) / 2.0, cy = cyp + (y1 + y2) / 2.0;
            double t1 = Math.Atan2((dy - cyp) / ry, (dx - cxp) / rx);
            double t2 = Math.Atan2((-dy - cyp) / ry, (-dx - cxp) / rx);
            double dt = t2 - t1;
            if (sweep && dt < 0) dt += 2 * Math.PI;
            if (!sweep && dt > 0) dt -= 2 * Math.PI;

            int segments = (int)Math.Ceiling(Math.Abs(dt) / (Math.PI / 2) - 1e-9);
            if (segments < 1) segments = 1;
            double step = dt / segments;
            double alpha = 4.0 / 3.0 * Math.Tan(step / 4);
            double a = t1;
            for (int s = 0; s < segments; s++)
            {
                double b = a + step;
                double ax = cx + rx * Math.Cos(a), ay = cy + ry * Math.Sin(a);
                double bx = cx + rx * Math.Cos(b), by = cy + ry * Math.Sin(b);
                double c1x = ax - alpha * rx * Math.Sin(a), c1y = ay + alpha * ry * Math.Cos(a);
                double c2x = bx + alpha * rx * Math.Sin(b), c2y = by - alpha * ry * Math.Cos(b);
                gp.AddBezier((float)ax, (float)ay, (float)c1x, (float)c1y, (float)c2x, (float)c2y, (float)bx, (float)by);
                a = b;
            }
        }
    }
}
