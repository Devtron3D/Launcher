using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace LauncherApp
{
    // Line icons drawn with GDI+ on a 24x24 grid, so they are sharp at every DPI and need no image files.
    static class Icons
    {
        // Icons a profile can have (saved by name in profiles.json). Drawn by ProfileIconArt.
        // Grouped: computers, parts, devices, network, tests, benchmarking, utilities.
        public static readonly string[] ProfileIcons =
        {
            "laptop", "desktop", "server", "phone",
            "chip", "gpu", "memory", "motherboard", "psu", "fan", "disk", "ssd", "usb", "sdcard",
            "monitor", "keyboard", "mouse", "printer", "camera", "speaker", "headphones", "gamepad",
            "network", "wifi", "router", "globe", "cloud",
            "thermometer", "flame", "gauge", "stopwatch", "hourglass", "calendar", "chart", "bolt", "battery", "plug",
            "trophy", "target", "checklist", "flask", "magnifier", "pulse", "dumbbell", "rocket", "loop", "snowflake",
            "warning", "filmstrip", "info",
            "terminal", "tools", "bug", "shield", "download"
        };

        // "gpu" -> "Graphics card": the name shown in the editor.
        public static string Label(string name)
        {
            switch (name)
            {
                case "gpu": return "Graphics card";
                case "tools": return "Tools";
                case "desktop": return "Desktop PC";
                case "psu": return "Power supply";
                case "ssd": return "SSD";
                case "usb": return "USB stick";
                case "sdcard": return "SD card";
                case "gamepad": return "Controller";
                case "wifi": return "Wi-Fi";
                case "globe": return "Internet";
                case "chart": return "Results";
                case "shield": return "Security";
                case "disk": return "Hard disk";
                case "trophy": return "Score";
                case "flask": return "Lab test";
                case "magnifier": return "Inspect";
                case "dumbbell": return "Load";
                case "rocket": return "Performance";
                case "loop": return "Repeat";
                case "snowflake": return "Cooling";
                case "filmstrip": return "FPS";
                case "info": return "System info";
                default: return string.IsNullOrEmpty(name) ? "" : char.ToUpperInvariant(name[0]) + name.Substring(1);
            }
        }

        // Draws a profile's icon (the detailed, coloured drawings) into r.
        public static void DrawProfile(Graphics g, string name, RectangleF r)
        {
            ProfileIconArt.Draw(g, name, r);
        }

        // The icon for a profile: the one chosen in the editor, or a guess from its name.
        public static string For(Profile p)
        {
            if (p != null && !string.IsNullOrEmpty(p.Icon) && Array.IndexOf(ProfileIcons, p.Icon) >= 0) return p.Icon;
            return Guess(p == null ? "" : p.Name);
        }

        public static string Guess(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            if (Has(n, "laptop", "notebook")) return "laptop";
            if (Has(n, "server")) return "server";
            if (Has(n, "desktop", "tower", "workstation")) return "desktop";
            if (Has(n, "phone", "android", "iphone", "mobile", "tablet")) return "phone";
            if (Has(n, "usb", "flash drive", "pendrive", "thumb drive")) return "usb";
            if (Has(n, "sd card", "sdcard", "microsd")) return "sdcard";
            if (Has(n, "ssd", "nvme", "m.2")) return "ssd";
            if (Has(n, "motherboard", "mainboard", "bios", "uefi")) return "motherboard";
            if (Has(n, "psu", "power supply")) return "psu";
            if (Has(n, "keyboard", "keys")) return "keyboard";
            if (Has(n, "mouse")) return "mouse";
            if (Has(n, "print")) return "printer";
            if (Has(n, "camera", "webcam")) return "camera";
            if (Has(n, "headphone", "headset")) return "headphones";
            if (Has(n, "audio", "sound", "speaker")) return "speaker";
            if (Has(n, "game", "controller", "gamepad", "joystick")) return "gamepad";
            if (Has(n, "wifi", "wi-fi", "wireless", "wlan")) return "wifi";
            if (Has(n, "router")) return "router";
            if (Has(n, "internet", "browser", "web")) return "globe";
            if (Has(n, "cloud", "backup", "upload", "sync")) return "cloud";
            if (Has(n, "virus", "malware", "security", "defender", "antivirus")) return "shield";
            if (Has(n, "debug", "bug", "diagnos", "crash")) return "bug";
            if (Has(n, "update", "download", "driver", "install")) return "download";
            if (Has(n, "schedule", "calendar", "daily", "weekly", "nightly")) return "calendar";
            if (Has(n, "wait", "idle", "overnight")) return "hourglass";
            if (Has(n, "result", "report", "score", "chart", "log")) return "chart";
            if (Has(n, "score", "leaderboard", "record")) return "trophy";
            if (Has(n, "fps", "frame", "video", "render")) return "filmstrip";
            if (Has(n, "loop", "repeat", "cycle", "endurance", "24h", "soak")) return "loop";
            if (Has(n, "load test", "workload", "weight")) return "dumbbell";
            if (Has(n, "performance", "boost", "speed", "overclock")) return "rocket";
            if (Has(n, "checklist", "test plan", "qa ", "checks")) return "checklist";
            if (Has(n, "lab", "experiment")) return "flask";
            if (Has(n, "scan", "inspect")) return "magnifier";
            if (Has(n, "pulse", "heart", "vital")) return "pulse";
            if (Has(n, "frost", "freeze", "cold", "chill")) return "snowflake";
            if (Has(n, "stability", "error", "warn")) return "warning";
            if (Has(n, "info", "specs", "cpu-z", "cpuz", "inventory")) return "info";
            if (Has(n, "target", "accuracy", "goal")) return "target";
            if (Has(n, "gpu", "graphic", "furmark", "3dmark", "occt gpu", "unigine")) return "gpu";
            if (Has(n, "memtest", "memory", "ram ", " ram", "dimm")) return "memory";
            if (Has(n, "stress", "prime", "burn", "heat", "temp", "thermal")) return "thermometer";
            if (Has(n, "bench", "cpu", "chip")) return "chip";
            if (Has(n, "net", "ping", "wifi", "lan", "ip ")) return "network";
            if (Has(n, "disk", "ssd", "hdd", "nvme", "storage", "drive")) return "disk";
            if (Has(n, "fan", "cool")) return "fan";
            if (Has(n, "battery", "charge")) return "battery";
            if (Has(n, "plug", "cable", "socket")) return "plug";
            if (Has(n, "sensor", "monitor", "gauge")) return "gauge";
            if (Has(n, "power", "psu", "volt")) return "bolt";
            if (Has(n, "display", "screen")) return "monitor";
            if (Has(n, "timer", "time", "burn-in", "soak")) return "stopwatch";
            if (Has(n, "tool", "repair", "fix", "service", "setup")) return "tools";
            return "terminal";
        }

        static bool Has(string s, params string[] words)
        {
            foreach (var w in words) if (s.Contains(w)) return true;
            return false;
        }

        // Draws an icon into r (any size; the 24x24 grid is scaled to fit).
        public static void Draw(Graphics g, string name, RectangleF r, Color color, float stroke = 1.75f)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 24f, r.Height / 24f);
            using (var pen = new Pen(color, stroke) { LineJoin = LineJoin.Miter, StartCap = LineCap.Square, EndCap = LineCap.Square })
            {
                switch (name)
                {
                    case "thermometer":
                        g.DrawLine(pen, 10, 4, 10, 14.8f);
                        g.DrawLine(pen, 14, 4, 14, 14.8f);
                        g.DrawArc(pen, 10, 2, 4, 4, 180, 180);
                        g.DrawArc(pen, 8, 14, 8, 8, -58, 296);
                        g.DrawLine(pen, 12, 9, 12, 16);
                        break;
                    case "chip":
                        g.DrawRectangle(pen, 6, 6, 12, 12);
                        g.DrawRectangle(pen, 9.5f, 9.5f, 5, 5);
                        foreach (float v in new[] { 9f, 15f })
                        {
                            g.DrawLine(pen, v, 2, v, 6);
                            g.DrawLine(pen, v, 18, v, 22);
                            g.DrawLine(pen, 2, v, 6, v);
                            g.DrawLine(pen, 18, v, 22, v);
                        }
                        break;
                    case "network":
                        g.DrawRectangle(pen, 9, 2, 6, 6);
                        g.DrawRectangle(pen, 2, 16, 6, 6);
                        g.DrawRectangle(pen, 16, 16, 6, 6);
                        g.DrawLine(pen, 12, 8, 12, 12);
                        g.DrawLines(pen, new[] { new PointF(5, 16), new PointF(5, 12), new PointF(19, 12), new PointF(19, 16) });
                        break;
                    case "flame":
                        using (var path = new GraphicsPath())
                        {
                            path.AddBezier(12, 22, 7.5f, 22, 5.5f, 18, 6.5f, 14.5f);
                            path.AddBezier(6.5f, 14.5f, 7.2f, 12, 8.8f, 11.5f, 9, 9.5f);
                            path.AddBezier(9, 9.5f, 10.5f, 11, 11, 12, 11, 13);
                            path.AddBezier(11, 13, 11, 9, 12.5f, 5, 14, 2);
                            path.AddBezier(14, 2, 15, 6, 18.5f, 9.5f, 18, 15);
                            path.AddBezier(18, 15, 17.7f, 19, 15.5f, 22, 12, 22);
                            g.DrawPath(pen, path);
                        }
                        break;
                    case "gauge":
                        g.DrawArc(pen, 3, 4, 18, 18, 150, 240);
                        g.DrawLine(pen, 12, 13, 16.5f, 8.5f);
                        g.DrawEllipse(pen, 11, 12, 2, 2);
                        break;
                    case "disk":
                        g.DrawRectangle(pen, 3, 5, 18, 14);
                        g.DrawEllipse(pen, 12, 8.5f, 6, 6);
                        g.DrawLine(pen, 6, 16, 9, 16);
                        break;
                    case "monitor":
                        g.DrawRectangle(pen, 2, 3, 20, 13);
                        g.DrawLine(pen, 12, 16, 12, 21);
                        g.DrawLine(pen, 8, 21, 16, 21);
                        break;
                    case "bolt":
                        g.DrawPolygon(pen, new[] { new PointF(13, 2), new PointF(4, 14), new PointF(11, 14), new PointF(10, 22), new PointF(20, 9), new PointF(13, 9) });
                        break;
                    case "pencil":
                        g.DrawPolygon(pen, new[] { new PointF(3, 21), new PointF(3, 17), new PointF(16.5f, 3.5f), new PointF(20.5f, 7.5f), new PointF(7, 21) });
                        g.DrawLine(pen, 13, 21, 21, 21);
                        break;
                    case "sliders":
                        g.DrawLine(pen, 4, 21, 4, 14); g.DrawLine(pen, 4, 10, 4, 3);
                        g.DrawLine(pen, 12, 21, 12, 12); g.DrawLine(pen, 12, 8, 12, 3);
                        g.DrawLine(pen, 20, 21, 20, 16); g.DrawLine(pen, 20, 12, 20, 3);
                        g.DrawLine(pen, 1, 14, 7, 14); g.DrawLine(pen, 9, 8, 15, 8); g.DrawLine(pen, 17, 16, 23, 16);
                        break;
                    case "dots":   // "more" menu
                        using (var dot = new SolidBrush(color))
                            foreach (float x in new[] { 5f, 12f, 19f })
                                g.FillEllipse(dot, x - 1.8f, 10.2f, 3.6f, 3.6f);
                        break;
                    case "sun":
                        g.DrawEllipse(pen, 8, 8, 8, 8);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            float cx = (float)Math.Cos(a), cy = (float)Math.Sin(a);
                            g.DrawLine(pen, 12 + cx * 7, 12 + cy * 7, 12 + cx * 10, 12 + cy * 10);
                        }
                        break;
                    case "moon":
                        using (var path = new GraphicsPath())
                        {
                            // Circle (12,12) r9 minus circle (16,8) r7; they meet at 6° and 264° of the first.
                            path.AddArc(3, 3, 18, 18, 6, 258);
                            path.AddArc(9, 1, 14, 14, 225, -180);
                            path.CloseFigure();
                            g.DrawPath(pen, path);
                        }
                        break;
                    default: // terminal
                        g.DrawRectangle(pen, 3, 4, 18, 16);
                        g.DrawLines(pen, new[] { new PointF(7, 9), new PointF(10, 12), new PointF(7, 15) });
                        g.DrawLine(pen, 12, 15, 17, 15);
                        break;
                }
            }
            g.Restore(state);
        }
    }
}
