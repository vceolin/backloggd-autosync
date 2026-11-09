using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace BackloggdAutoSync
{
    public static class GameTitleMatcher
    {
        private static readonly Regex SymbolsRegex = new Regex(@"[™®©]", RegexOptions.Compiled);
        private static readonly Regex EditionDashRegex = new Regex(
            @"\s*[–—-]\s*(?:Digital\s+)?(?:Nintendo\s+Switch(?:\s+2)?\s+Edition|Deluxe|Collector's|Definitive|Game of the Year|GOTY|Enhanced|Special|Director's Cut|Remastered|Complete|Ultimate|Standard|Edition).*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EditionParenRegex = new Regex(
            @"\s*\((?:Standard|Deluxe|Definitive|Collector's|Enhanced|Remastered|GOTY)\s+Edition\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        public static string CleanGameTitle(string rawTitle)
        {
            if (string.IsNullOrWhiteSpace(rawTitle)) return "";

            string t = SymbolsRegex.Replace(rawTitle, "");
            t = EditionDashRegex.Replace(t, "");
            t = EditionParenRegex.Replace(t, "");
            t = WhitespaceRegex.Replace(t, " ").Trim();
            return t;
        }

        public static string NormalizeTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";

            var sb = new StringBuilder(title.Length);
            for (int i = 0; i < title.Length; i++)
            {
                char c = char.ToLowerInvariant(title[i]);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        public static GameSearchResult SelectBestMatch(string targetTitle, List<GameSearchResult> results)
        {
            if (results == null || results.Count == 0) return null;

            string normTarget = NormalizeTitle(targetTitle);
            if (string.IsNullOrEmpty(normTarget)) return null;

            // 1. Exact normalized match
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (r == null || string.IsNullOrEmpty(r.Title)) continue;

                string normRes = NormalizeTitle(r.Title);
                if (normRes == normTarget) return r;
            }

            // 2. Prefix match
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (r == null || string.IsNullOrEmpty(r.Title)) continue;

                string normRes = NormalizeTitle(r.Title);
                if (!string.IsNullOrEmpty(normRes) && (normRes.StartsWith(normTarget) || normTarget.StartsWith(normRes)))
                {
                    return r;
                }
            }

            // 3. Fallback: if only 1 result came back from search, use it
            if (results.Count == 1) return results[0];

            return null;
        }
    }
}
