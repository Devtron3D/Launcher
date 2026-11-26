using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LauncherApp
{
    // Main layout, bottom: a coloured dot and what is happening (or the last log line), how many programs are
    // open, and [Stop all] [Close launched programs]. Clicking the text opens the whole log.
    class MainStatusBar : Panel
    {
        public readonly Button StopAllButton = new Button { Text = Theme.Pick("Stop all", "[STOP ALL]"), AutoSize = true };
        public readonly Button CloseAllButton = new Button { Text = Theme.Pick("Close launched programs", "[CLOSE ALL]"), AutoSize = true };
        public event EventHandler OpenFullLog;

        public string Message = "";
        public Color DotColor;
        public string Counts = "";
        public bool CountsWarn;

        public MainStatusBar()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            Height = Theme.S(48);
            BackColor = Theme.Surface;
            DotColor = Theme.Disabled;
            foreach (var b in new[] { StopAllButton, CloseAllButton })
            {
                b.MinimumSize = new Size(0, Theme.S(32));
                b.Padding = new Padding(Theme.S(8), 0, Theme.S(8), 0);
                b.SizeChanged += (s, e) => LayoutButtons();
                Controls.Add(b);
            }
            Cursor = Cursors.Hand;
        }

        int TextRight { get { return StopAllButton.Left - Theme.S(12); } }

        void LayoutButtons()
        {
            int pad = Theme.S(16), gap = Theme.S(8);
            CloseAllButton.Location = new Point(Width - pad - CloseAllButton.Width, (Height - CloseAllButton.Height) / 2);
            StopAllButton.Location = new Point(CloseAllButton.Left - gap - StopAllButton.Width, (Height - StopAllButton.Height) / 2);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutButtons();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.X < TextRight && OpenFullLog != null) OpenFullLog(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, 0, 0, Width, 0);

            int x = Theme.S(20), dot = Theme.S(9);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(DotColor)) g.FillEllipse(b, x, (Height - dot) / 2, dot, dot);
            g.SmoothingMode = SmoothingMode.None;
            x += dot + Theme.S(10);

            const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            int countsW = string.IsNullOrEmpty(Counts) ? 0
                : TextRenderer.MeasureText(g, Counts, Theme.Small, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + Theme.S(2);
            int right = TextRight;
            if (countsW > 0)
            {
                TextRenderer.DrawText(g, Counts, Theme.Small, new Rectangle(right - countsW, 0, countsW, Height),
                    CountsWarn ? Theme.Warning : Theme.Muted, flags);
                right -= countsW + Theme.S(14);
            }
            if (right > x)
                TextRenderer.DrawText(g, Message, Theme.Ui, new Rectangle(x, 0, right - x, Height), Theme.Text, flags);
        }
    }
}
