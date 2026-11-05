using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LocaForge.Core
{
    internal static class BrowserLauncher
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, EntryPoint = "AssocQueryStringW")]
        static extern int AssocQueryString(uint flags, uint str, string assoc, string extra, StringBuilder output, ref uint length);

        internal static string ResolveBrowser()
        {
            uint length = 0;
            AssocQueryString(0x1000, 2, "https", "open", null, ref length);
            if (length < 2 || length > 32768) return null;
            var output = new StringBuilder((int)length);
            if (AssocQueryString(0x1000, 2, "https", "open", output, ref length) != 0) return null;
            var path = output.ToString();
            if (!Path.IsPathRooted(path) || !File.Exists(path)) return null;
            switch (Path.GetFileName(path).ToLowerInvariant())
            {
                case "msedge.exe": case "chrome.exe": case "firefox.exe": case "brave.exe":
                case "vivaldi.exe": case "opera.exe": case "360chrome.exe": case "360se.exe":
                case "qqbrowser.exe": case "sogouexplorer.exe": return path;
                default: return null; // Packaged applications and delegate handlers use the shell fallback.
            }
        }

        internal static ProcessStartInfo DirectRequest(string executable, string url)
        {
            Validate(url);
            return new ProcessStartInfo(executable, "\"" + url + "\"")
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable)
            };
        }

        static void Validate(string url)
        {
            Uri uri;
            if (string.IsNullOrWhiteSpace(url) || url.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0 ||
                !Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("提交链接无效。", nameof(url));
        }

        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
        const int ASFW_ANY = -1;

        internal static string Open(string url) => Open(url, ResolveBrowser, request =>
        {
            using (var process = Process.Start(request)) { }
        });

        /// <summary>
        /// 打开网页并让浏览器到前台。
        /// 浏览器通常已经在运行，新页面会交给已有的浏览器进程；Windows 默认不允许它抢前台，
        /// 结果是页面开在后面、用户以为没反应。所以先 AllowSetForegroundWindow 授权，再交给系统外壳打开，
        /// 系统外壳不可用时（如关联损坏）再直接启动默认浏览器程序。
        /// </summary>
        internal static string Open(string url, Func<string> resolve, Action<ProcessStartInfo> start)
        {
            Validate(url);
            try { AllowSetForegroundWindow(ASFW_ANY); } catch { }
            Exception shellError = null;
            try
            {
                start(new ProcessStartInfo(url) { UseShellExecute = true, Verb = "open" });
                return "shell";
            }
            catch (Exception ex) { shellError = ex; }
            try
            {
                var browser = resolve();
                if (!string.IsNullOrEmpty(browser))
                {
                    start(DirectRequest(browser, url));
                    return "browser";
                }
            }
            catch (Exception ex) { shellError = new AggregateException(shellError, ex); }
            throw new InvalidOperationException("无法自动打开浏览器，请复制链接后手动打开。", shellError);
        }
    }
}
