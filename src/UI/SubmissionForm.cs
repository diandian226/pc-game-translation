using System;
using System.Drawing;
using System.Windows.Forms;
using LocaForge.Core;

namespace LocaForge.UI
{
    internal sealed class SubmissionForm : Form
    {
        readonly string url;
        readonly Label message;
        readonly FlatButton retry;

        internal SubmissionForm(string title, string url)
        {
            this.url = url;
            Text = title;
            Font = Theme.Body;
            BackColor = Theme.Page;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.Dp(640), Theme.Dp(240));
            int pad = Theme.Dp(24), width = ClientSize.Width - pad * 2;
            message = new Label { Text = "正在打开默认浏览器……", ForeColor = Theme.Text, Bounds = new Rectangle(pad, pad, width, Theme.Dp(48)) };
            var address = new TextBox { ReadOnly = true, Text = url, Font = Theme.Caption, Bounds = new Rectangle(pad, Theme.Dp(80), width, Theme.Dp(28)) };
            var note = new Label { Text = "在 GitHub 中填写后自行提交，需要登录账号。若页面没出现，可复制上方链接。", ForeColor = Theme.TextSecondary, Bounds = new Rectangle(pad, Theme.Dp(120), width, Theme.Dp(36)) };
            retry = new FlatButton("重新打开", ButtonKind.Ink, Glyph.Open) { Bounds = new Rectangle(pad, Theme.Dp(178), Theme.Dp(150), Theme.Dp(40)) };
            var copy = new FlatButton("复制链接", ButtonKind.Secondary) { Bounds = new Rectangle(pad + Theme.Dp(162), Theme.Dp(178), Theme.Dp(130), Theme.Dp(40)) };
            var close = new FlatButton("关闭", ButtonKind.Ghost) { Bounds = new Rectangle(ClientSize.Width - pad - Theme.Dp(88), Theme.Dp(178), Theme.Dp(88), Theme.Dp(40)) };
            retry.Click += (s, e) => OpenBrowser();
            copy.Click += (s, e) =>
            {
                try { Clipboard.SetText(url); message.Text = "链接已复制，请粘贴到浏览器地址栏打开。"; message.ForeColor = Theme.Success; }
                catch (Exception) { message.Text = "剪贴板暂时不可用，请选中上方链接，按 Ctrl+C 复制。"; message.ForeColor = Theme.Warn; }
            };
            close.Click += (s, e) => Close();
            Controls.AddRange(new Control[] { message, address, note, retry, copy, close });
            CancelButton = null;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Show the fallback first, then let the browser take focus; do not hide the result in a background task.
            BeginInvoke(new Action(OpenBrowser));
        }

        void OpenBrowser()
        {
            if (IsDisposed) return;
            retry.Enabled = false;
            try
            {
                BrowserLauncher.Open(url);
                message.Text = "已请求在浏览器中打开页面。若没有显示，请点“复制链接”手动打开。";
                message.ForeColor = Theme.Text;
            }
            catch (Exception ex) { message.Text = ex.Message; message.ForeColor = Theme.Warn; }
            finally { retry.Enabled = true; }
        }
    }
}
