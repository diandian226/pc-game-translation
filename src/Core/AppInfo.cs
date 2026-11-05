using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;

namespace FMGPatcher.Core
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

        public static string CatalogPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalog.json");

        public static Catalog LoadCatalog()
        {
            if (!File.Exists(CatalogPath)) throw new PatchException("缺少 catalog.json，请重新下载工具");
            return Json.Load<Catalog>(CatalogPath);
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
        public static string ResultFile => Path.Combine(Path.GetTempPath(), "FMGPatcher-elevated-result.txt");

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
