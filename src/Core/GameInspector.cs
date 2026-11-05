using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FMGPatcher.Core
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
                var full = Path.Combine(dir, group.Key.Replace('/', '\\'));
                if (!File.Exists(full)) continue;
                anyFileFound = true;
                long size = new FileInfo(full).Length;
                var candidates = group.Where(f => f.Size == size).ToList();
                if (candidates.Count == 0) continue;
                string hash;
                try { hash = FileOps.Sha256(full); } catch { continue; }
                var hit = candidates.FirstOrDefault(f => string.Equals(f.Sha256, hash, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit.Version;
            }

            problem = anyFileFound
                ? "不是已知的官方版本，可能是修改版，不能安装"
                : "缺少游戏脚本包，无法识别版本";
            return text;
        }

        static string ReadVersionText(GameProfile p, string dir)
        {
            try
            {
                var file = Path.Combine(dir, p.VersionFile.Replace('/', '\\'));
                if (!File.Exists(file)) return null;
                var m = Regex.Match(File.ReadAllText(file), p.VersionRegex);
                return m.Success ? m.Groups[1].Value.Trim() : null;
            }
            catch { return null; }
        }

        /// <summary>游戏是否正在运行。Ren'Py 的启动器会再拉起一个同名进程，所以按名字找即可。</summary>
        public static bool IsRunning(GameCandidate game)
        {
            foreach (var exe in game.Profile.ExeNames)
            {
                var procs = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe));
                foreach (var proc in procs)
                {
                    try
                    {
                        var path = proc.MainModule?.FileName;
                        if (path == null || path.StartsWith(game.Dir + "\\", StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    catch { return true; } // 拿不到路径时保守处理
                    finally { proc.Dispose(); }
                }
            }
            return false;
        }

        public static string SaveDirectory(GameCandidate game)
        {
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
