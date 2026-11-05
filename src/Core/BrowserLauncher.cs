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

        internal static string Open(string url) => Open(url, ResolveBrowser, request =>
        {
            using (var process = Process.Start(request)) { }
        });

        // Keep resolution and dispatch separate so fallback behavior can be checked without opening a browser.
        internal static string Open(string url, Func<string> resolve, Action<ProcessStartInfo> start)
        {
            Validate(url);
            Exception directError = null;
            try
            {
                var browser = resolve();
                if (!string.IsNullOrEmpty(browser))
                {
                    start(DirectRequest(browser, url));
                    return "browser";
                }
            }
            catch (Exception ex) { directError = ex; }
            try
            {
                start(new ProcessStartInfo(url) { UseShellExecute = true, Verb = "open" });
                return "shell";
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("无法自动打开浏览器，请复制链接后手动打开。",
                    directError == null ? ex : new AggregateException(directError, ex));
            }
        }
    }
}
