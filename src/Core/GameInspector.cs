using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LocaForge.Core
{
    public enum PatchState { NotPatched, Patched, ManuallyPatched, Interrupted }

    public sealed class GameStatus
    {
        public GameCandidate Game;
        public string Version;              // 读不到时为 null
        public PatchPackage MatchedPatch;    // 与版本对应的补丁；没有则为 null
        public PatchState State;
        public InstallRecord Record;
        public string Problem;               // 版本校验失败等原因，给界面直接展示
        public bool CanInstall => MatchedPatch != null && Problem == null;
        /// <summary>已用本工具装过补丁，但补丁清单里有更新的版本。</summary>
        public bool PatchOutdated => State == PatchState.Patched && MatchedPatch != null && Record != null &&
                                     Record.PatchVersion != MatchedPatch.PatchVersion;
    }

    /// <summary>读取游戏版本、比对补丁、读取安装记录。</summary>
    public static class GameInspector
    {
        public static GameStatus Inspect(GameCandidate game)
        {
            var p = game.Profile;
            var s = new GameStatus { Game = game };
            s.Version = DetectVersion(p, game.Dir, out var versionProblem);
            s.Record = PatchEngine.ReadRecord(game.Dir);

            if (s.Record != null)
                s.State = s.Record.State == "installed" ? PatchState.Patched : PatchState.Interrupted;
            else if (p.PatchedMarkers.Count > 0 && p.PatchedMarkers.All(m => File.Exists(Path.Combine(game.Dir, m.Replace('/', '\\')))))
                s.State = PatchState.ManuallyPatched;
            else
                s.State = PatchState.NotPatched;

            if (versionProblem != null || s.Version == null)
            {
                s.Problem = versionProblem ?? "读不到游戏版本号";
                return s;
            }
            s.MatchedPatch = p.Patches.FirstOrDefault(x => x.GameVersion == s.Version);
            if (s.MatchedPatch == null)
            {
                s.Problem = "暂无适配 " + s.Version + " 的补丁";
                return s;
            }
            foreach (var f in s.MatchedPatch.RequiredFiles)
            {
                var full = Path.Combine(game.Dir, f.Path.Replace('/', '\\'));
                if (!File.Exists(full)) { s.Problem = "缺少原版文件 " + f.Path; break; }
                if (f.Size > 0 && new FileInfo(full).Length != f.Size) { s.Problem = "原版文件不一致 " + f.Path; break; }
                if (!string.IsNullOrEmpty(f.Sha256) && !string.Equals(FileOps.Sha256(full), f.Sha256, StringComparison.OrdinalIgnoreCase))
                { s.Problem = "原版文件不一致 " + f.Path; break; }
            }
            return s;
        }

        /// <summary>
        /// 识别游戏版本，以脚本包指纹为准：
        /// 官方原版的脚本都打包在 game/scripts.rpa 里，目录里没有单独的 options.rpy，
        /// 所以先按 catalog 里的 versionFingerprints 比对 scripts.rpa 的大小和哈希（补丁不改它，汉化前后都能认）。
        /// 指纹文件存在却对不上任何已知版本 → 修改版或未收录的新版本，不允许安装。
        /// catalog 没有为这个游戏写指纹时，退回读 options.rpy 的 config.version。
        /// 注意 script_version.txt 记录的是 Ren'Py 引擎版本（如 (8, 5, 3)），不是游戏版本。
        /// </summary>
        public static string DetectVersion(GameProfile p, string dir, out string problem)
        {
            problem = null;
            var text = ReadVersionText(p, dir);
            if (p.VersionFingerprints.Count == 0) return text;

            bool anyFileFound = false;
            foreach (var group in p.VersionFingerprints.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
            {
                // {exe}：单文件游戏直接对程序本身做指纹；目录里有多个符合的程序（如原版和改过的版本）时逐个比对
                var files = group.Key == "{exe}"
                    ? ExeFinder.Find(p, dir)
                    : new List<string> { Path.Combine(dir, group.Key.Replace('/', '\\')) };
                foreach (var full in files)
                {
                    if (!File.Exists(full)) continue;
                    anyFileFound = true;
                    long size = new FileInfo(full).Length;
                    var candidates = group.Where(f => f.Size == size).ToList();
                    if (candidates.Count == 0) continue;
                    var hash = CachedSha256(full);
                    if (hash == null) continue;
                    var hit = candidates.FirstOrDefault(f => string.Equals(f.Sha256, hash, StringComparison.OrdinalIgnoreCase));
                    if (hit != null) return hit.Version;
                }
            }

            problem = anyFileFound
                ? "不是已收录的版本（可能是其他版本或修改版），暂不能安装"
                : "缺少用来识别版本的游戏文件";
            return text;
        }

        /// <summary>单文件游戏的 exe 有几百 MB，按"路径 + 大小 + 修改时间"缓存哈希，避免每次刷新都重算。</summary>
        static readonly Dictionary<string, string> hashCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static string CachedSha256(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                var key = fi.FullName + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
                lock (hashCache)
                    if (hashCache.TryGetValue(key, out var h)) return h;
                var hash = FileOps.Sha256(path);
                lock (hashCache) hashCache[key] = hash;
                return hash;
            }
            catch { return null; }
        }

        static string ReadVersionText(GameProfile p, string dir)
        {
            if (string.IsNullOrEmpty(p.VersionFile) || string.IsNullOrEmpty(p.VersionRegex)) return null;
            try
            {
                var file = Path.Combine(dir, p.VersionFile.Replace('/', '\\'));
                if (!File.Exists(file)) return null;
                var m = Regex.Match(File.ReadAllText(file), p.VersionRegex);
                return m.Success ? m.Groups[1].Value.Trim() : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// 游戏是否正在运行：进程名符合 exeNames（支持通配符），且程序位于这个游戏目录里。
        /// Ren'Py 的启动器会在 lib 子目录再拉起一个同名进程，所以判断"在目录之下"而不是"在目录里"。
        /// </summary>
        public static bool IsRunning(GameCandidate game)
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (!ExeFinder.IsMatch(game.Profile, proc.ProcessName + ".exe")) continue;
                    var path = proc.MainModule?.FileName;
                    if (path == null || path.StartsWith(game.Dir + "\\", StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch { if (ExeFinder.IsMatch(game.Profile, proc.ProcessName + ".exe")) return true; } // 拿不到路径时保守处理
                finally { proc.Dispose(); }
            }
            return false;
        }

        /// <summary>存档目录：catalog 里写了 saveDirectory（可含 %APPDATA% 等环境变量）就用它，否则按 Ren'Py 规则从 options.rpy 推算。</summary>
        public static string SaveDirectory(GameCandidate game)
        {
            if (!string.IsNullOrEmpty(game.Profile.SaveDirectory))
                return Environment.ExpandEnvironmentVariables(game.Profile.SaveDirectory).Replace('/', '\\');
            try
            {
                var opt = Path.Combine(game.Dir, @"game\options.rpy");
                if (!File.Exists(opt)) return null;
                var m = Regex.Match(File.ReadAllText(opt), "config\\.save_directory\\s*=\\s*\"([^\"]+)\"");
                if (!m.Success) return null;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RenPy", m.Groups[1].Value);
            }
            catch { return null; }
        }
    }
}
