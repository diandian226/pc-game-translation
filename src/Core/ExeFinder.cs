using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LocaForge.Core
{
    /// <summary>
    /// catalog 里的 exeNames 可以写通配符（如 Realm_of_Evergrow*.exe），因为玩家常给单文件游戏改名。
    /// 这里统一处理"某个目录里有哪些符合的游戏程序"。
    /// </summary>
    public static class ExeFinder
    {
        static readonly Dictionary<string, Regex> cache = new Dictionary<string, Regex>();

        public static Regex ToRegex(string pattern)
        {
            lock (cache)
            {
                if (!cache.TryGetValue(pattern, out var r))
                    cache[pattern] = r = new Regex("^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                return r;
            }
        }

        public static bool IsMatch(GameProfile p, string fileName) => p.ExeNames.Any(e => ToRegex(e).IsMatch(fileName));

        /// <summary>目录里所有符合的游戏程序（完整路径，按文件名排序）。自解压安装包和不符合引擎特征的 exe 会被排除。</summary>
        public static List<string> Find(GameProfile p, string dir)
        {
            var list = new List<string>();
            try
            {
                foreach (var e in p.ExeNames)
                {
                    if (e.IndexOfAny(new[] { '*', '?' }) < 0)
                    {
                        var full = Path.Combine(dir, e);
                        if (File.Exists(full)) list.Add(full);
                    }
                    else
                    {
                        // Windows 的通配符对三位扩展名比较宽松（*.exe 也会匹配 .exe1），再用正则精确过滤一次
                        foreach (var f in Directory.EnumerateFiles(dir, e))
                            if (ToRegex(e).IsMatch(Path.GetFileName(f))) list.Add(f);
                    }
                }
            }
            catch { }
            return list.Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(f => !IsSelfExtractor(f) && PassesCheck(p, f))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// 自解压安装包（如 7-Zip、WinRAR 生成的 exe）常常和游戏同名，例如"Realm_of_Evergrow_0.2.8（万物生长领域）.exe"，
        /// 但里面装的是压缩包。按文件属性里的描述和产品名识别。
        /// </summary>
        public static bool IsSelfExtractor(string exe)
        {
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(exe);
                var text = (vi.FileDescription + " " + vi.ProductName + " " + vi.CompanyName).ToLowerInvariant();
                return text.Contains("sfx") || text.Contains("7-zip") || text.Contains("winrar") ||
                       text.Contains("self-extract") || text.Contains("自解压");
            }
            catch { return false; }
        }

        /// <summary>
        /// 按 catalog 的 exeCheck 确认 exe 真的是这个引擎的游戏：
        /// godot —— 资源包内嵌时 exe 末尾有 "GDPC" 标记，不内嵌时旁边有同名 .pck。
        /// </summary>
        static bool PassesCheck(GameProfile p, string exe)
        {
            if (string.IsNullOrEmpty(p.ExeCheck)) return true;
            if (p.ExeCheck == "godot")
            {
                if (File.Exists(Path.ChangeExtension(exe, ".pck"))) return true;
                try
                {
                    using (var fs = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        if (fs.Length < 16) return false;
                        fs.Seek(-4, SeekOrigin.End);
                        var b = new byte[4];
                        return fs.Read(b, 0, 4) == 4 && b[0] == 'G' && b[1] == 'D' && b[2] == 'P' && b[3] == 'C';
                    }
                }
                catch { return false; }
            }
            return true;   // 不认识的检查类型：不拦截，交给版本指纹判断
        }
    }
}
