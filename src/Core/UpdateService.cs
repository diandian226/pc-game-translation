using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace FMGPatcher.Core
{
    /// <summary>
    /// update.json：随每个 GitHub Release 发布，工具从 releases/latest 读取。
    /// 同目录的 update.json.sig 是对 update.json 原始字节的 RSA-SHA256 签名，验证通过才会使用。
    /// </summary>
    [DataContract]
    public sealed class UpdateManifest
    {
        [DataMember(Name = "schema")] public int Schema;
        [DataMember(Name = "published")] public string Published;
        [DataMember(Name = "notes")] public string Notes;
        [DataMember(Name = "page")] public string Page;
        [DataMember(Name = "tool")] public ToolRelease Tool;
        /// <summary>低于这个版本的工具不使用本清单里的补丁更新（先更新工具）。</summary>
        [DataMember(Name = "minToolVersion")] public string MinToolVersion;
        [DataMember(Name = "catalog")] public Catalog Catalog;
        [DataMember(Name = "payloads")] public List<RemoteFile> Payloads = new List<RemoteFile>();
    }

    [DataContract]
    public sealed class ToolRelease
    {
        [DataMember(Name = "version")] public string Version;
        [DataMember(Name = "url")] public string Url;
        [DataMember(Name = "size")] public long Size;
        [DataMember(Name = "sha256")] public string Sha256;
    }

    [DataContract]
    public sealed class RemoteFile
    {
        /// <summary>相对路径，与 catalog 里 patches[].payload 一致，例如 payload/fmgworld-zh-0.2.1.zip</summary>
        [DataMember(Name = "path")] public string Path;
        [DataMember(Name = "url")] public string Url;
        [DataMember(Name = "size")] public long Size;
        [DataMember(Name = "sha256")] public string Sha256;
    }

    public sealed class UpdateInfo
    {
        public UpdateManifest Manifest;
        public bool ToolUpdate;          // 有新版工具
        public bool CatalogUpdate;       // 有新版补丁清单（可直接应用）
        public bool CatalogNeedsTool;    // 有新补丁，但要先更新工具
        public List<string> PatchChanges = new List<string>();  // 给界面看的变化说明
        public bool Any => ToolUpdate || CatalogUpdate;
    }

    public static class UpdateService
    {
        public const string Repo = "diandian226/pc-game-translation";
        static string LatestBase => Environment.GetEnvironmentVariable("FMGPATCHER_UPDATE_BASE")   // 测试用：指向本地服务器
                                    ?? "https://github.com/" + Repo + "/releases/latest/download";
        public static string ReleasesPage => "https://github.com/" + Repo + "/releases/latest";

        static readonly Lazy<HttpClient> http = new Lazy<HttpClient>(() =>
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, UseProxy = true })
            {
                Timeout = TimeSpan.FromMinutes(10),
            };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("FMGPatcher/" + AppInfo.Version);
            return c;
        });

        // ================= 检查 =================
        public static async Task<UpdateInfo> CheckAsync(Catalog current, CancellationToken ct)
        {
            byte[] json, sig;
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(TimeSpan.FromSeconds(20));
                json = await GetBytes(LatestBase + "/update.json", cts.Token);
                sig = await GetBytes(LatestBase + "/update.json.sig", cts.Token);
            }
            if (!Verify(json, sig)) throw new PatchException("更新信息的签名无效，已忽略");

            UpdateManifest m;
            using (var ms = new MemoryStream(json))
                m = (UpdateManifest)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(UpdateManifest)).ReadObject(ms);
            if (m.Schema != 1) throw new PatchException("更新信息格式不受支持，请到发布页手动下载新版");

            var info = new UpdateInfo { Manifest = m };
            info.ToolUpdate = m.Tool != null && AppInfo.VersionLess(AppInfo.Version, m.Tool.Version);

            if (m.Catalog != null && m.Catalog.Revision > (current?.Revision ?? 0))
            {
                if (!string.IsNullOrEmpty(m.MinToolVersion) && AppInfo.VersionLess(AppInfo.Version, m.MinToolVersion))
                    info.CatalogNeedsTool = true;
                else
                    info.CatalogUpdate = true;
                info.PatchChanges = DescribeChanges(current, m.Catalog);
            }
            return info;
        }

        static List<string> DescribeChanges(Catalog oldCat, Catalog newCat)
        {
            var list = new List<string>();
            foreach (var g in newCat.Games)
            {
                var og = oldCat?.Games.FirstOrDefault(x => x.Id == g.Id);
                foreach (var p in g.Patches)
                {
                    var op = og?.Patches.FirstOrDefault(x => x.Id == p.Id);
                    if (op == null) list.Add(g.Name + " " + p.GameVersion + " 新补丁 " + p.PatchVersion);
                    else if (op.PatchVersion != p.PatchVersion) list.Add(g.Name + " 补丁 " + op.PatchVersion + " → " + p.PatchVersion);
                }
            }
            if (list.Count == 0) list.Add("补丁清单有更新");
            return list;
        }

        // ================= 补丁更新 =================
        /// <summary>
        /// 下载新补丁包到缓存目录并校验，全部成功后才写入新的 catalog.json（写清单是最后一步，中途失败不影响现有补丁）。
        /// 补丁包内容没变时复用已有文件，不重复下载。
        /// </summary>
        public static async Task ApplyCatalogAsync(UpdateInfo info, IProgress<PatchProgress> progress, CancellationToken ct)
        {
            var m = info.Manifest;
            var cache = AppInfo.CacheDir;
            Directory.CreateDirectory(cache);

            var needed = m.Payloads.Where(p => m.Catalog.Games.Any(g => g.Patches.Any(x => SamePath(x.Payload, p.Path)))).ToList();
            long total = needed.Sum(p => p.Size), done = 0;

            foreach (var file in needed)
            {
                ct.ThrowIfCancellationRequested();
                var dest = FileOps.SafeCombine(cache, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));

                if (HasFile(dest, file)) { done += file.Size; continue; }
                var bundled = FileOps.SafeCombine(AppInfo.ExeDir, file.Path);
                if (HasFile(bundled, file))
                {
                    File.Copy(bundled, dest, true);
                    done += file.Size;
                    continue;
                }

                long baseDone = done;
                await DownloadAsync(file.Url, dest, file.Size, file.Sha256, new Progress<long>(n =>
                    progress?.Report(new PatchProgress(total > 0 ? (int)((baseDone + n) * 95 / total) : -1,
                        "下载 " + Path.GetFileName(file.Path) + "  " + FileOps.FormatSize(baseDone + n) + " / " + FileOps.FormatSize(total)))), ct);
                done += file.Size;
            }

            progress?.Report(new PatchProgress(98, "写入补丁清单"));
            Json.Save(Path.Combine(cache, "catalog.json"), m.Catalog);

            // 清理不再被引用的旧补丁包
            try
            {
                var keep = new HashSet<string>(needed.Select(p => FileOps.SafeCombine(cache, p.Path)), StringComparer.OrdinalIgnoreCase);
                var payloadDir = Path.Combine(cache, "payload");
                if (Directory.Exists(payloadDir))
                    foreach (var f in Directory.GetFiles(payloadDir, "*", SearchOption.AllDirectories))
                        if (!keep.Contains(f)) File.Delete(f);
            }
            catch { }
            progress?.Report(new PatchProgress(100, "补丁清单已更新"));
        }

        // ================= 工具自更新 =================
        /// <summary>
        /// 下载新 exe 并校验，然后把正在运行的 exe 改名为 .old（Windows 允许给运行中的程序改名），新 exe 放回原名。
        /// 返回新 exe 路径，由调用方重启。.old 在下次启动时删除。
        /// </summary>
        public static async Task<string> ApplyToolAsync(UpdateInfo info, IProgress<PatchProgress> progress, CancellationToken ct)
        {
            var t = info.Manifest.Tool;
            var exe = AppInfo.ExePath;
            var tmp = exe + ".new";
            var old = exe + ".old";

            await DownloadAsync(t.Url, tmp, t.Size, t.Sha256, new Progress<long>(n =>
                progress?.Report(new PatchProgress(t.Size > 0 ? (int)(n * 95 / t.Size) : -1,
                    "下载新版工具  " + FileOps.FormatSize(n) + " / " + FileOps.FormatSize(t.Size)))), ct);

            progress?.Report(new PatchProgress(97, "替换工具"));
            try
            {
                if (File.Exists(old)) File.Delete(old);
                File.Move(exe, old);
                try { File.Move(tmp, exe); }
                catch { File.Move(old, exe); throw; } // 放不进去就把旧的挪回来
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                throw;
            }
            progress?.Report(new PatchProgress(100, "新版工具已就绪"));
            return exe;
        }

        public static void CleanupOldExe()
        {
            try
            {
                var old = AppInfo.ExePath + ".old";
                if (File.Exists(old)) File.Delete(old);
                var tmp = AppInfo.ExePath + ".new";
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch { }
        }

        // ================= 工具函数 =================
        public static bool Verify(byte[] data, byte[] sigFile)
        {
            try
            {
                var sig = Convert.FromBase64String(System.Text.Encoding.ASCII.GetString(sigFile).Trim());
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(UpdateKey.PublicXml);
                    return rsa.VerifyData(data, CryptoConfig.MapNameToOID("SHA256"), sig);
                }
            }
            catch { return false; }
        }

        static async Task<byte[]> GetBytes(string url, CancellationToken ct)
        {
            using (var resp = await http.Value.GetAsync(url, ct))
            {
                if (resp.StatusCode == HttpStatusCode.NotFound) throw new PatchException("发布页上没有找到更新信息");
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsByteArrayAsync();
            }
        }

        static async Task DownloadAsync(string url, string dest, long size, string sha256, IProgress<long> progress, CancellationToken ct)
        {
            var part = dest + ".download";
            try
            {
                using (var resp = await http.Value.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    resp.EnsureSuccessStatusCode();
                    using (var src = await resp.Content.ReadAsStreamAsync())
                    using (var dst = File.Create(part))
                    using (var sha = SHA256.Create())
                    {
                        var buf = new byte[1 << 16];
                        long n = 0;
                        int r;
                        while ((r = await src.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                        {
                            await dst.WriteAsync(buf, 0, r, ct);
                            sha.TransformBlock(buf, 0, r, null, 0);
                            n += r;
                            if (size > 0 && n > size) throw new PatchException("下载的文件比预期大，已中止");
                            progress?.Report(n);
                        }
                        sha.TransformFinalBlock(buf, 0, 0);
                        if (size > 0 && n != size) throw new PatchException("下载不完整，请重试");
                        if (!string.Equals(FileOps.ToHex(sha.Hash), sha256, StringComparison.OrdinalIgnoreCase))
                            throw new PatchException("下载的文件校验失败，请重试");
                    }
                }
                FileOps.ReplaceFile(part, dest);
            }
            finally
            {
                try { if (File.Exists(part)) File.Delete(part); } catch { }
            }
        }

        static bool HasFile(string path, RemoteFile f)
        {
            try
            {
                return File.Exists(path) && new FileInfo(path).Length == f.Size &&
                       string.Equals(FileOps.Sha256(path), f.Sha256, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        static bool SamePath(string a, string b) =>
            string.Equals(FileOps.NormalizeRel(a ?? ""), FileOps.NormalizeRel(b ?? ""), StringComparison.OrdinalIgnoreCase);
    }
}
