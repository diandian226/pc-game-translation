using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace LocaForge.UI
{
    /// <summary>
    /// 与安卓汉化工具同一套颜色：暖灰中性色阶 + 朱红强调色 + 墨色主按钮。
    /// 方向：稳妥、清楚、带点温度；世界是纸与朱印；签名细节是"版本签条"。
    /// </summary>
    static class Theme
    {
        // 中性色阶（与 MainActivity.java 一致）
        public static readonly Color Page = Hex(0xF6F6F4);
        public static readonly Color Surface = Color.White;
        public static readonly Color Sunk = Hex(0xF1F0EE);
        public static readonly Color Pressed = Hex(0xE5E4E1);
        public static readonly Color Hover = Hex(0xEDECE9);
        public static readonly Color Border = Hex(0xE6E5E2);
        public static readonly Color BorderStrong = Hex(0xD3D1CD);
        public static readonly Color Text = Hex(0x1C1B19);
        public static readonly Color TextSecondary = Hex(0x5C5852);
        public static readonly Color TextMuted = Hex(0x8C8780);

        public static readonly Color Ink = Hex(0x1C1B19);
        public static readonly Color InkHover = Hex(0x33312E);
        public static readonly Color InkPressed = Hex(0x45433F);

        public static readonly Color Accent = Hex(0xB8321F);
        public static readonly Color AccentTint = Hex(0xFBEDEA);
        public static readonly Color AccentTintPressed = Hex(0xF3D5CF);
        public static readonly Color AccentInk = Hex(0x9A2817);

        public static readonly Color Success = Hex(0x3D7A4E);
        public static readonly Color Warn = Hex(0x9A6514);
        public static readonly Color Idle = Hex(0xC4C1BC);

        static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);

        // ---------- 尺寸 ----------
        public static float Scale = 1f;
        public static int Dp(float v) => (int)Math.Round(v * Scale);

        // ---------- 字体 ----------
        static readonly string UiFamily = Pick("Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI");
        static readonly string MonoFamily = Pick("Cascadia Mono", "Consolas", "Courier New");

        static string Pick(params string[] names)
        {
            using (var fonts = new InstalledFontCollection())
            {
                var have = fonts.Families.Select(f => f.Name).ToList();
                return names.FirstOrDefault(n => have.Contains(n)) ?? "Segoe UI";
            }
        }

        static Font Ui(float px) => new Font(UiFamily, px * Scale, FontStyle.Regular, GraphicsUnit.Pixel);
        static Font Mono(float px) => new Font(MonoFamily, px * Scale, FontStyle.Regular, GraphicsUnit.Pixel);

        public static Font Title, Heading, Body, BodySmall, Caption, MonoBody, MonoCaption, Button;

        public static void Init(float scale)
        {
            Scale = scale;
            Title = Ui(22);
            Heading = Ui(16);
            Body = Ui(14);
            BodySmall = Ui(13);
            Caption = Ui(12);
            Button = Ui(14);
            MonoBody = Mono(13);
            MonoCaption = Mono(12);
        }

        /// <summary>纯 ASCII（版本号、大小）用等宽字；含中文的值用界面字体，避免等宽字回退成宋体。</summary>
        public static Font ValueFont(string text, bool small = false) =>
            text != null && text.All(c => c < 128) ? (small ? MonoCaption : MonoBody) : (small ? Caption : Body);

        // ---------- 绘制 ----------
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color fill, Rectangle r, int radius, Color? border = null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1f, r.Height - 1f);
            using (var path = Round(rf, radius))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border.HasValue)
                    using (var pen = new Pen(border.Value, 1f)) g.DrawPath(pen, path);
            }
        }

        public static void Dot(Graphics g, Color c, int cx, int cy, int size)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - size / 2f, cy - size / 2f, size, size);
        }

        public static void DrawText(Graphics g, string text, Font font, Color color, Rectangle bounds,
            TextFormatFlags extra = TextFormatFlags.Left)
        {
            TextRenderer.DrawText(g, text, font, bounds, color,
                TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | extra);
        }

        public static Size Measure(string text, Font font) =>
            TextRenderer.MeasureText(text ?? "", font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }
}
