using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        /// <summary>
        /// 下载缓存的候选位置，按优先级：%LOCALAPPDATA% → 工具所在文件夹 → 临时目录。
        /// 某些环境（受限账户、沙盒里启动的程序）不允许写 %LOCALAPPDATA%，这时自动换下一个。
        /// </summary>
        static IEnumerable<string> CacheCandidates()
        {
            var env = Environment.GetEnvironmentVariable("LOCAFORGE_CACHE_DIR");   // 测试用
            if (env != null) { yield return env; yield break; }
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocaForge", "updates");
            yield return Path.Combine(ExeDir, "updates");
            yield return Path.Combine(Path.GetTempPath(), "LocaForge", "updates");
        }

        static string cacheDir;

        /// <summary>联网下载的补丁清单和补丁包放在这里：第一个能写入的候选位置。</summary>
        public static string CacheDir
        {
            get
            {
                if (cacheDir != null) return cacheDir;
                foreach (var dir in CacheCandidates())
                    if (CanWrite(dir)) return cacheDir = dir;
                throw new PatchException("找不到可以保存更新的文件夹：%LOCALAPPDATA%、工具所在文件夹和临时目录都没有写入权限");
            }
        }

        static bool CanWrite(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                var probe = Path.Combine(dir, ".write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        /// <summary>改名前（FMGPatcher 0.2.x）的下载缓存，只读不写；新缓存没有更新的清单时沿用它。</summary>
        static string LegacyCacheDir => Environment.GetEnvironmentVariable("LOCAFORGE_CACHE_DIR") != null ? null
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FMG" + "Patcher", "updates");

        /// <summary>内置清单和所有候选缓存位置里的清单，取 revision 最大的那份（只读，不要求可写）。</summary>
        public static Catalog LoadCatalog()
        {
            Catalog best = null;
            if (File.Exists(CatalogPath)) { best = Json.Load<Catalog>(CatalogPath); best.Bind(ExeDir); }
            foreach (var dir in CacheCandidates().Concat(new[] { LegacyCacheDir }))
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
