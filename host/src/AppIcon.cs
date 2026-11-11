using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Vivarium
{
    internal static class AppIcon
    {
        private static Icon _shared;

        public static Icon Shared
        {
            get
            {
                if (_shared == null) _shared = Build(64);
                return _shared;
            }
        }

        public static Icon Build(int size)
        {
            using (Bitmap bmp = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);

                    RectangleF body = new RectangleF(size * 0.06f, size * 0.06f, size * 0.88f, size * 0.88f);
                    float radius = size * 0.26f;
                    using (GraphicsPath path = RoundedRect(body, radius))
                    using (LinearGradientBrush brush = new LinearGradientBrush(body, Color.FromArgb(110, 231, 183), Color.FromArgb(13, 92, 99), 55f))
                    {
                        g.FillPath(brush, path);
                    }

                    /* soft highlight */
                    using (GraphicsPath path = RoundedRect(body, radius))
                    using (LinearGradientBrush gloss = new LinearGradientBrush(
                        new RectangleF(body.X, body.Y, body.Width, body.Height * 0.55f),
                        Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    {
                        g.FillPath(gloss, path);
                    }

                    /* leaf / sprout mark */
                    float cx = size * 0.5f;
                    float cy = size * 0.56f;
                    using (Pen stem = new Pen(Color.FromArgb(235, 6, 42, 34), size * 0.055f))
                    {
                        stem.StartCap = LineCap.Round;
                        stem.EndCap = LineCap.Round;
                        g.DrawLine(stem, cx, size * 0.80f, cx, size * 0.46f);
                    }
                    using (SolidBrush leaf = new SolidBrush(Color.FromArgb(240, 236, 255, 236)))
                    {
                        g.FillEllipse(leaf, cx - size * 0.205f, cy - size * 0.26f, size * 0.235f, size * 0.30f);
                        g.FillEllipse(leaf, cx - size * 0.03f, cy - size * 0.38f, size * 0.235f, size * 0.30f);
                    }
                }

                IntPtr handle = bmp.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(handle))
                    {
                        return (Icon)temp.Clone();
                    }
                }
                finally
                {
                    Native.DestroyIcon(handle);
                }
            }
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
