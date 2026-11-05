using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LocaForge.Core;

namespace LocaForge.UI
{
    /// <summary>
    /// 左侧：电脑上找到的游戏。右侧：选中游戏的卡片（游戏图标 + 版本签条）和安装操作。
    /// 布局全部手动计算，所有间距都在 4px 网格上。
    /// </summary>
    sealed class MainForm : Form
    {
        Catalog catalog;
        CancellationTokenSource scanCts;
        bool busy;
        readonly Dictionary<string, string> payloadInfo = new Dictionary<string, string>();

        // 侧栏
        readonly Panel side = new Panel();
        readonly GameList list = new GameList();
        readonly FlatButton btnScan = new FlatButton("搜索电脑", ButtonKind.Secondary, Glyph.Search);
        readonly FlatButton btnBrowse = new FlatButton("手动选择游戏", ButtonKind.Ghost, Glyph.Folder) { AlignLeft = true };
        Bitmap appIcon;

        // 主区
        readonly Panel main = new Panel();
        readonly TextView title = new TextView("安装汉化补丁", () => Theme.Title, Theme.Text);
        readonly FlatButton btnHelp = new FlatButton("使用说明", ButtonKind.Tint, Glyph.Help) { Radius = 16 };
        readonly FlatButton btnCheckUpdate = new FlatButton("检查更新", ButtonKind.Ghost, Glyph.Refresh);

        // 更新提示卡片：只在有更新、或手动检查后出现
        UpdateInfo pendingUpdate;
        bool checkingUpdate;
        readonly Card updateCard = new Card { Visible = false };
        readonly StatusLine updateLine = new StatusLine();
        readonly TextView updateSub = new TextView("", () => Theme.Caption, Theme.TextMuted);
        readonly FlatButton btnApplyUpdate = new FlatButton("立即更新", ButtonKind.Ink, Glyph.Install);
        readonly FlatButton btnLater = new FlatButton("稍后", ButtonKind.Ghost);
        readonly ProgressLine updateProgress = new ProgressLine { Visible = false };

        readonly Card emptyCard = new Card();
        readonly GameIconBox emptyIcon = new GameIconBox();
        readonly TextView emptyHeading = new TextView("还没有选中游戏", () => Theme.Heading, Theme.Text);
        readonly TextView emptyBody = new TextView("在左侧选一个游戏，或把游戏文件夹、FMGWorld.exe 拖进这个窗口", () => Theme.Body, Theme.TextSecondary) { Wrap = true };
        readonly FlatButton emptyScan = new FlatButton("搜索电脑", ButtonKind.Ink, Glyph.Search);
        readonly FlatButton emptyBrowse = new FlatButton("手动选择", ButtonKind.Secondary, Glyph.Folder);

        readonly Card gameCard = new Card();
        readonly GameIconBox gameIcon = new GameIconBox();
        readonly TextView gameName = new TextView("", () => Theme.Heading, Theme.Text);
        readonly TextView gamePath = new TextView("", () => Theme.Caption, Theme.TextMuted) { PathEllipsis = true };
        readonly FlatButton btnOpen = new FlatButton("打开目录", ButtonKind.Ghost, Glyph.Open);
        readonly Slip slip = new Slip();

        readonly Card actionCard = new Card();
        readonly FlatButton btnInstall = new FlatButton("安装中文补丁", ButtonKind.Ink, Glyph.Install);
        readonly FlatButton btnRestore = new FlatButton("还原原版", ButtonKind.Secondary, Glyph.Restore);
        readonly Card statusPanel = new Card { Radius = 10, Stroke = null };
        readonly StatusLine status = new StatusLine();
        readonly ProgressLine progress = new ProgressLine();
        readonly TextView detail = new TextView("", () => Theme.Caption, Theme.TextMuted);
        readonly TextView footnote = new TextView(
            "安装前会把被覆盖的原文件备份到游戏目录的 _zh_patch 文件夹，还原时原样放回。补丁不修改游戏程序，也不动存档。",
            () => Theme.Caption, Theme.TextMuted) { Wrap = true };

        public MainForm()
        {
            using (var g = CreateGraphics()) Theme.Init(g.DpiX / 96f);
            AutoScaleMode = AutoScaleMode.None;
            Text = "LocaForge 汉化工坊";
            Font = Theme.Body;
            BackColor = Theme.Page;
            ClientSize = new Size(Theme.Dp(1000), Theme.Dp(760));
            MinimumSize = SizeFromClientSize(new Size(Theme.Dp(840), Theme.Dp(640)));
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            appIcon = ExeIcon.Load(Application.ExecutablePath, Theme.Dp(32));

            BuildSidebar();
            BuildMain();
            EnableDrop(this);
            ShowGame(null);
        }

