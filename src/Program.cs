using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using FMGPatcher.Core;
using FMGPatcher.UI;

namespace FMGPatcher
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            UpdateService.CleanupOldExe();
            if (args.Length > 0 && args[0] == "--render") return Render(args);
            if (args.Length > 0) return RunCommandLine(args);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
                MessageBox.Show(e.Exception.Message, "出错了", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Run(new MainForm());
            return 0;
        }

        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        /// <summary>开发用：把窗口画成 PNG 检查界面。FMGPatcher.exe --render out.png ["游戏目录"]</summary>
        static int Render(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-5000, -5000), ShowInTaskbar = false };
            form.Show();
            void Pump(int ms) { var end = DateTime.Now.AddMilliseconds(ms); while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(10); } }
            Pump(300);
            var until = DateTime.Now.AddSeconds(20);
            while (form.Scanning && DateTime.Now < until) Pump(50);
            Pump(300);
            while (form.CheckingUpdate && DateTime.Now < until) Pump(50);
            if (args.Length > 2) form.AddUserPath(args[2], remember: false);
            Pump(300);
            using (var bmp = new System.Drawing.Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
            }
            form.Close();
            return 0;
        }

        /// <summary>
        /// 命令行模式，供管理员提权和自动化测试使用：
        ///   FMGPatcher.exe --scan
        ///   FMGPatcher.exe --status    "游戏目录"
        ///   FMGPatcher.exe --install   "游戏目录"
        ///   FMGPatcher.exe --uninstall "游戏目录"
        ///   FMGPatcher.exe --check-update      只检查
        ///   FMGPatcher.exe --update            下载并应用补丁更新和工具更新（不重启）
        /// </summary>
        static int RunCommandLine(string[] args)
        {
            bool elevated = args.Contains("--elevated");
            if (!elevated)
            {
                AttachConsole(-1);
                try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
            }
            Action<PatchProgress> progress = p => { if (!elevated) Console.WriteLine(p.Message); };
            try
            {
                var catalog = AppInfo.LoadCatalog();
                var cmd = args[0];
                if (cmd == "--scan")
                {
                    foreach (var g in GameLocator.FindAll(catalog, true, null, CancellationToken.None))
                        Console.WriteLine(g.Source + "\t" + g.Dir);
                    return 0;
                }
                if (cmd == "--check-update" || cmd == "--update")
                {
                    var info = UpdateService.CheckAsync(catalog, CancellationToken.None).GetAwaiter().GetResult();
                    Console.WriteLine("当前工具	" + AppInfo.Version + "	清单 revision " + catalog.Revision + "（" + catalog.BaseDir + "）");
                    Console.WriteLine("最新工具	" + (info.Manifest.Tool?.Version ?? "无") + (info.ToolUpdate ? "	可更新" : ""));
                    Console.WriteLine("最新清单	" + (info.Manifest.Catalog?.Revision.ToString() ?? "无") +
                        (info.CatalogUpdate ? "	可更新" : info.CatalogNeedsTool ? "	需先更新工具" : ""));
                    foreach (var c in info.PatchChanges) Console.WriteLine("  " + c);
                    if (cmd == "--update")
                    {
                        var pr = new SyncProgress(progress);
                        if (info.CatalogUpdate) UpdateService.ApplyCatalogAsync(info, pr, CancellationToken.None).GetAwaiter().GetResult();
                        if (info.ToolUpdate) Console.WriteLine("新工具：" + UpdateService.ApplyToolAsync(info, pr, CancellationToken.None).GetAwaiter().GetResult());
                    }
                    return 0;
                }
                if (args.Length < 2) throw new PatchException("缺少游戏目录参数");
                var game = GameLocator.FromUserPath(catalog, args[1]) ?? throw new PatchException("这个目录不是支持的游戏：" + args[1]);
                var status = GameInspector.Inspect(game);
                switch (cmd)
                {
                    case "--status":
                        Console.WriteLine("版本\t" + (status.Version ?? "未知"));
                        Console.WriteLine("状态\t" + status.State);
                        Console.WriteLine("补丁\t" + (status.MatchedPatch?.Id ?? "无") + " " + (status.MatchedPatch?.PatchVersion ?? ""));
                        if (status.PatchOutdated) Console.WriteLine("可更新\t" + status.Record.PatchVersion + " -> " + status.MatchedPatch.PatchVersion);
                        if (status.Problem != null) Console.WriteLine("问题\t" + status.Problem);
                        return 0;
                    case "--install":
                        PatchEngine.Install(status, new SyncProgress(progress), CancellationToken.None);
                        return 0;
                    case "--uninstall":
                        PatchEngine.Uninstall(game, new SyncProgress(progress));
                        return 0;
                    default:
                        throw new PatchException("未知命令 " + cmd);
                }
            }
            catch (Exception e)
            {
                var msg = e is UnauthorizedAccessException ? "没有写入权限：" + e.Message : e.Message;
                if (elevated) { try { File.WriteAllText(Elevation.ResultFile, msg); } catch { } }
                else Console.Error.WriteLine("失败：" + msg);
                return 1;
            }
        }

        /// <summary>命令行下按顺序立即输出（Progress&lt;T&gt; 在无界面时会乱序投递到线程池）。</summary>
        sealed class SyncProgress : IProgress<PatchProgress>
        {
            readonly Action<PatchProgress> action;
            public SyncProgress(Action<PatchProgress> action) { this.action = action; }
            public void Report(PatchProgress value) => action(value);
        }
    }
}
