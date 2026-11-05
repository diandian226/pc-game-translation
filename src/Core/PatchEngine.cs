using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace FMGPatcher.Core
{
    public sealed class PatchProgress
    {
        public int Percent;      // 0..100，-1 表示不确定
        public string Message;
        public PatchProgress(int percent, string message) { Percent = percent; Message = message; }
    }

    /// <summary>用户能看懂的失败原因，界面直接显示 Message。</summary>
    public sealed class PatchException : Exception
    {
        public PatchException(string message) : base(message) { }
    }

    /// <summary>
    /// 安装流程：检查 → 写"安装中"记录 → 逐个文件（备份原文件 → 写临时文件 → 替换）→ 校验 → 记录改为"已安装"。
    /// 任一步失败自动回滚；中途断电则下次打开显示"上次安装中断"，可一键还原。
    /// </summary>
    public static class PatchEngine
    {
        public const string RecordDirName = "_zh_patch";
        const string RecordFile = "install.json";
        const string BackupDirName = "backup";
        const string TempSuffix = ".zhtmp";

        public static string RecordDir(string gameDir) => Path.Combine(gameDir, RecordDirName);
        static string BackupRoot(string gameDir) => Path.Combine(RecordDir(gameDir), BackupDirName);

        public static InstallRecord ReadRecord(string gameDir)
        {
            var f = Path.Combine(RecordDir(gameDir), RecordFile);
            try { return File.Exists(f) ? Json.Load<InstallRecord>(f) : null; }
            catch { return null; }
        }

        static void SaveRecord(string gameDir, InstallRecord r) =>
            Json.Save(Path.Combine(RecordDir(gameDir), RecordFile), r);

        public static string PayloadPath(PatchPackage patch) =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, patch.Payload.Replace('/', '\\'));

        public static long PayloadSize(PatchPackage patch)
        {
            try
            {
                using (var zip = ZipFile.OpenRead(PayloadPath(patch)))
                    return zip.Entries.Where(e => e.Name.Length > 0).Sum(e => e.Length);
            }
            catch { return -1; }
        }

        public static int PayloadFileCount(PatchPackage patch)
        {
            try
            {
                using (var zip = ZipFile.OpenRead(PayloadPath(patch)))
                    return zip.Entries.Count(e => e.Name.Length > 0);
            }
            catch { return -1; }
        }

        // ================= 安装 =================
        public static void Install(GameStatus status, IProgress<PatchProgress> progress, CancellationToken ct)
        {
            var game = status.Game;
            var patch = status.MatchedPatch;
            var dir = game.Dir;
            if (!status.CanInstall) throw new PatchException(status.Problem ?? "这个版本没有可用的补丁");
            if (GameInspector.IsRunning(game)) throw new PatchException("游戏正在运行，请先关闭游戏");

            var payload = PayloadPath(patch);
            if (!File.Exists(payload)) throw new PatchException("找不到补丁包 " + patch.Payload);

            // 已装过或上次中断：先还原到原版，再装新的
            if (ReadRecord(dir) != null)
            {
                progress?.Report(new PatchProgress(-1, "先还原上一次的安装"));
                Uninstall(game, progress);
            }

            Directory.CreateDirectory(RecordDir(dir)); // 没有写权限会在这里抛 UnauthorizedAccessException，由界面提权重试
            TrySetHidden(RecordDir(dir));

            using (var zip = ZipFile.OpenRead(payload))
            {
                var root = FileOps.NormalizeRel(patch.PayloadRoot ?? "");
                var entries = new List<KeyValuePair<string, ZipArchiveEntry>>();
                foreach (var e in zip.Entries)
                {
                    if (e.Name.Length == 0) continue; // 目录项
                    var rel = FileOps.NormalizeRel(e.FullName);
                    if (root.Length > 0)
                    {
                        if (!rel.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) continue;
                        rel = rel.Substring(root.Length + 1);
                    }
                    entries.Add(new KeyValuePair<string, ZipArchiveEntry>(rel, e));
                }
                var payloadSet = new HashSet<string>(entries.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);

                long need = entries.Sum(x => x.Value.Length) * 2; // 新文件 + 备份，宽松估计
                var drive = new DriveInfo(Path.GetPathRoot(dir));
                if (drive.IsReady && drive.AvailableFreeSpace < need)
                    throw new PatchException("磁盘空间不足，至少需要 " + FileOps.FormatSize(need));

                var record = new InstallRecord
                {
                    State = "installing",
                    ProfileId = game.Profile.Id,
                    PatchId = patch.Id,
                    PatchVersion = patch.PatchVersion,
                    GameVersion = status.Version,
                    ToolVersion = AppInfo.Version,
                    InstalledAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                };
                SaveRecord(dir, record);

                try
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var rel = entries[i].Key;
                        var target = FileOps.SafeCombine(dir, rel);
                        progress?.Report(new PatchProgress(i * 90 / entries.Count, "写入 " + rel));

                        EnsureDir(dir, Path.GetDirectoryName(target), record);

                        bool backedUp = false;
                        if (File.Exists(target))
                        {
                            BackupFile(dir, rel);
                            backedUp = true;
                        }

                        var tmp = target + TempSuffix;
                        string hash;
                        using (var src = entries[i].Value.Open())
                        using (var dst = File.Create(tmp))
                        using (var sha = SHA256.Create())
                        using (var cs = new CryptoStream(dst, sha, CryptoStreamMode.Write))
                        {
                            src.CopyTo(cs, 1 << 16);
                            cs.FlushFinalBlock();
                            hash = FileOps.ToHex(sha.Hash);
                        }
                        FileOps.ReplaceFile(tmp, target);
                        record.Files.Add(new InstalledFile { Path = rel, Sha256 = hash, BackedUp = backedUp });

                        // 覆盖了 .rpy：把旧 .rpyc 收进备份，让 Ren'Py 用新脚本重新编译
                        if (patch.RemoveStaleCompiled && rel.EndsWith(".rpy", StringComparison.OrdinalIgnoreCase))
                        {
                            var relc = rel + "c";
                            var compiled = target + "c";
                            if (!payloadSet.Contains(relc) && File.Exists(compiled))
                            {
                                BackupFile(dir, relc);
                                File.Delete(compiled);
                                record.Removed.Add(relc);
                            }
                        }

                        if (i % 16 == 15) SaveRecord(dir, record);
                    }

                    progress?.Report(new PatchProgress(92, "校验文件"));
                    foreach (var f in record.Files)
                    {
                        ct.ThrowIfCancellationRequested();
                        var full = FileOps.SafeCombine(dir, f.Path);
                        if (!File.Exists(full) || FileOps.Sha256(full) != f.Sha256)
                            throw new PatchException("校验失败：" + f.Path);
                    }

                    record.State = "installed";
                    SaveRecord(dir, record);
                    progress?.Report(new PatchProgress(100, "已安装 " + record.Files.Count + " 个文件"));
                }
                catch
                {
                    progress?.Report(new PatchProgress(-1, "出错了，正在还原"));
                    SaveRecord(dir, record);
                    try { Rollback(dir, record, null); DeleteRecordDir(dir); } catch { /* 保留记录，下次可再次还原 */ }
                    throw;
                }
            }
        }

        // ================= 还原 =================
        public static void Uninstall(GameCandidate game, IProgress<PatchProgress> progress)
        {
            var dir = game.Dir;
            var record = ReadRecord(dir) ?? throw new PatchException("没有找到安装记录，无法自动还原");
            if (GameInspector.IsRunning(game)) throw new PatchException("游戏正在运行，请先关闭游戏");
            int skipped = Rollback(dir, record, progress);
            DeleteRecordDir(dir);
            progress?.Report(new PatchProgress(100, skipped == 0
                ? "已还原为原版"
                : "已还原为原版，" + skipped + " 个文件在安装后被改过，保留未动"));
        }

        /// <summary>返回因"安装后被改动过"而跳过的文件数。</summary>
        static int Rollback(string dir, InstallRecord record, IProgress<PatchProgress> progress)
        {
            int skipped = 0;
            var backup = BackupRoot(dir);
            for (int i = record.Files.Count - 1; i >= 0; i--)
            {
                var f = record.Files[i];
                var target = FileOps.SafeCombine(dir, f.Path);
                progress?.Report(new PatchProgress((record.Files.Count - i) * 90 / Math.Max(1, record.Files.Count), "还原 " + f.Path));
                if (File.Exists(target))
                {
                    // 游戏更新等原因改了这个文件：不删，也不用旧备份覆盖
                    if (!string.Equals(FileOps.Sha256(target), f.Sha256, StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                    File.SetAttributes(target, FileAttributes.Normal);
                    File.Delete(target);
                }
                if (f.BackedUp)
                {
                    var src = FileOps.SafeCombine(backup, f.Path);
                    if (File.Exists(src)) File.Copy(src, target, true);
                }
            }
            foreach (var rel in record.Removed)
            {
                var target = FileOps.SafeCombine(dir, rel);
                var src = FileOps.SafeCombine(backup, rel);
                if (!File.Exists(target) && File.Exists(src)) File.Copy(src, target);
            }
            for (int i = record.CreatedDirs.Count - 1; i >= 0; i--)
            {
                var d = FileOps.SafeCombine(dir, record.CreatedDirs[i]);
                try { if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
            }
            return skipped;
        }

        // ================= 工具函数 =================
        static void BackupFile(string dir, string rel)
        {
            var src = FileOps.SafeCombine(dir, rel);
            var dst = FileOps.SafeCombine(BackupRoot(dir), rel);
            if (File.Exists(dst)) return; // 只保留第一次见到的原文件
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(src, dst);
        }

        static void EnsureDir(string gameDir, string target, InstallRecord record)
        {
            if (Directory.Exists(target)) return;
            EnsureDir(gameDir, Path.GetDirectoryName(target), record);
            Directory.CreateDirectory(target);
            var rel = FileOps.NormalizeRel(target.Substring(gameDir.TrimEnd('\\').Length));
            record.CreatedDirs.Add(rel);
        }

        static void DeleteRecordDir(string dir)
        {
            var d = RecordDir(dir);
            if (!Directory.Exists(d)) return;
            foreach (var f in Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            new DirectoryInfo(d).Attributes = FileAttributes.Directory;
            Directory.Delete(d, true);
        }

        static void TrySetHidden(string d)
        {
            try { new DirectoryInfo(d).Attributes |= FileAttributes.Hidden; } catch { }
        }
    }
}