        // ================= 构建 =================
        void BuildSidebar()
        {
            side.Dock = DockStyle.Left;
            side.Width = Theme.Dp(280);
            side.BackColor = Theme.Sunk;
            side.Paint += PaintSidebar;
            side.Resize += (s, e) => LayoutSidebar();

            list.BackColor = Theme.Sunk;
            list.SelectionChanged += (s, e) => ShowGame(list.Selected);
            btnScan.Click += (s, e) => ToggleScan();
            btnBrowse.Click += (s, e) => Browse();

            side.Controls.AddRange(new Control[] { list, btnScan, btnBrowse });
            Controls.Add(side);
        }

        void PaintSidebar(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int pad = Theme.Dp(20);
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, side.Width - 1, 0, side.Width - 1, side.Height);

            int iconSize = Theme.Dp(32);
            if (appIcon != null)
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(appIcon, new Rectangle(pad, Theme.Dp(24), iconSize, iconSize));
            }
            int tx = pad + iconSize + Theme.Dp(12);
            Theme.DrawText(g, "LocaForge", Theme.Heading, Theme.Text, new Rectangle(tx, Theme.Dp(20), side.Width - tx, Theme.Dp(24)));
            Theme.DrawText(g, "汉化工坊 · v" + AppInfo.Version, Theme.Caption, Theme.TextMuted, new Rectangle(tx, Theme.Dp(42), side.Width - tx, Theme.Dp(18)));

