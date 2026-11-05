using System.Drawing;
using System.Drawing.Drawing2D;

namespace FMGPatcher.UI
{
    public enum Glyph { None, Search, Folder, Help, Chevron, Gamepad, Install, Restore, Open }

    /// <summary>界面里仅有的几个线条图标，统一 1.5 笔画、圆头，与安卓版 LineIcon 同一画法。</summary>
    static class LineIcon
    {
        public static void Draw(Graphics g, Glyph kind, Rectangle box, Color color)
        {
            if (kind == Glyph.None) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float x = box.X, y = box.Y, w = box.Width, h = box.Height;
            using (var pen = new Pen(color, 1.5f * Theme.Scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                PointF P(float fx, float fy) => new PointF(x + w * fx, y + h * fy);
                switch (kind)
                {
                    case Glyph.Search:
                        g.DrawEllipse(pen, x + w * 0.16f, y + h * 0.16f, w * 0.52f, h * 0.52f);
                        g.DrawLine(pen, P(0.6f, 0.6f), P(0.84f, 0.84f));
                        break;
                    case Glyph.Folder:
                        g.DrawLines(pen, new[] { P(0.12f, 0.3f), P(0.12f, 0.8f), P(0.88f, 0.8f), P(0.88f, 0.36f), P(0.5f, 0.36f), P(0.42f, 0.22f), P(0.12f, 0.22f), P(0.12f, 0.3f) });
                        break;
                    case Glyph.Open:
                        g.DrawLines(pen, new[] { P(0.56f, 0.18f), P(0.82f, 0.18f), P(0.82f, 0.44f) });
                        g.DrawLine(pen, P(0.82f, 0.18f), P(0.48f, 0.52f));
                        g.DrawLines(pen, new[] { P(0.7f, 0.6f), P(0.7f, 0.82f), P(0.18f, 0.82f), P(0.18f, 0.3f), P(0.4f, 0.3f) });
                        break;
                    case Glyph.Help:
                        g.DrawEllipse(pen, x + w * 0.1f, y + h * 0.1f, w * 0.8f, h * 0.8f);
                        g.DrawArc(pen, x + w * 0.38f, y + h * 0.26f, w * 0.24f, h * 0.24f, 180, 270);
                        g.DrawLine(pen, P(0.5f, 0.5f), P(0.5f, 0.58f));
                        using (var b = new SolidBrush(color)) g.FillEllipse(b, x + w * 0.5f - pen.Width * 0.7f, y + h * 0.72f - pen.Width * 0.7f, pen.Width * 1.4f, pen.Width * 1.4f);
                        break;
                    case Glyph.Chevron:
                        g.DrawLines(pen, new[] { P(0.375f, 0.25f), P(0.625f, 0.5f), P(0.375f, 0.75f) });
                        break;
                    case Glyph.Gamepad:
                        using (var path = Theme.Round(new RectangleF(x + w * 0.1f, y + h * 0.28f, w * 0.8f, h * 0.46f), h * 0.2f)) g.DrawPath(pen, path);
                        g.DrawLine(pen, P(0.3f, 0.42f), P(0.3f, 0.6f));
                        g.DrawLine(pen, P(0.21f, 0.51f), P(0.39f, 0.51f));
                        using (var b = new SolidBrush(color))
                        {
                            float r = pen.Width * 0.8f;
                            g.FillEllipse(b, x + w * 0.64f - r, y + h * 0.46f - r, r * 2, r * 2);
                            g.FillEllipse(b, x + w * 0.74f - r, y + h * 0.56f - r, r * 2, r * 2);
                        }
                        break;
                    case Glyph.Install:
                        g.DrawLine(pen, P(0.5f, 0.14f), P(0.5f, 0.6f));
                        g.DrawLines(pen, new[] { P(0.3f, 0.42f), P(0.5f, 0.62f), P(0.7f, 0.42f) });
                        g.DrawLines(pen, new[] { P(0.16f, 0.66f), P(0.16f, 0.84f), P(0.84f, 0.84f), P(0.84f, 0.66f) });
                        break;
                    case Glyph.Restore:
                        g.DrawArc(pen, x + w * 0.18f, y + h * 0.18f, w * 0.64f, h * 0.64f, 200, 290);
                        g.DrawLines(pen, new[] { P(0.16f, 0.2f), P(0.2f, 0.4f), P(0.4f, 0.36f) });
                        break;
                }
            }
        }
    }
}
