using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BackloggdAutoSync
{
    public static class GameTitleMatcher
    {
        private static readonly Regex SymbolsRegex = new Regex(@"[™®©℠]", RegexOptions.Compiled);
        private static readonly Regex EditionDashRegex = new Regex(
            @"\s*[–—-]\s*(?:Digital\s+)?(?:Nintendo\s+Switch(?:\s+2)?\s+Edition|Deluxe|Collector's|Definitive|Game of the Year|GOTY|Enhanced|Special|Director's Cut|Remastered|Complete|Ultimate|Standard|Edition).*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EditionParenRegex = new Regex(
            @"\s*\((?:(?:Standard|Deluxe|Definitive|Collector's|Enhanced|Remastered|GOTY)\s+Edition|Classic|Retired|(?:English|US|EU|JP|ROW|WW|International)\s+Ver\.?)\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex StripBaseSuffixRegex = new Regex(
            @"(?:\s*(?:[–—:,]|-)\s*(?:The\s+)?(?:Digital\s+)?(?:Deluxe|Collector's|Definitive|Game of the Year|GOTY|Enhanced|Special|Director's Cut|Remastered|Complete|Ultimate|Standard|Anniversary|Scholarship|Legacy|Farewell|Sunset|Steam|VR|Edition|Multiplayer|Zombies|Online\s+Multiplayer\s+Board\s+Game).*|\s+(?:(?:Digital\s+)?(?:Deluxe|Collector's|Definitive|Game of the Year|GOTY|Enhanced|Special|Director's Cut|Complete|Ultimate|Standard|Anniversary|Scholarship|Steam|VR)\s+Edition|Director's\s+Cut|Enhanced|Legacy|Classic|SA))$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TrailingParenRegex = new Regex(@"\s*\([^)]*\)\s*$", RegexOptions.Compiled);
        private static readonly Regex FranchisePrefixRegex = new Regex(
            @"^(?:Tom\s+Clancy'?s|Sid\s+Meier'?s|Disney'?s|Marvel'?s|Clive\s+Barker'?s|Peter\s+Jackson'?s)\s+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        public static string StripDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            string normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat != UnicodeCategory.NonSpacingMark &&
                    cat != UnicodeCategory.SpacingCombiningMark &&
                    cat != UnicodeCategory.EnclosingMark)
                {
                    if (c == '’' || c == '‘') sb.Append('\'');
                    else if (c == '–' || c == '—') sb.Append('-');
                    else sb.Append(c);
                }
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        public static string CleanGameTitle(string rawTitle)
        {
            if (string.IsNullOrWhiteSpace(rawTitle)) return "";

            string t = SymbolsRegex.Replace(rawTitle, "");
            t = EditionDashRegex.Replace(t, "");
            t = EditionParenRegex.Replace(t, "");
            t = WhitespaceRegex.Replace(t, " ").Trim();
            return t;
        }

        public static List<string> GetSearchQueryCandidates(string rawTitle)
        {
            var candidates = new List<string>();
            if (string.IsNullOrWhiteSpace(rawTitle)) return candidates;

            Action<string> addIfUnique = (s) =>
            {
                if (string.IsNullOrWhiteSpace(s)) return;
                string trimmed = WhitespaceRegex.Replace(s, " ").Trim();
                if (trimmed.Length < 2) return;
                string stripped = StripDiacritics(trimmed);
                if (!candidates.Exists(x => string.Equals(x, stripped, StringComparison.OrdinalIgnoreCase)))
                {
                    candidates.Add(stripped);
                }
            };

            string clean = CleanGameTitle(rawTitle);
            addIfUnique(clean);

            // Candidate without trailing parentheses like "(2005)", "(Classic)", "(English Ver.)"
            string noParen = TrailingParenRegex.Replace(clean, "");
            addIfUnique(noParen);

            // Candidate with edition/subtitle suffixes stripped (e.g. ": The Complete Edition", " - Multiplayer", " Ultimate Edition")
            string strippedEdition = StripBaseSuffixRegex.Replace(noParen, "");
            addIfUnique(strippedEdition);

            // Candidate with "&" replaced by "and"
            if (clean.Contains("&"))
            {
                addIfUnique(clean.Replace("&", "and"));
            }

            // Candidate before colon or " - " if the title has a long subtitle
            int colonIdx = strippedEdition.IndexOf(':');
            if (colonIdx >= 3)
            {
                addIfUnique(strippedEdition.Substring(0, colonIdx));
            }
            int dashIdx = strippedEdition.IndexOf(" - ", StringComparison.Ordinal);
            if (dashIdx >= 3)
            {
                addIfUnique(strippedEdition.Substring(0, dashIdx));
            }

            return candidates;
        }

        public static string NormalizeTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";

            string stripped = StripDiacritics(title);
            // Normalize "&" to "and" so "SEGA Mega Drive & Genesis Classics" matches "Sega Mega Drive and Genesis Classics"
            stripped = stripped.Replace("&", "and");
            var sb = new StringBuilder(stripped.Length);
            for (int i = 0; i < stripped.Length; i++)
            {
                char c = char.ToLowerInvariant(stripped[i]);
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

            string cleanTarget = CleanGameTitle(targetTitle);
            string normClean = NormalizeTitle(cleanTarget);
            string baseTarget = StripBaseSuffixRegex.Replace(TrailingParenRegex.Replace(cleanTarget, ""), "").Trim();
            string normBase = NormalizeTitle(baseTarget);

            // Extract explicit release year in parentheses if present, e.g., "Resident Evil 4 (2005)" -> "2005"
            string targetYear = null;
            Match yearParen = Regex.Match(targetTitle ?? "", @"\((\d{4})\)");
            if (yearParen.Success)
            {
                targetYear = yearParen.Groups[1].Value;
            }

            // 1. Exact normalized match (also preferring matching release year if specified in title)
            GameSearchResult firstExact = null;
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (r == null || string.IsNullOrEmpty(r.Title)) continue;

                string normRes = NormalizeTitle(r.Title);
                if (normRes == normTarget || (!string.IsNullOrEmpty(normClean) && normRes == normClean))
                {
                    if (!string.IsNullOrEmpty(targetYear) && r.ReleaseYear == targetYear) return r;
                    if (firstExact == null) firstExact = r;
                }
            }
            if (firstExact != null) return firstExact;

            // 2. Exact match against base title (with edition/legacy/multiplayer suffixes or franchise prefixes stripped)
            string strippedFranchiseTarget = NormalizeTitle(FranchisePrefixRegex.Replace(baseTarget, ""));
            if (!string.IsNullOrEmpty(normBase))
            {
                for (int i = 0; i < results.Count; i++)
                {
                    var r = results[i];
                    if (r == null || string.IsNullOrEmpty(r.Title)) continue;

                    string normRes = NormalizeTitle(r.Title);
                    string strippedFranchiseRes = NormalizeTitle(FranchisePrefixRegex.Replace(r.Title, ""));
                    if (normRes == normBase ||
                        (!string.IsNullOrEmpty(strippedFranchiseRes) && strippedFranchiseRes == normBase) ||
                        (!string.IsNullOrEmpty(strippedFranchiseTarget) && (normRes == strippedFranchiseTarget || strippedFranchiseRes == strippedFranchiseTarget)))
                    {
                        if (!string.IsNullOrEmpty(targetYear) && r.ReleaseYear == targetYear) return r;
                        if (firstExact == null) firstExact = r;
                    }
                }
                if (firstExact != null) return firstExact;
            }

            // 3. Prefix match
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (r == null || string.IsNullOrEmpty(r.Title)) continue;

                string normRes = NormalizeTitle(r.Title);
                if (!string.IsNullOrEmpty(normRes) &&
                    (normRes.StartsWith(normTarget) || normTarget.StartsWith(normRes) ||
                     (!string.IsNullOrEmpty(normBase) && (normRes.StartsWith(normBase) || normBase.StartsWith(normRes)))))
                {
                    return r;
                }
            }

            // 4. Fallback: if only 1 result came back from search, use it
            if (results.Count == 1) return results[0];

            return null;
        }
    }
}
