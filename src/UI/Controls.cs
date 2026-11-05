using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LocaForge.UI
{
    /// <summary>所有自绘控件的基类：双缓冲，背景取父控件颜色，圆角外的地方不露底。</summary>
    abstract class PaintedControl : Control
    {
        protected PaintedControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        protected Color ParentColor => Parent?.BackColor ?? Theme.Page;

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(ParentColor);
    }

    /// <summary>白色卡片：圆角 + 发丝线边框。子控件的背景色自动继承卡片色。</summary>
    sealed class Card : Panel
    {
        public int Radius = 16;
        public Color Fill = Theme.Surface;
        public Color? Stroke = Theme.Border;

        public Card()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Fill;
        }

        public void SetFill(Color c) { Fill = c; BackColor = c; Invalidate(); }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Page);
            Theme.FillRound(e.Graphics, Fill, ClientRectangle, Theme.Dp(Radius), Stroke);
        }
    }

    enum ButtonKind { Ink, Secondary, Ghost, Tint }

    sealed class FlatButton : PaintedControl
    {
        public ButtonKind Kind;
        public Glyph Glyph;
        public int Radius = 10;
        public bool AlignLeft;
        bool hover, down;

        public FlatButton(string text, ButtonKind kind, Glyph glyph = Glyph.None)
        {
            Text = text; Kind = kind; Glyph = glyph;
            Font = Theme.Button;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        public int PreferredWidth()
        {
            int w = Theme.Measure(Text, Font).Width + Theme.Dp(AlignLeft ? 24 : 40);
            if (Glyph != Glyph.None) w += Theme.Dp(24);
            return w;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override bool IsInputKey(Keys keyData) => keyData == Keys.Space || keyData == Keys.Enter || base.IsInputKey(keyData);
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if ((e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) && Enabled) OnClick(EventArgs.Empty);
            base.OnKeyUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color fill, text, border = Color.Empty;
            switch (Kind)
            {
                case ButtonKind.Ink:
                    fill = !Enabled ? Theme.Pressed : down ? Theme.InkPressed : hover ? Theme.InkHover : Theme.Ink;
                    text = Enabled ? Color.White : Theme.TextMuted;
                    break;
                case ButtonKind.Secondary:
                    fill = down ? Theme.Pressed : hover && Enabled ? Theme.Hover : Theme.Surface;
                    text = Enabled ? Theme.Text : Theme.TextMuted;
                    border = Enabled ? Theme.BorderStrong : Theme.Border;
                    break;
                case ButtonKind.Tint:
                    fill = down ? Theme.AccentTintPressed : Theme.AccentTint;
                    text = Theme.AccentInk;
                    break;
                default:
                    fill = down ? Theme.Pressed : hover && Enabled ? Theme.Hover : ParentColor;
                    text = Enabled ? Theme.TextSecondary : Theme.Idle;
                    break;
            }

            var r = ClientRectangle;
            Theme.FillRound(g, fill, r, Theme.Dp(Radius), border == Color.Empty ? (Color?)null : border);

            int icon = Theme.Dp(16), gap = Theme.Dp(8);
            var size = Theme.Measure(Text, Font);
            int content = size.Width + (Glyph != Glyph.None ? icon + gap : 0);
            int x = AlignLeft ? Theme.Dp(12) : (r.Width - content) / 2;
            if (Glyph != Glyph.None)
            {
                LineIcon.Draw(g, Glyph, new Rectangle(x, (r.Height - icon) / 2, icon, icon), text);
                x += icon + gap;
            }
            Theme.DrawText(g, Text, Font, text, new Rectangle(x, 0, r.Width - x - Theme.Dp(8), r.Height));

            if (Focused && ShowFocusCues)
            {
                var fr = Rectangle.Inflate(r, -1, -1);
                using (var path = Theme.Round(fr, Theme.Dp(Radius)))
                using (var pen = new Pen(Theme.Accent, 2f)) g.DrawPath(pen, path);
            }
        }
    }

    /// <summary>
    /// 版本签条：本工具的"签名细节"。键在左（灰），值在右（版本号用等宽字），行间发丝线。
    /// </summary>
    sealed class Slip : PaintedControl
    {
        public sealed class Row { public string Key, Value; public Color? Dot; }
        public readonly List<Row> Rows = new List<Row>();
        public int RowHeight => Theme.Dp(40);
        public int KeyWidth => Theme.Dp(88);

        public void SetRows(params Row[] rows)
        {
            Rows.Clear(); Rows.AddRange(rows);
            Height = RowHeight * Rows.Count + 2;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.FillRound(g, Theme.Surface, ClientRectangle, Theme.Dp(10), Theme.BorderStrong);
            int pad = Theme.Dp(16);
            for (int i = 0; i < Rows.Count; i++)
            {
                var row = Rows[i];
                int y = 1 + i * RowHeight;
                if (i > 0)
                    using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, pad, y, Width - pad, y);
                Theme.DrawText(g, row.Key, Theme.BodySmall, Theme.TextMuted, new Rectangle(pad, y, KeyWidth, RowHeight));
                int x = pad + KeyWidth;
                if (row.Dot.HasValue)
                {
                    Theme.Dot(g, row.Dot.Value, x + Theme.Dp(4), y + RowHeight / 2, Theme.Dp(8));
                    x += Theme.Dp(16);
                }
                Theme.DrawText(g, row.Value, Theme.ValueFont(row.Value), Theme.Text,
                    new Rectangle(x, y, Width - x - pad, RowHeight), TextFormatFlags.PathEllipsis);
            }
        }
    }

    /// <summary>细进度条：4px，朱红在浅灰轨道上。Value = -1 时为不确定进度（只在真正处理中才动）。</summary>
    sealed class ProgressLine : PaintedControl
    {
        int value;
        float phase;
        readonly Timer timer = new Timer { Interval = 16 };

        public ProgressLine()
        {
            Height = Theme.Dp(4);
            timer.Tick += (s, e) => { phase = (phase + 0.012f) % 1.4f; Invalidate(); };
        }

        public int Value
        {
            get => value;
            set
            {
                this.value = value;
                timer.Enabled = value < 0 && Visible;
                Invalidate();
            }
        }

        protected override void OnVisibleChanged(EventArgs e) { timer.Enabled = value < 0 && Visible; base.OnVisibleChanged(e); }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int r = Height / 2;
            Theme.FillRound(g, Theme.BorderStrong, ClientRectangle, r);
            Rectangle bar;
            if (value < 0)
            {
                int w = Width * 3 / 10;
                int x = (int)((phase - 0.4f) * Width);
                bar = Rectangle.Intersect(new Rectangle(x, 0, w, Height), ClientRectangle);
            }
            else bar = new Rectangle(0, 0, Width * Math.Min(100, value) / 100, Height);
            if (bar.Width > 0) Theme.FillRound(g, Theme.Accent, bar, r);
        }
    }

    /// <summary>游戏自己的图标：整个界面里唯一有丰富颜色的元素。拿不到时显示手柄线条图标。</summary>
    sealed class GameIconBox : PaintedControl
    {
        Bitmap image;
        public Bitmap Image { get => image; set { image = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var r = ClientRectangle;
            int radius = Theme.Dp(14);
            if (image != null)
            {
                using (var path = Theme.Round(new RectangleF(0, 0, r.Width, r.Height), radius))
                {
                    g.SetClip(path);
                    g.DrawImage(image, r);
                    g.ResetClip();
                    using (var pen = new Pen(Color.FromArgb(20, Theme.Text))) g.DrawPath(pen, path);
                }
            }
            else
            {
                Theme.FillRound(g, Theme.Sunk, r, radius, Theme.Border);
                int s = r.Width / 2;
                LineIcon.Draw(g, Glyph.Gamepad, new Rectangle((r.Width - s) / 2, (r.Height - s) / 2, s, s), Theme.TextMuted);
            }
        }
    }

    static class ExeIcon
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, int[] ids, uint count, uint flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        /// <summary>按指定尺寸取 exe 图标（ExtractAssociatedIcon 只有 32px，放大会糊）。</summary>
        public static Bitmap Load(string exe, int size)
        {
            try
            {
                var handles = new IntPtr[1];
                if (PrivateExtractIcons(exe, 0, size, size, handles, new int[1], 1, 0) == 0 || handles[0] == IntPtr.Zero) return null;
                try
                {
                    using (var icon = System.Drawing.Icon.FromHandle(handles[0])) return icon.ToBitmap();
                }
                finally { DestroyIcon(handles[0]); }
            }
            catch { return null; }
        }
    }
}
