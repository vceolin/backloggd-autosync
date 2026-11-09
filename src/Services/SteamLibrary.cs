using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BackloggdAutoSync
{
    public static class SteamLibrary
    {
        private static readonly Dictionary<int, string> _titleCache = new Dictionary<int, string>();
        private static readonly object _cacheLock = new object();

        public static List<string> GetSteamAppsDirectories()
        {
            var libraryDirs = new List<string>();
            try
            {
                string steamPath = null;
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key != null)
                    {
                        steamPath = key.GetValue("SteamPath") as string;
                    }
                }
                if (string.IsNullOrEmpty(steamPath)) return libraryDirs;
                steamPath = steamPath.Replace('/', '\\');

                string defaultSteamApps = Path.Combine(steamPath, "steamapps");
                if (Directory.Exists(defaultSteamApps)) libraryDirs.Add(defaultSteamApps);

                string vdfPath = Path.Combine(defaultSteamApps, "libraryfolders.vdf");
                if (File.Exists(vdfPath))
                {
                    string vdf = File.ReadAllText(vdfPath, Encoding.UTF8);
                    MatchCollection pathMatches = Regex.Matches(vdf, @"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase);
                    foreach (Match pm in pathMatches)
                    {
                        string libRoot = pm.Groups[1].Value.Replace(@"\\", @"\");
                        string sa = Path.Combine(libRoot, "steamapps");
                        if (Directory.Exists(sa) && !libraryDirs.Contains(sa))
                        {
                            libraryDirs.Add(sa);
                        }
                    }
                }
            }
            catch { }
            return libraryDirs;
        }

        public static List<InstalledSteamGame> GetInstalledSteamGames(List<int> ignoredAppIds = null)
        {
            var games = new List<InstalledSteamGame>();
            var seenIds = new HashSet<int>();

            foreach (string saDir in GetSteamAppsDirectories())
            {
                try
                {
                    string[] acfFiles = Directory.GetFiles(saDir, "appmanifest_*.acf");
                    foreach (string acfPath in acfFiles)
                    {
                        try
                        {
                            string acf = File.ReadAllText(acfPath, Encoding.UTF8);
                            Match idMatch = Regex.Match(acf, @"""appid""\s+""(\d+)""", RegexOptions.IgnoreCase);
                            Match nameMatch = Regex.Match(acf, @"""name""\s+""([^""]+)""", RegexOptions.IgnoreCase);
                            if (idMatch.Success && nameMatch.Success)
                            {
                                int appId = Convert.ToInt32(idMatch.Groups[1].Value);
                                string name = nameMatch.Groups[1].Value.Trim();

                                if (ignoredAppIds != null && ignoredAppIds.Contains(appId)) continue;
                                if (name.IndexOf("Proton ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    name.IndexOf("Steamworks Common", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    name.IndexOf("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    continue;
                                }

                                if (appId > 0 && !seenIds.Contains(appId))
                                {
                                    seenIds.Add(appId);
                                    games.Add(new InstalledSteamGame { AppId = appId, Title = name });
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            games.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            return games;
        }

        public static string FindTitleFromSteamManifests(int appId)
        {
            string manifestFile = string.Format("appmanifest_{0}.acf", appId);
            foreach (string dir in GetSteamAppsDirectories())
            {
                string fullPath = Path.Combine(dir, manifestFile);
                if (File.Exists(fullPath))
                {
                    string acf = File.ReadAllText(fullPath, Encoding.UTF8);
                    Match m = Regex.Match(acf, @"""name""\s+""([^""]+)""", RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        return m.Groups[1].Value;
                    }
                }
            }

            return null;
        }

        public static string ResolveSteamGameTitle(int appId)
        {
            lock (_cacheLock)
            {
                if (_titleCache.ContainsKey(appId))
                {
                    return _titleCache[appId];
                }
            }

            // 1. Check saved mappings
            var mappings = DataStore.LoadMappings();
            var m = mappings.Find(x => x.AppId == appId);
            if (m != null && !string.IsNullOrEmpty(m.SteamTitle) && !m.SteamTitle.StartsWith("Steam App "))
            {
                lock (_cacheLock) { _titleCache[appId] = m.SteamTitle; }
                return m.SteamTitle;
            }

            // 2. Check Registry HKCU\Software\Valve\Steam\Apps\<AppID> -> Name
            try
            {
                using (RegistryKey appKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\Apps\" + appId))
                {
                    if (appKey != null)
                    {
                        string regName = appKey.GetValue("Name") as string;
                        if (!string.IsNullOrWhiteSpace(regName))
                        {
                            string trimmed = regName.Trim();
                            lock (_cacheLock) { _titleCache[appId] = trimmed; }
                            return trimmed;
                        }
                    }
                }
            }
            catch { }

            // 3. Check Steam appmanifest_<AppID>.acf across all Steam library folders
            try
            {
                string acfTitle = FindTitleFromSteamManifests(appId);
                if (!string.IsNullOrWhiteSpace(acfTitle))
                {
                    string trimmed = acfTitle.Trim();
                    lock (_cacheLock) { _titleCache[appId] = trimmed; }
                    return trimmed;
                }
            }
            catch { }

            // 4. Fallback to Steam Store API
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://store.steampowered.com/api/appdetails?appids=" + appId);
                req.UserAgent = "Mozilla/5.0";
                req.Timeout = 8000;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string json = sr.ReadToEnd();
                    Match nameMatch = Regex.Match(json, @"""name""\s*:\s*""((?:\\.|[^""\\])*)""");
                    if (nameMatch.Success)
                    {
                        string rawName = Regex.Unescape(nameMatch.Groups[1].Value).Trim();
                        if (!string.IsNullOrEmpty(rawName))
                        {
                            lock (_cacheLock) { _titleCache[appId] = rawName; }
                            return rawName;
                        }
                    }
                }
            }
            catch { }

            return "Steam App " + appId;
        }
    }
}
