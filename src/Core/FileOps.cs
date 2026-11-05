using System;
using System.IO;
using System.Security.Cryptography;

namespace FMGPatcher.Core
{
    public static class FileOps
    {
        public static string Sha256(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            using (var sha = SHA256.Create())
                return ToHex(sha.ComputeHash(fs));
        }

        public static string ToHex(byte[] bytes)
        {
            var c = new char[bytes.Length * 2];
            const string hex = "0123456789abcdef";
            for (int i = 0; i < bytes.Length; i++) { c[i * 2] = hex[bytes[i] >> 4]; c[i * 2 + 1] = hex[bytes[i] & 15]; }
            return new string(c);
        }

        /// <summary>用临时文件替换目标：目标不存在就直接改名，存在就先删再改名（跨卷安全）。</summary>
        public static void ReplaceFile(string tmp, string target)
        {
            if (File.Exists(target))
            {
                File.SetAttributes(target, FileAttributes.Normal);
                File.Delete(target);
            }
            File.Move(tmp, target);
        }

        /// <summary>把相对路径安全地拼到根目录下，拒绝 ..\ 越界（防 zip slip）。</summary>
        public static string SafeCombine(string root, string relative)
        {
            relative = relative.Replace('/', '\\').TrimStart('\\');
            var full = Path.GetFullPath(Path.Combine(root, relative));
            var rootFull = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("补丁包里有越界路径：" + relative);
            return full;
        }

        public static string NormalizeRel(string rel) => rel.Replace('\\', '/').TrimStart('/');

        public static string FormatSize(long bytes)
        {
            if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.0") + " GB";
            if (bytes >= 1L << 20) return (bytes / (double)(1L << 20)).ToString("0.0") + " MB";
            if (bytes >= 1L << 10) return (bytes / (double)(1L << 10)).ToString("0") + " KB";
            return bytes + " B";
        }
    }
}
