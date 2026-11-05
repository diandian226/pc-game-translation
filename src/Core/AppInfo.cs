using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;

namespace LocaForge.Core
{
    public static class AppInfo
    {
        public static string Version
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }

        public static System.Version ParsedVersion => Assembly.GetExecutingAssembly().GetName().Version;

        public static string ExeDir => AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        public static string ExePath => Process.GetCurrentProcess().MainModule.FileName;
        public static string CatalogPath => Path.Combine(ExeDir, "catalog.json");

        /// <summary>联网下载的补丁清单和补丁包放在这里（工具所在目录可能没有写权限）。</summary>
        public static string CacheDir => Environment.GetEnvironmentVariable("LOCAFORGE_CACHE_DIR")   // 测试用
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocaForge", "updates");

        /// <summary>改名前（FMGPatcher 0.2.x）的下载缓存，只读不写；新缓存没有更新的清单时沿用它。</summary>
        static string LegacyCacheDir => Environment.GetEnvironmentVariable("LOCAFORGE_CACHE_DIR") != null ? null
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FMG" + "Patcher", "updates");

        /// <summary>内置清单和下载缓存里的清单，取 revision 大的那份。</summary>
        public static Catalog LoadCatalog()
        {
            Catalog best = null;
            if (File.Exists(CatalogPath)) { best = Json.Load<Catalog>(CatalogPath); best.Bind(ExeDir); }
            foreach (var dir in new[] { CacheDir, LegacyCacheDir })
            {
                if (dir == null) continue;
                try
                {
                    var path = Path.Combine(dir, "catalog.json");
                    if (!File.Exists(path)) continue;
                    var c = Json.Load<Catalog>(path);
                    c.Bind(dir);
                    if (best == null || c.Revision > best.Revision) best = c;
                }
                catch { } // 缓存损坏就忽略，下次更新会重写
            }
            if (best == null) throw new PatchException("缺少 catalog.json，请重新下载工具");
            return best;
        }

        public static bool VersionLess(string a, string b)
        {
            System.Version va, vb;
            return System.Version.TryParse(a, out va) && System.Version.TryParse(b, out vb) && va < vb;
        }

        public static bool IsAdmin()
        {
            using (var id = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// 游戏装在 Program Files 之类的受保护目录时，以管理员身份再启动自己一次，只做这一个操作。
    /// 子进程把结果写到临时文件，父进程读取后刷新界面。
    /// </summary>
    public static class Elevation
    {
        public static string ResultFile => Path.Combine(Path.GetTempPath(), "LocaForge-elevated-result.txt");

        /// <returns>null 表示成功，否则为错误信息</returns>
        public static string RunElevated(string verb, string gameDir)
        {
            try { File.Delete(ResultFile); } catch { }
            var psi = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,
                "--" + verb + " \"" + gameDir.TrimEnd('\\') + "\" --elevated")
            {
                Verb = "runas",
                UseShellExecute = true,
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                    if (p.ExitCode == 0) return null;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return "已取消管理员授权";
            }
            try { return File.ReadAllText(ResultFile); } catch { return "管理员模式下操作失败"; }
        }
    }
}
