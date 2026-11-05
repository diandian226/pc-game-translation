using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using FMGPatcher.Core;

namespace FMGPatcher.UI
{
    sealed class GameItem
    {
        public GameCandidate Game;
        public GameStatus Status;
        public Bitmap SmallIcon;
        public Bitmap LargeIcon;
    }

    /// <summary>侧栏里的游戏列表：每行 56 高，图标 32，名称 + 路径，右侧状态圆点。</summary>
    sealed class GameList : ScrollableControl
    {
        public readonly List<GameItem> Items = new List<GameItem>();
        int selected = -1, hover = -1;
        public event EventHandler SelectionChanged;
        public string EmptyText = "正在查找游戏";
        public string EmptyHint = "";

        int RowH => Theme.Dp(56);
        int Gap => Theme.Dp(4);

        public GameList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            AutoScroll = true;
            TabStop = true;
        }

        public GameItem Selected => selected >= 0 && selected < Items.Count ? Items[selected] : null;

        public void SetItems(IEnumerable<GameItem> items, string keepDir)
        {
            Items.Clear(); Items.AddRange(items);
            selected = Items.FindIndex(i => string.Equals(i.Game.Dir, keepDir, StringComparison.OrdinalIgnoreCase));
            if (selected < 0 && Items.Count > 0) selected = 0;
            AutoScrollMinSize = new Size(0, Items.Count * (RowH + Gap));
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Select(int index)
        {
            if (index < 0 || index >= Items.Count || index == selected) return;
            selected = index;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        int HitTest(Point p)
        {
            int y = p.Y - AutoScrollPosition.Y;
            int i = y / (RowH + Gap);
            return i >= 0 && i < Items.Count && y % (RowH + Gap) < RowH ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); Select(HitTest(e.Location)); base.OnMouseDown(e); }
        protected override bool IsInputKey(Keys k) => k == Keys.Up || k == Keys.Down || base.IsInputKey(k);
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up) Select(selected - 1);
            if (e.KeyCode == Keys.Down) Select(selected + 1);
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            int pad = Theme.Dp(12), icon = Theme.Dp(32);
            if (Items.Count == 0)
            {
                Theme.DrawText(g, EmptyText, Theme.Body, Theme.TextSecondary, new Rectangle(pad, Theme.Dp(8), Width - pad * 2, Theme.Dp(24)));
                TextRenderer.DrawText(g, EmptyHint, Theme.Caption, new Rectangle(pad, Theme.Dp(36), Width - pad * 2, Theme.Dp(60)), Theme.TextMuted,
                    TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.PathEllipsis);
                return;
            }
            for (int i = 0; i < Items.Count; i++)
            {
                var it = Items[i];
                var r = new Rectangle(0, AutoScrollPosition.Y + i * (RowH + Gap), Width - 1, RowH);
                if (!r.IntersectsWith(e.ClipRectangle)) continue;

                if (i == selected)
                    Theme.FillRound(g, Theme.Surface, r, Theme.Dp(10), Focused ? Theme.BorderStrong : Theme.Border);
                else if (i == hover)
                    Theme.FillRound(g, Theme.Hover, r, Theme.Dp(10));

                var ir = new Rectangle(r.X + pad, r.Y + (RowH - icon) / 2, icon, icon);
                if (it.SmallIcon != null)
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(it.SmallIcon, ir);
                }
                else
                {
                    Theme.FillRound(g, Theme.Sunk, ir, Theme.Dp(8), Theme.Border);
                    LineIcon.Draw(g, Glyph.Gamepad, Rectangle.Inflate(ir, -Theme.Dp(7), -Theme.Dp(7)), Theme.TextMuted);
                }

                int tx = ir.Right + Theme.Dp(12), dotW = Theme.Dp(24);
                int tw = r.Right - tx - dotW - Theme.Dp(4);
                // 同一个游戏可能有多份，用文件夹名区分；第二行是版本和来源
                var folder = System.IO.Path.GetFileName(it.Game.Dir);
                var meta = (it.Status?.Version ?? "版本未知") + " · " + it.Game.Source;
                Theme.DrawText(g, folder, Theme.Body, Theme.Text, new Rectangle(tx, r.Y + Theme.Dp(8), tw, Theme.Dp(22)));
                Theme.DrawText(g, meta, Theme.Caption, Theme.TextMuted, new Rectangle(tx, r.Y + Theme.Dp(28), tw, Theme.Dp(20)));

                Theme.Dot(g, StateColor(it.Status), r.Right - Theme.Dp(20), r.Y + RowH / 2, Theme.Dp(8));
            }
        }

        public static Color StateColor(GameStatus s)
        {
            if (s == null) return Theme.Idle;
            switch (s.State)
            {
                case PatchState.Patched: return Theme.Success;
                case PatchState.Interrupted:
                case PatchState.ManuallyPatched: return Theme.Warn;
                default: return s.Problem != null ? Theme.Warn : Theme.Idle;
            }
        }
    }
}
