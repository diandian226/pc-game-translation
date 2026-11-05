using System;
using System.Collections.Generic;
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

        /// <summary>目录里所有符合的游戏程序（完整路径，按文件名排序）。</summary>
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
            return list.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
