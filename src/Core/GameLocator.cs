using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace LocaForge.Core
{
    public sealed class GameCandidate
    {
        public string Dir;
        public string Source;          // 手动选择 / Steam / itch / 运行记录 / 磁盘扫描
        public GameProfile Profile;
    }

    /// <summary>
    /// 在电脑上找游戏目录。按可靠程度依次：Steam 库 → itch 客户端 → Windows 运行记录 → 常见文件夹扫描。
    /// 每个来源都只返回"特征文件齐全"的目录。
    /// </summary>
    public static class GameLocator
    {
        public static List<GameCandidate> FindAll(Catalog catalog, bool includeDiskScan, IProgress<string> progress, CancellationToken ct)
        {
            var found = new List<GameCandidate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(GameProfile p, string dir, string source)
            {
                try { dir = Path.GetFullPath(dir).TrimEnd('\\'); } catch { return; }
                // 运行记录里可能留着已删除游戏的旧路径，删除后文件夹会落在回收站里
                if (dir.IndexOf("\\$RECYCLE.BIN\\", StringComparison.OrdinalIgnoreCase) >= 0) return;
                if (seen.Contains(dir) || !Matches(p, dir)) return;
                seen.Add(dir);
                found.Add(new GameCandidate { Dir = dir, Source = source, Profile = p });
                progress?.Report("找到：" + dir);
            }

            foreach (var p in catalog.Games)
            {
                progress?.Report("查看 Steam 游戏库");
                foreach (var d in FromSteam(p)) Add(p, d, "Steam");
                ct.ThrowIfCancellationRequested();

                progress?.Report("查看 itch 客户端");
                foreach (var d in FromItch(p)) Add(p, d, "itch");
                ct.ThrowIfCancellationRequested();

                progress?.Report("查看 Windows 运行记录");
                foreach (var d in FromRunHistory(p)) Add(p, d, "运行记录");
                ct.ThrowIfCancellationRequested();

                if (includeDiskScan)
                    foreach (var d in FromDiskScan(p, progress, ct)) Add(p, d, "磁盘扫描");
            }
            return found;
        }

        /// <summary>用户拖进来或手动选的路径：可以是 exe，也可以是游戏目录或其中的 game 子目录。</summary>
        public static GameCandidate FromUserPath(Catalog catalog, string path)
        {
            string dir = File.Exists(path) ? Path.GetDirectoryName(path) : path;
            for (int up = 0; up < 2 && dir != null; up++)
            {
                foreach (var p in catalog.Games)
                    if (Matches(p, dir))
                        return new GameCandidate { Dir = Path.GetFullPath(dir).TrimEnd('\\'), Source = "手动选择", Profile = p };
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        public static bool Matches(GameProfile p, string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return false;
                if (ExeFinder.Find(p, dir).Count == 0) return false;
                foreach (var m in p.Markers)
                {
                    var full = Path.Combine(dir, m.Replace('/', '\\'));
                    if (!File.Exists(full) && !Directory.Exists(full)) return false;
                }
                return true;
            }
            catch { return false; }
        }

        // ---------- Steam ----------
        static IEnumerable<string> FromSteam(GameProfile p)
        {
            var results = new List<string>();
            try
            {
                var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                         ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
                if (string.IsNullOrEmpty(steam)) return results;
                steam = steam.Replace('/', '\\');

                var libraries = new List<string> { steam };
                var vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));

                foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var apps = Path.Combine(lib, "steamapps");
                    if (!string.IsNullOrEmpty(p.SteamAppId))
                    {
                        var acf = Path.Combine(apps, "appmanifest_" + p.SteamAppId + ".acf");
                        if (File.Exists(acf))
                        {
                            var m = Regex.Match(File.ReadAllText(acf), "\"installdir\"\\s+\"([^\"]+)\"");
                            if (m.Success) results.Add(Path.Combine(apps, "common", m.Groups[1].Value));
                        }
                    }
                    var common = Path.Combine(apps, "common");
                    if (Directory.Exists(common))
                        results.AddRange(SafeSubdirs(common));
                }
            }
            catch { }
            return results;
        }

        // ---------- itch ----------
        static IEnumerable<string> FromItch(GameProfile p)
        {
            var results = new List<string>();
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"itch\apps");
            if (!Directory.Exists(root)) return results;
            foreach (var app in SafeSubdirs(root))
            {
                results.Add(app);
                results.AddRange(SafeSubdirs(app)); // 有的游戏 zip 里还套了一层
            }
            return results;
        }

        // ---------- Windows 运行记录 ----------
        /// <summary>Windows 会把运行过的 exe 路径记在这两个注册表位置，能找到解压在任意位置、玩过的游戏。</summary>
        static IEnumerable<string> FromRunHistory(GameProfile p)
        {
            var results = new List<string>();
            string[] keys =
            {
                @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store",
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache",
            };
            foreach (var k in keys)
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(k))
                    {
                        if (key == null) continue;
                        // 值名形如 C:\路径\Game.exe 或 C:\路径\Game.exe.FriendlyAppName
                        foreach (var name in key.GetValueNames())
                        {
                            int i = name.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                            if (i <= 0) continue;
                            var exePath = name.Substring(0, i + 4);
                            if (ExeFinder.IsMatch(p, Path.GetFileName(exePath)))
                                results.Add(Path.GetDirectoryName(exePath));
                        }
                    }
                }
                catch { }
            }
            return results;
        }

        // ---------- 磁盘扫描 ----------
        static readonly string[] SkipDirs =
        {
            "Windows", "Program Files", "Program Files (x86)", "ProgramData", "$Recycle.Bin", "System Volume Information",
            "AppData", "node_modules", ".git", "Recovery", "PerfLogs", "MSOCache", "Intel", "AMD", "NVIDIA", "$RECYCLE.BIN",
        };

        static IEnumerable<string> FromDiskScan(GameProfile p, IProgress<string> progress, CancellationToken ct)
        {
            var results = new List<string>();
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var userRoots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Path.Combine(home, "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(home, "Games"),
            };
            foreach (var r in userRoots.Where(Directory.Exists))
            {
                progress?.Report("扫描 " + r);
                Walk(p, r, 4, results, ct);
            }
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                progress?.Report("扫描 " + drive.Name);
                Walk(p, drive.RootDirectory.FullName, 3, results, ct);
            }
            return results;
        }

        static void Walk(GameProfile p, string dir, int depth, List<string> results, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (ExeFinder.Find(p, dir).Count > 0) { results.Add(dir); return; }
            if (depth <= 0) return;
            foreach (var sub in SafeSubdirs(dir))
            {
                var name = Path.GetFileName(sub);
                if (SkipDirs.Contains(name, StringComparer.OrdinalIgnoreCase) || name.StartsWith(".")) continue;
                // 名字像游戏的文件夹多给两层深度
                bool hinted = p.SearchFolderNames.Any(n => name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
                Walk(p, sub, hinted ? Math.Max(depth - 1, 2) : depth - 1, results, ct);
            }
        }

        static IEnumerable<string> SafeSubdirs(string dir)
        {
            try
            {
                return new DirectoryInfo(dir).EnumerateDirectories()
                    .Where(d => (d.Attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) == 0)
                    .Select(d => d.FullName).ToList();
            }
            catch { return Enumerable.Empty<string>(); }
        }
    }
}