            int ly = Theme.Dp(88);
            Theme.DrawText(g, "电脑上的游戏", Theme.Caption, Theme.TextMuted, new Rectangle(pad, ly, Theme.Dp(160), Theme.Dp(20)));
            if (list.Items.Count > 0)
                Theme.DrawText(g, list.Items.Count.ToString(), Theme.MonoCaption, Theme.TextMuted,
                    new Rectangle(side.Width - pad - Theme.Dp(40), ly, Theme.Dp(40), Theme.Dp(20)), TextFormatFlags.Right);
        }

        void LayoutSidebar()
        {
            int pad = Theme.Dp(16), w = side.Width - pad * 2 - 1;
            int bottom = side.Height - Theme.Dp(20);
            btnBrowse.Bounds = new Rectangle(pad, bottom - Theme.Dp(36), w, Theme.Dp(36));
            btnScan.Bounds = new Rectangle(pad, btnBrowse.Top - Theme.Dp(8) - Theme.Dp(40), w, Theme.Dp(40));
            int top = Theme.Dp(116);
            list.Bounds = new Rectangle(pad - Theme.Dp(4), top, w + Theme.Dp(8), btnScan.Top - Theme.Dp(16) - top);
        }

        void BuildMain()
        {
            main.Dock = DockStyle.Fill;
            main.BackColor = Theme.Page;
            main.AutoScroll = true;
            main.Resize += (s, e) => LayoutMain();

            btnHelp.Click += (s, e) => ShowHelp();
            btnCheckUpdate.Click += async (s, e) => await CheckUpdates(silent: false);
            btnApplyUpdate.Click += async (s, e) => await ApplyUpdate();
            btnLater.Click += (s, e) => { updateCard.Visible = false; LayoutMain(); };
            updateCard.Controls.AddRange(new Control[] { updateLine, updateSub, btnApplyUpdate, btnLater, updateProgress });

            emptyScan.Click += (s, e) => ToggleScan();
            emptyBrowse.Click += (s, e) => Browse();
            emptyCard.Controls.AddRange(new Control[] { emptyIcon, emptyHeading, emptyBody, emptyScan, emptyBrowse });

            btnOpen.Click += (s, e) => OpenFolder(list.Selected?.Game.Dir);
            gameCard.Controls.AddRange(new Control[] { gameIcon, gameName, gamePath, btnOpen, slip });

            btnInstall.Click += async (s, e) => await RunInstall();
            btnRestore.Click += async (s, e) => await RunRestore();
            statusPanel.SetFill(Theme.Sunk);
            statusPanel.Controls.AddRange(new Control[] { status, progress, detail });
            actionCard.Controls.AddRange(new Control[] { btnInstall, btnRestore, statusPanel });

            main.Controls.AddRange(new Control[] { title, btnCheckUpdate, btnHelp, updateCard, emptyCard, gameCard, actionCard, footnote });
            Controls.Add(main);
            main.BringToFront();
        }

        void LayoutMain()
        {
            if (main.ClientSize.Width <= 0) return;
            int pad = Theme.Dp(32), cp = Theme.Dp(24);
            int w = Math.Min(main.ClientSize.Width - pad * 2, Theme.Dp(760));
            var origin = main.AutoScrollPosition;
            int x = pad + origin.X, y = Theme.Dp(28) + origin.Y;

            int hw = btnHelp.PreferredWidth(), uw = btnCheckUpdate.PreferredWidth();
            btnHelp.Bounds = new Rectangle(x + w - hw, y + Theme.Dp(2), hw, Theme.Dp(32));
            btnCheckUpdate.Bounds = new Rectangle(btnHelp.Left - Theme.Dp(8) - uw, y + Theme.Dp(2), uw, Theme.Dp(32));
            title.Bounds = new Rectangle(x, y, btnCheckUpdate.Left - Theme.Dp(16) - x, Theme.Dp(36));
            y += Theme.Dp(36) + Theme.Dp(24);

            // 更新提示：固定 96 高，下载时进度条出现在底部，不会挤动下面的卡片
            if (updateCard.Visible)
            {
                int up = Theme.Dp(20), bh = Theme.Dp(40);
                int lw = btnLater.Visible ? Math.Max(Theme.Dp(72), btnLater.PreferredWidth()) : 0;
                int aw = btnApplyUpdate.Visible ? Math.Max(Theme.Dp(120), btnApplyUpdate.PreferredWidth()) : 0;
                int by = (Theme.Dp(96) - bh) / 2;
                btnLater.Bounds = new Rectangle(w - up - lw, by, lw, bh);
                btnApplyUpdate.Bounds = new Rectangle((lw > 0 ? btnLater.Left - Theme.Dp(8) : w - up) - aw, by, aw, bh);
                int tr = (aw > 0 ? btnApplyUpdate.Left : lw > 0 ? btnLater.Left : w - up) - Theme.Dp(16);
                updateLine.Bounds = new Rectangle(up, up, tr - up, Theme.Dp(24));
                updateSub.Bounds = new Rectangle(up + Theme.Dp(16), up + Theme.Dp(28), tr - up - Theme.Dp(16), Theme.Dp(20));
                updateProgress.Bounds = new Rectangle(up, Theme.Dp(80), w - up * 2, Theme.Dp(4));
                updateCard.Bounds = new Rectangle(x, y, w, Theme.Dp(96));
                y += updateCard.Height + Theme.Dp(16);
            }

            // 空状态
            int iw = w - cp * 2;
            emptyIcon.Bounds = new Rectangle(cp, cp + Theme.Dp(8), Theme.Dp(64), Theme.Dp(64));
            emptyHeading.Bounds = new Rectangle(cp, emptyIcon.Bottom + Theme.Dp(20), iw, Theme.Dp(24));
            emptyBody.Bounds = new Rectangle(cp, emptyHeading.Bottom + Theme.Dp(8), iw, Theme.Dp(24));
            int bw1 = Math.Max(Theme.Dp(140), emptyScan.PreferredWidth());
            emptyScan.Bounds = new Rectangle(cp, emptyBody.Bottom + Theme.Dp(24), bw1, Theme.Dp(44));
            emptyBrowse.Bounds = new Rectangle(emptyScan.Right + Theme.Dp(12), emptyScan.Top, Math.Max(Theme.Dp(120), emptyBrowse.PreferredWidth()), Theme.Dp(44));
            emptyCard.Bounds = new Rectangle(x, y, w, emptyScan.Bottom + cp + Theme.Dp(8));

            // 游戏卡片
            gameIcon.Bounds = new Rectangle(cp, cp, Theme.Dp(64), Theme.Dp(64));
            int ow = btnOpen.PreferredWidth();
            btnOpen.Bounds = new Rectangle(w - cp - ow, cp + Theme.Dp(16), ow, Theme.Dp(32));
            int nx = gameIcon.Right + Theme.Dp(16), nw = btnOpen.Left - Theme.Dp(12) - nx;
            gameName.Bounds = new Rectangle(nx, cp + Theme.Dp(8), nw, Theme.Dp(24));
            gamePath.Bounds = new Rectangle(nx, cp + Theme.Dp(36), nw, Theme.Dp(20));
            slip.Bounds = new Rectangle(cp, gameIcon.Bottom + Theme.Dp(20), iw, slip.Height);
            gameCard.Bounds = new Rectangle(x, y, w, slip.Bottom + cp);

            // 操作卡片
            int ay = (gameCard.Visible ? gameCard.Bottom : emptyCard.Bottom) + Theme.Dp(16);
            int rw = Theme.Dp(152);
            btnRestore.Bounds = new Rectangle(w - cp - rw, cp, rw, Theme.Dp(48));
            btnInstall.Bounds = new Rectangle(cp, cp, btnRestore.Left - Theme.Dp(12) - cp, Theme.Dp(48));
            int sp = Theme.Dp(16);
            status.Bounds = new Rectangle(sp, sp, iw - sp * 2, Theme.Dp(24));
            progress.Bounds = new Rectangle(sp, status.Bottom + Theme.Dp(12), iw - sp * 2, Theme.Dp(4));
            detail.Bounds = new Rectangle(sp, progress.Bottom + Theme.Dp(12), iw - sp * 2, Theme.Dp(20));
            statusPanel.Bounds = new Rectangle(cp, btnInstall.Bottom + Theme.Dp(16), iw, detail.Bottom + sp);
            actionCard.Bounds = new Rectangle(x, ay, w, statusPanel.Bottom + cp);

            footnote.Bounds = new Rectangle(x + Theme.Dp(4), actionCard.Bottom + Theme.Dp(16), w - Theme.Dp(8), Theme.Dp(40));
            main.AutoScrollMinSize = new Size(0, footnote.Bottom - origin.Y + pad);
        }

        // ================= 显示 =================
        void ShowGame(GameItem item)
        {
            bool has = item != null;
            emptyCard.Visible = !has;
            gameCard.Visible = has;
            actionCard.Visible = has;
            footnote.Visible = has;
            if (has)
            {
                var s = item.Status;
                var g = item.Game;
                gameIcon.Image = item.LargeIcon;
                gameName.Text = g.Profile.Name + (s?.Version != null ? " " + s.Version : "");
                gamePath.Text = g.Dir;

                var patch = s?.MatchedPatch ?? g.Profile.Patches.LastOrDefault();
                var save = GameInspector.SaveDirectory(g);
                slip.SetRows(
                    new Slip.Row { Key = "补丁适配", Value = patch != null ? g.Profile.Name + " " + patch.GameVersion : "无" },
                    new Slip.Row { Key = "本机版本", Value = s?.Version ?? "无法识别" },
                    new Slip.Row { Key = "本机状态", Value = StateText(s), Dot = GameList.StateColor(s) },
                    new Slip.Row { Key = "汉化补丁", Value = patch != null ? PayloadInfo(patch) : "暂无" },
                    new Slip.Row { Key = "存档位置", Value = save == null ? "未知" : Directory.Exists(save) ? ShortenAppData(save) : "还没有存档" });

                if (!busy) ShowIdleStatus(s);
            }
            UpdateButtons();
            LayoutMain();
            side.Invalidate();
        }

        /// <summary>交给系统外壳打开文件夹（复用已运行的资源管理器），不自己启动 explorer.exe。</summary>
        void OpenFolder(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                using (Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true, Verb = "open" })) { }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开文件夹：" + ex.Message + "\n\n" + dir, "打开目录", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        static string ShortenAppData(string path)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return path.StartsWith(appData, StringComparison.OrdinalIgnoreCase) ? "%APPDATA%" + path.Substring(appData.Length) : path;
        }

        static string StateText(GameStatus s)
        {
            if (s == null) return "检查中";
            switch (s.State)
            {
                case PatchState.Patched:
                    return s.PatchOutdated
                        ? "已汉化 · 补丁 " + s.Record.PatchVersion + "，可更新到 " + s.MatchedPatch.PatchVersion
                        : "已汉化 · 补丁 " + s.Record.PatchVersion;
                case PatchState.Interrupted: return "上次安装没有完成";
                case PatchState.ManuallyPatched: return "已手动复制过汉化文件";
                default: return s.Problem ?? "英文原版，未汉化";
            }
        }

        void ShowIdleStatus(GameStatus s)
        {
            progress.Visible = false;
            if (s == null) { status.Set(Theme.Idle, "正在检查游戏"); detail.Text = ""; return; }
            switch (s.State)
            {
                case PatchState.Patched:
                    if (s.PatchOutdated) status.Set(Theme.Accent, "汉化补丁有新版本 " + s.MatchedPatch.PatchVersion + "，点“更新汉化补丁”即可，存档不受影响");
                    else status.Set(Theme.Success, "中文补丁已安装，直接启动游戏即可");
                    detail.Text = "安装于 " + s.Record.InstalledAt + " · " + s.Record.Files.Count + " 个文件";
                    break;
                case PatchState.Interrupted:
                    status.Set(Theme.Warn, "上次安装中断，建议先还原原版再重新安装");
                    detail.Text = "";
                    break;
                case PatchState.ManuallyPatched:
                    status.Set(Theme.Warn, "检测到手动复制的汉化文件，可用本工具重新安装以便日后还原");
                    detail.Text = "";
                    break;
                default:
                    if (s.Problem != null) status.Set(Theme.Warn, s.Problem);
                    else status.Set(Theme.Idle, "准备就绪，安装大约需要几秒");
                    detail.Text = "";
                    break;
            }
        }

        string PayloadInfo(PatchPackage p)
        {
            var key = p.Id + "|" + p.PatchVersion + "|" + p.BaseDir;
            if (payloadInfo.TryGetValue(key, out var cached)) return cached;
            int n = PatchEngine.PayloadFileCount(p);
            long size = PatchEngine.PayloadSize(p);
            var text = n < 0 ? p.PatchVersion + " · 补丁包缺失" : p.PatchVersion + " · " + n + " 个文件 · " + FileOps.FormatSize(size);
            payloadInfo[key] = text;
            return text;
        }

        void UpdateButtons()
        {
            var s = list.Selected?.Status;
            bool scanning = scanCts != null;
            btnScan.Text = scanning ? "停止搜索" : "搜索电脑";
            emptyScan.Text = btnScan.Text;
            btnBrowse.Enabled = emptyBrowse.Enabled = !busy && !scanning;
            list.Enabled = !busy;

            btnInstall.Enabled = !busy && s != null && s.CanInstall;
            btnInstall.Text = s == null ? "安装中文补丁"
                : s.PatchOutdated ? "更新汉化补丁"
                : s.State == PatchState.Patched || s.State == PatchState.Interrupted ? "重新安装补丁"
                : "安装中文补丁";
            btnApplyUpdate.Enabled = !busy;
            btnCheckUpdate.Enabled = !busy && !checkingUpdate;
            btnRestore.Enabled = !busy && s != null && s.Record != null;
        }

        // ================= 查找游戏 =================
        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LayoutSidebar();
            try { catalog = AppInfo.LoadCatalog(); }
            catch (Exception ex)
            {
                status.Set(Theme.Warn, ex.Message);
                MessageBox.Show(this, ex.Message, "无法启动", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            await Scan(includeDisk: false);
            await CheckUpdates(silent: true);
        }

        void ToggleScan()
        {
            if (scanCts != null) { scanCts.Cancel(); return; }
            _ = Scan(includeDisk: true);
        }

        async Task Scan(bool includeDisk)
        {
            if (catalog == null || scanCts != null) return;
            scanCts = new CancellationTokenSource();
            var ct = scanCts.Token;
            list.EmptyText = "正在查找游戏";
            list.Invalidate();
            UpdateButtons();
            var keep = list.Selected?.Game.Dir;
            var hint = new Progress<string>(m => { if (list.Items.Count == 0 || includeDisk) list.EmptyHint = m; list.Invalidate(); });
            List<GameItem> items;
            try
            {
                items = await Task.Run(() =>
                {
                    var found = new List<GameCandidate>();
                    foreach (var dir in KnownDirs.Load())
                    {
                        var c = GameLocator.FromUserPath(catalog, dir);
                        if (c != null) { c.Source = "手动选择"; found.Add(c); }
                    }
                    try { found.AddRange(GameLocator.FindAll(catalog, includeDisk, hint, ct)); }
                    catch (OperationCanceledException) { }
                    return found
                        .GroupBy(c => c.Dir, StringComparer.OrdinalIgnoreCase).Select(gr => gr.First())
                        .Select(MakeItem).ToList();
                });
            }
            finally
            {
                scanCts.Dispose();
                scanCts = null;
            }
            // 保留列表里已有、这次没扫到的（例如手动添加的）
            foreach (var old in list.Items)
                if (!items.Any(i => string.Equals(i.Game.Dir, old.Game.Dir, StringComparison.OrdinalIgnoreCase)) && Directory.Exists(old.Game.Dir))
                    items.Add(old);
            list.EmptyText = "还没找到游戏";
            list.EmptyHint = "点下方“搜索电脑”，或把游戏文件夹拖进窗口";
            list.SetItems(items, keep);
            if (includeDisk && items.Count == 0)
                status.Set(Theme.Idle, "没有在常见位置找到游戏，请手动选择");
            UpdateButtons();
        }

        static GameItem MakeItem(GameCandidate c)
        {
            var exe = c.Profile.ExeNames.Select(n => Path.Combine(c.Dir, n)).FirstOrDefault(File.Exists);
            return new GameItem
            {
                Game = c,
                Status = GameInspector.Inspect(c),
                SmallIcon = exe != null ? ExeIcon.Load(exe, Theme.Dp(32)) : null,
                LargeIcon = exe != null ? ExeIcon.Load(exe, Theme.Dp(64)) : null,
            };
        }

        void Browse()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "选择游戏程序",
                Filter = "游戏程序 (*.exe)|*.exe",
                CheckFileExists = true,
            })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK) AddUserPath(dlg.FileName);
            }
        }

        internal bool Scanning => scanCts != null;
        internal bool CheckingUpdate => checkingUpdate;

        internal void AddUserPath(string path, bool remember = true)
        {
            if (catalog == null || busy) return;
            var c = GameLocator.FromUserPath(catalog, path);
            if (c == null)
            {
                MessageBox.Show(this, "这个位置不是支持的游戏。\n请选择 FMGWorld.exe，或包含它的游戏文件夹。\n\n" + path,
                    "不是支持的游戏", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (remember) KnownDirs.Add(c.Dir);
            var items = list.Items.Where(i => !string.Equals(i.Game.Dir, c.Dir, StringComparison.OrdinalIgnoreCase)).ToList();
            items.Insert(0, MakeItem(c));
            list.SetItems(items, c.Dir);
        }

        // ================= 安装 / 还原 =================
        async Task RunInstall()
        {
            var item = list.Selected;
            if (item?.Status == null) return;
            await RunOperation(item, "install", "正在安装中文补丁", p => PatchEngine.Install(item.Status, p, CancellationToken.None),
                "安装完成，直接启动游戏即可");
        }

        async Task RunRestore()
        {
            var item = list.Selected;
            if (item?.Status?.Record == null) return;
            var ok = MessageBox.Show(this, "把游戏还原为安装补丁前的状态？\n存档不受影响。", "还原原版",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (ok != DialogResult.OK) return;
            await RunOperation(item, "uninstall", "正在还原原版", p => PatchEngine.Uninstall(item.Game, p), "已还原为原版");
        }

        async Task RunOperation(GameItem item, string verb, string busyText, Action<IProgress<PatchProgress>> work, string doneText)
        {
            busy = true;
            UpdateButtons();
            status.Set(Theme.Accent, busyText);
            progress.Value = -1;
            progress.Visible = true;
            detail.Text = "";
            var sw = Stopwatch.StartNew();
            var reporter = new Progress<PatchProgress>(p =>
            {
                if (p.Percent >= 0) progress.Value = p.Percent;
                detail.Text = p.Message;
            });

            string error = null;
            try { await Task.Run(() => work(reporter)); }
            catch (UnauthorizedAccessException)
            {
                var ask = MessageBox.Show(this, "游戏所在的文件夹需要管理员权限才能写入。\n以管理员身份继续？",
                    "需要管理员权限", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (ask == DialogResult.OK)
                {
                    detail.Text = "等待管理员授权";
                    error = await Task.Run(() => Elevation.RunElevated(verb, item.Game.Dir));
                }
                else error = "已取消：没有写入权限";
            }
            catch (PatchException ex) { error = ex.Message; }
            catch (IOException) { error = "有文件正被其他程序占用，已自动还原。请关闭游戏后重试"; }
            catch (Exception ex) { error = ex.Message; }

            var elapsed = sw.Elapsed.TotalSeconds;
            item.Status = await Task.Run(() => GameInspector.Inspect(item.Game));
            busy = false;
            ShowGame(item);
            list.Invalidate();
            progress.Visible = false;
            if (error == null)
            {
                status.Set(Theme.Success, doneText);
                detail.Text = "用时 " + elapsed.ToString("0.0") + " 秒" + (item.Status.Record != null ? " · " + item.Status.Record.Files.Count + " 个文件" : "");
            }
            else
            {
                status.Set(Theme.Warn, error);
                detail.Text = "";
            }
        }

        // ================= 更新 =================
        async Task CheckUpdates(bool silent)
        {
            if (catalog == null || checkingUpdate || busy) return;
            checkingUpdate = true;
            btnCheckUpdate.Text = "正在检查";
            UpdateButtons();
            try
            {
                var info = await UpdateService.CheckAsync(catalog, CancellationToken.None);
                if (info.Any || info.CatalogNeedsTool)
                {
                    pendingUpdate = info;
                    var parts = new List<string>();
                    if (info.ToolUpdate) parts.Add("工具 v" + AppInfo.Version + " → v" + info.Manifest.Tool.Version);
                    if (info.CatalogUpdate || info.CatalogNeedsTool) parts.AddRange(info.PatchChanges);
                    string head = info.ToolUpdate && (info.CatalogUpdate || info.CatalogNeedsTool) ? "工具和汉化补丁都有新版本"
                        : info.ToolUpdate ? "新版工具 v" + info.Manifest.Tool.Version + " 可用"
                        : "汉化补丁有更新";
                    btnApplyUpdate.Text = "立即更新";
                    ShowUpdateCard(Theme.Accent, head, string.Join(" · ", parts), apply: true, later: "稍后");
                }
                else if (!silent)
                    ShowUpdateCard(Theme.Success, "已是最新版本", "工具 v" + AppInfo.Version + " · 补丁清单第 " + catalog.Revision + " 版", apply: false, later: "好");
            }
            catch (Exception ex)
            {
                if (!silent) ShowUpdateCard(Theme.Warn, "检查更新失败", FriendlyNetError(ex), apply: false, later: "好");
            }
            finally
            {
                checkingUpdate = false;
                btnCheckUpdate.Text = "检查更新";
                UpdateButtons();
            }
        }

        void ShowUpdateCard(Color dot, string head, string sub, bool apply, string later)
        {
            updateLine.Set(dot, head);
            updateSub.Text = sub;
            btnApplyUpdate.Visible = apply;
            btnLater.Visible = later != null;
            btnLater.Text = later ?? "";
            updateCard.Visible = true;
            LayoutMain();
        }

        async Task ApplyUpdate()
        {
            var info = pendingUpdate;
            if (info == null || busy) return;
            busy = true;
            UpdateButtons();
            btnLater.Visible = false;
            btnApplyUpdate.Visible = false;
            updateLine.Set(Theme.Accent, "正在更新");
            updateProgress.Value = -1;
            updateProgress.Visible = true;
            LayoutMain();
            var reporter = new Progress<PatchProgress>(p =>
            {
                if (p.Percent >= 0) updateProgress.Value = p.Percent;
                updateSub.Text = p.Message;
            });
            try
            {
                if (info.CatalogUpdate)
                {
                    await UpdateService.ApplyCatalogAsync(info, reporter, CancellationToken.None);
                    await ReloadCatalog();
                }
                if (info.ToolUpdate)
                {
                    string exe;
                    try { exe = await UpdateService.ApplyToolAsync(info, reporter, CancellationToken.None); }
                    catch (UnauthorizedAccessException)
                    {
                        pendingUpdate = null;
                        ShowUpdateCard(Theme.Warn, "工具所在的文件夹不能写入",
                            "已打开发布页，请手动下载新版；或把工具移到桌面等位置后再更新", apply: false, later: "好");
                        OpenUrl(info.Manifest.Page ?? UpdateService.ReleasesPage);
                        return;
                    }
                    updateLine.Set(Theme.Success, "新版工具已下载，正在重新打开");
                    using (Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) })) { }
                    Application.Exit();
                    return;
                }
                pendingUpdate = null;
                bool outdated = list.Items.Any(i => i.Status?.PatchOutdated == true);
                ShowUpdateCard(Theme.Success, "汉化补丁已更新到补丁清单第 " + catalog.Revision + " 版",
                    outdated ? "已装过旧补丁的游戏，选中后点“更新汉化补丁”即可" : "选中游戏后即可安装新补丁", apply: false, later: "好");
            }
            catch (Exception ex)
            {
                btnApplyUpdate.Text = "重试";
                ShowUpdateCard(Theme.Warn, "更新失败", FriendlyNetError(ex), apply: true, later: "稍后");
            }
            finally
            {
                busy = false;
                updateProgress.Visible = false;
                UpdateButtons();
                ShowGame(list.Selected);
            }
        }

        /// <summary>换用新的补丁清单后，按新清单重新识别列表里的每个游戏。</summary>
        async Task ReloadCatalog()
        {
            catalog = AppInfo.LoadCatalog();
            payloadInfo.Clear();
            var keep = list.Selected?.Game.Dir;
            var olds = list.Items.ToList();
            var cat = catalog;
            var items = await Task.Run(() => olds.Select(old =>
            {
                var c = GameLocator.FromUserPath(cat, old.Game.Dir);
                if (c == null) return null;
                c.Source = old.Game.Source;
                return MakeItem(c);
            }).Where(i => i != null).ToList());
            list.SetItems(items, keep);
        }

        static string FriendlyNetError(Exception ex)
        {
            if (ex is PatchException) return ex.Message;
            for (var e = ex; e != null; e = e.InnerException)
                if (e is System.Net.Http.HttpRequestException || e is System.Net.WebException || e is TaskCanceledException)
                    return "连不上 GitHub，请检查网络或代理后重试";
            return ex.Message;
        }

        static void OpenUrl(string url)
        {
            try { using (Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { } } catch { }
        }

        // ================= 其他 =================
        void ShowHelp()
        {
            MessageBox.Show(this,
                "1. 准备 FMGWorld 0.2.1 英文原版（建议从 itch.io 官方页面获取）\n" +
                "2. 在左侧选中游戏；找不到时点“搜索电脑”，或把游戏文件夹拖进窗口\n" +
                "3. 本机版本和补丁适配一致后，点“安装中文补丁”\n" +
                "4. 想恢复英文原版时，点“还原原版”\n\n" +
                "游戏装在 Program Files 等系统目录时，会请求一次管理员权限。\n" +
                "工具启动时会自动检查新版补丁和新版工具，也可以点右上角“检查更新”。\n" +
                "安装前请先关闭游戏。",
                "使用说明", MessageBoxButtons.OK, MessageBoxIcon.None);
        }

        void EnableDrop(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) && !busy ? DragDropEffects.Link : DragDropEffects.None;
            c.DragDrop += (s, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) AddUserPath(files[0]);
            };
            foreach (Control child in c.Controls) EnableDrop(child);
        }
    }

    /// <summary>单行或折行文字，背景透明。</summary>
    sealed class TextView : PaintedControl
    {
        readonly Func<Font> font;
        public Color Color;
        public bool Wrap, PathEllipsis;

        public TextView(string text, Func<Font> font, Color color)
        {
            Text = text; this.font = font; Color = color;
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var flags = TextFormatFlags.NoPrefix;
            if (Wrap) flags |= TextFormatFlags.WordBreak;
            else flags |= TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | (PathEllipsis ? TextFormatFlags.PathEllipsis : TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, Text, font(), ClientRectangle, Color, flags);
        }
    }

    /// <summary>状态行：圆点 + 文字。</summary>
    sealed class StatusLine : PaintedControl
    {
        Color dot = Theme.Idle;
        public void Set(Color dotColor, string text) { dot = dotColor; Text = text; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            int d = Theme.Dp(8);
            Theme.Dot(e.Graphics, dot, d / 2, Height / 2, d);
            Theme.DrawText(e.Graphics, Text, Theme.Body, Theme.Text, new Rectangle(Theme.Dp(16), 0, Width - Theme.Dp(16), Height));
        }
    }

    /// <summary>记住用户手动选过的游戏目录。</summary>
    static class KnownDirs
    {
        static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LocaForge", "games.txt");
        /// <summary>改名前（FMGPatcher）的记录，新记录不存在时读它。</summary>
        static string LegacyPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FMG" + "Patcher", "games.txt");

        public static List<string> Load()
        {
            try
            {
                var f = File.Exists(FilePath) ? FilePath : LegacyPath;
                return File.Exists(f) ? File.ReadAllLines(f).Where(l => l.Trim().Length > 0).ToList() : new List<string>();
            }
            catch { return new List<string>(); }
        }

        public static void Add(string dir)
        {
            try
            {
                var all = Load();
                all.RemoveAll(d => string.Equals(d, dir, StringComparison.OrdinalIgnoreCase));
                all.Insert(0, dir);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, all.Take(20));
            }
            catch { }
        }
    }
}
