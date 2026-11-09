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
        private static bool _appInfoLoadedIntoCache = false;

        public static string GetSteamRootDirectory()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key != null)
                    {
                        string steamPath = key.GetValue("SteamPath") as string;
                        if (!string.IsNullOrEmpty(steamPath))
                        {
                            steamPath = steamPath.Replace('/', '\\');
                            if (Directory.Exists(steamPath)) return steamPath;
                        }
                    }
                }
            }
            catch { }

            string defaultPath = @"C:\Program Files (x86)\Steam";
            if (Directory.Exists(defaultPath)) return defaultPath;
            return null;
        }

        public static uint GetActiveSteamAccountId(string steamRoot = null)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("ActiveUser");
                        if (val != null)
                        {
                            uint activeUser = Convert.ToUInt32(val);
                            if (activeUser > 0) return activeUser;
                        }
                    }
                }
            }
            catch { }

            try
            {
                if (string.IsNullOrEmpty(steamRoot)) steamRoot = GetSteamRootDirectory();
                if (!string.IsNullOrEmpty(steamRoot))
                {
                    string userdataDir = Path.Combine(steamRoot, "userdata");
                    if (Directory.Exists(userdataDir))
                    {
                        string[] dirs = Directory.GetDirectories(userdataDir);
                        DateTime newest = DateTime.MinValue;
                        uint bestId = 0;
                        foreach (string dir in dirs)
                        {
                            string folderName = Path.GetFileName(dir);
                            uint uid;
                            if (uint.TryParse(folderName, out uid) && uid > 0)
                            {
                                string lcPath = Path.Combine(dir, @"config\localconfig.vdf");
                                DateTime wt = File.Exists(lcPath) ? File.GetLastWriteTimeUtc(lcPath) : Directory.GetLastWriteTimeUtc(dir);
                                if (wt > newest)
                                {
                                    newest = wt;
                                    bestId = uid;
                                }
                            }
                        }
                        if (bestId > 0) return bestId;
                    }
                }
            }
            catch { }

            return 0;
        }

        public static List<string> GetSteamAppsDirectories()
        {
            var libraryDirs = new List<string>();
            try
            {
                string steamPath = GetSteamRootDirectory();
                if (string.IsNullOrEmpty(steamPath)) return libraryDirs;

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

        private static readonly Regex NonGameDemoTestRegex = new Regex(
            @"(?:\bPublic\s+Test(?:\s+Server)?\b|\bTest\s+Server\b|\bExperimental\s+Server\b|\bClosed\s+Beta\b|\bOpen\s+Beta\b|\bBeta\s+Test\b|\s+Beta$|\bPlaytest\b|\b1-Shot\s+Demo\b|\bRaccoon\s+City\s+Demo\b|\s+Demo$|\(Demo\))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsNonGameTitle(int appId, string name)
        {
            if (appId <= 0 || appId == 480) return true; // 480 = Spacewar (Steamworks SDK test app)
            if (string.IsNullOrWhiteSpace(name)) return true;
            if (name.IndexOf("Proton ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Steamworks Common", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Dedicated Server", StringComparison.OrdinalIgnoreCase) >= 0 ||
                NonGameDemoTestRegex.IsMatch(name))
            {
                return true;
            }
            return false;
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
                                if (IsNonGameTitle(appId, name)) continue;

                                if (!seenIds.Contains(appId))
                                {
                                    seenIds.Add(appId);
                                    games.Add(new InstalledSteamGame
                                    {
                                        AppId = appId,
                                        Title = name,
                                        IsInstalled = true
                                    });
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

        /// <summary>
        /// Enumerates all owned Steam games on the local machine (both installed and uninstalled)
        /// by reading Steam's local licensecache, packageinfo.vdf, appinfo.vdf, localconfig.vdf, and appmanifest_*.acf.
        /// Excludes DLCs, tools, soundtracks, demos, and family-shared games owned by other accounts.
        /// </summary>
        public static List<InstalledSteamGame> GetAllOwnedSteamGames(List<int> ignoredAppIds = null, bool includeHidden = false)
        {
            var installedList = GetInstalledSteamGames(ignoredAppIds);
            var installedMap = new Dictionary<int, InstalledSteamGame>();
            foreach (var g in installedList)
            {
                installedMap[g.AppId] = g;
            }

            string steamRoot = GetSteamRootDirectory();
            if (string.IsNullOrEmpty(steamRoot))
            {
                return installedList;
            }

            uint accountId = GetActiveSteamAccountId(steamRoot);
            HashSet<uint> hiddenApps = ReadHiddenSteamApps(steamRoot, accountId);
            Dictionary<uint, AppInfoEntry> appInfoMap = ReadAppInfoVdf(Path.Combine(steamRoot, @"appcache\appinfo.vdf"));

            // Cache all game titles for fast offline resolution
            lock (_cacheLock)
            {
                foreach (var kv in appInfoMap)
                {
                    if (!string.IsNullOrEmpty(kv.Value.Name) && kv.Key <= int.MaxValue)
                    {
                        _titleCache[(int)kv.Key] = kv.Value.Name;
                    }
                }
                _appInfoLoadedIntoCache = true;
            }

            HashSet<uint> ownedAppIds = new HashSet<uint>();

            // 1. Primary source: licensecache (filtered to owner_id == accountId) -> packageinfo.vdf
            if (accountId > 0)
            {
                string licenseCachePath = Path.Combine(steamRoot, string.Format(@"userdata\{0}\config\licensecache", accountId));
                string packageInfoPath = Path.Combine(steamRoot, @"appcache\packageinfo.vdf");
                if (File.Exists(licenseCachePath) && File.Exists(packageInfoPath))
                {
                    HashSet<uint> myPackages = ReadOwnedPackagesFromLicenseCache(licenseCachePath, accountId);
                    if (myPackages.Count > 0)
                    {
                        HashSet<uint> pkgApps = ReadAppIdsForPackages(packageInfoPath, myPackages);
                        foreach (uint aid in pkgApps) ownedAppIds.Add(aid);
                    }
                }

                // 2. Fallback if licensecache/packageinfo yielded nothing: read localconfig.vdf Software/Valve/Steam/apps
                if (ownedAppIds.Count == 0)
                {
                    string localConfigPath = Path.Combine(steamRoot, string.Format(@"userdata\{0}\config\localconfig.vdf", accountId));
                    if (File.Exists(localConfigPath))
                    {
                        foreach (uint aid in ReadAppIdsFromLocalConfig(localConfigPath))
                        {
                            ownedAppIds.Add(aid);
                        }
                    }
                }
            }

            // 3. Always union installed games
            foreach (int instId in installedMap.Keys)
            {
                if (instId > 0) ownedAppIds.Add((uint)instId);
            }

            var results = new List<InstalledSteamGame>();
            var addedIds = new HashSet<int>();

            foreach (uint rawId in ownedAppIds)
            {
                if (rawId == 0 || rawId > int.MaxValue) continue;
                int appId = (int)rawId;

                if (ignoredAppIds != null && ignoredAppIds.Contains(appId)) continue;
                bool isHidden = hiddenApps.Contains(rawId);
                if (isHidden && !includeHidden) continue;

                string title = null;
                bool isGame = false;

                AppInfoEntry meta;
                if (appInfoMap.TryGetValue(rawId, out meta))
                {
                    if (string.Equals(meta.Type, "game", StringComparison.OrdinalIgnoreCase))
                    {
                        isGame = true;
                        title = meta.Name;
                    }
                }
                else if (installedMap.ContainsKey(appId))
                {
                    isGame = true;
                    title = installedMap[appId].Title;
                }

                if (!isGame || string.IsNullOrWhiteSpace(title)) continue;
                if (IsNonGameTitle(appId, title)) continue;

                if (!addedIds.Contains(appId))
                {
                    addedIds.Add(appId);
                    results.Add(new InstalledSteamGame
                    {
                        AppId = appId,
                        Title = title.Trim(),
                        IsInstalled = installedMap.ContainsKey(appId),
                        IsHidden = isHidden
                    });
                }
            }

            results.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            return results;
        }

        private class AppInfoEntry
        {
            public uint AppId;
            public string Name;
            public string Type;
        }

        private static Dictionary<uint, AppInfoEntry> ReadAppInfoVdf(string path)
        {
            var result = new Dictionary<uint, AppInfoEntry>();
            try
            {
                if (!File.Exists(path)) return result;
                byte[] b = File.ReadAllBytes(path);
                if (b.Length < 16) return result;

                uint magic = BitConverter.ToUInt32(b, 0);
                if (magic != 0x07564429 && magic != 0x07564428 && magic != 0x07564427) return result;

                bool hasStringTable = (magic == 0x07564429);
                string[] strings = null;
                int pos = 8;

                if (hasStringTable)
                {
                    long strOffset = BitConverter.ToInt64(b, 8);
                    pos = 16;
                    if (strOffset > 0 && strOffset + 4 <= b.Length)
                    {
                        int sPos = (int)strOffset;
                        uint count = BitConverter.ToUInt32(b, sPos);
                        sPos += 4;
                        strings = new string[count];
                        for (int i = 0; i < count && sPos < b.Length; i++)
                        {
                            int end = sPos;
                            while (end < b.Length && b[end] != 0) end++;
                            strings[i] = Encoding.UTF8.GetString(b, sPos, end - sPos);
                            sPos = end + 1;
                        }
                    }
                }

                while (pos + 8 <= b.Length)
                {
                    uint appId = BitConverter.ToUInt32(b, pos);
                    if (appId == 0) break;
                    uint size = BitConverter.ToUInt32(b, pos + 4);
                    int entryEnd = pos + 8 + (int)size;
                    if (entryEnd > b.Length || entryEnd <= pos) break;

                    int vdfPos = pos + (magic >= 0x07564428 ? 68 : 48);
                    var meta = new AppInfoEntry { AppId = appId };
                    try
                    {
                        ParseAppInfoCommon(b, ref vdfPos, entryEnd, strings, meta);
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(meta.Name))
                    {
                        result[appId] = meta;
                    }

                    pos = entryEnd;
                }
            }
            catch { }
            return result;
        }

        private static string ReadVdfKey(byte[] b, ref int pos, int end, string[] strings)
        {
            if (strings != null)
            {
                if (pos + 4 > end) { pos = end; return ""; }
                uint idx = BitConverter.ToUInt32(b, pos);
                pos += 4;
                return idx < strings.Length ? strings[idx] : "";
            }
            else
            {
                int sEnd = pos;
                while (sEnd < end && b[sEnd] != 0) sEnd++;
                string s = Encoding.UTF8.GetString(b, pos, sEnd - pos);
                pos = sEnd + 1;
                return s;
            }
        }

        private static string ReadVdfString(byte[] b, ref int pos, int end)
        {
            int sEnd = pos;
            while (sEnd < end && b[sEnd] != 0) sEnd++;
            string s = Encoding.UTF8.GetString(b, pos, sEnd - pos);
            pos = sEnd + 1;
            return s;
        }

        private static void ParseAppInfoCommon(byte[] b, ref int pos, int end, string[] strings, AppInfoEntry meta)
        {
            int depth = 0;
            bool inCommon = false;
            int commonDepth = -1;

            while (pos < end)
            {
                byte type = b[pos++];
                if (type == 8)
                {
                    if (inCommon && depth == commonDepth) inCommon = false;
                    depth--;
                    if (depth < 0) break;
                    continue;
                }

                string key = ReadVdfKey(b, ref pos, end, strings);
                if (type == 0)
                {
                    depth++;
                    if (depth == 2 && string.Equals(key, "common", StringComparison.OrdinalIgnoreCase))
                    {
                        inCommon = true;
                        commonDepth = depth;
                    }
                }
                else if (type == 1)
                {
                    string val = ReadVdfString(b, ref pos, end);
                    if (inCommon && depth == commonDepth)
                    {
                        if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(meta.Name))
                            meta.Name = val;
                        else if (string.Equals(key, "type", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(meta.Type))
                            meta.Type = val;
                    }
                    if (!string.IsNullOrEmpty(meta.Name) && !string.IsNullOrEmpty(meta.Type)) return;
                }
                else if (type == 2 || type == 3 || type == 4 || type == 6)
                {
                    pos += 4;
                }
                else if (type == 7 || type == 10)
                {
                    pos += 8;
                }
                else if (type == 5)
                {
                    while (pos + 1 < end && (b[pos] != 0 || b[pos + 1] != 0)) pos += 2;
                    pos += 2;
                }
                else
                {
                    break;
                }
            }
        }

        private class ValveUniformRandomStream
        {
            private int _idum;
            private int _iy;
            private readonly int[] _iv = new int[32];

            public void SetSeed(int seed)
            {
                _idum = (seed < 0) ? seed : -seed;
                _iy = 0;
            }

            private int GenerateRandomNumber()
            {
                int j, k;
                if (_idum <= 0 || _iy == 0)
                {
                    _idum = (-_idum < 1) ? 1 : -_idum;
                    for (j = 39; j >= 0; j--)
                    {
                        k = _idum / 127773;
                        _idum = 16807 * (_idum - k * 127773) - 2836 * k;
                        if (_idum < 0) _idum += 2147483647;
                        if (j < 32) _iv[j] = _idum;
                    }
                    _iy = _iv[0];
                }
                k = _idum / 127773;
                _idum = 16807 * (_idum - k * 127773) - 2836 * k;
                if (_idum < 0) _idum += 2147483647;
                j = _iy / (1 + 2147483646 / 32);
                _iy = _iv[j];
                _iv[j] = _idum;
                return _iy;
            }

            public int RandomInt(int low, int high)
            {
                uint x = (uint)(high - low + 1);
                if (x <= 1 || 0x7FFFFFFFu < x - 1) return low;
                uint maxAcceptable = 0x7FFFFFFFu - ((0x7FFFFFFFu + 1) % x);
                uint n;
                do
                {
                    n = (uint)GenerateRandomNumber();
                } while (n > maxAcceptable);
                return low + (int)(n % x);
            }
        }

        private static ulong ReadProtoVarint(byte[] b, ref int pos, int end)
        {
            ulong res = 0;
            int shift = 0;
            while (pos < end)
            {
                byte v = b[pos++];
                res |= (ulong)(v & 0x7F) << shift;
                if ((v & 0x80) == 0) break;
                shift += 7;
            }
            return res;
        }

        private static HashSet<uint> ReadOwnedPackagesFromLicenseCache(string licenseCachePath, uint accountId)
        {
            var packages = new HashSet<uint>();
            try
            {
                byte[] raw = File.ReadAllBytes(licenseCachePath);
                if (raw.Length < 4) return packages;

                var rng = new ValveUniformRandomStream();
                rng.SetSeed((int)accountId);

                byte[] b = new byte[raw.Length];
                for (int i = 0; i < b.Length; i++)
                {
                    b[i] = (byte)(raw[i] ^ (byte)rng.RandomInt(32, 126));
                }

                int pos = 0;
                while (pos < b.Length)
                {
                    ulong tag = ReadProtoVarint(b, ref pos, b.Length);
                    int fieldNum = (int)(tag >> 3);
                    int wireType = (int)(tag & 7);

                    if (wireType == 0)
                    {
                        ReadProtoVarint(b, ref pos, b.Length);
                    }
                    else if (wireType == 2)
                    {
                        int len = (int)ReadProtoVarint(b, ref pos, b.Length);
                        int end = pos + len;
                        if (end > b.Length || len < 0) break;

                        if (fieldNum == 2)
                        {
                            uint pkgId = 0;
                            ulong ownerId = 0;
                            while (pos < end)
                            {
                                ulong lTag = ReadProtoVarint(b, ref pos, end);
                                int lField = (int)(lTag >> 3);
                                int lWire = (int)(lTag & 7);
                                if (lWire == 0)
                                {
                                    ulong val = ReadProtoVarint(b, ref pos, end);
                                    if (lField == 1) pkgId = (uint)val;
                                    else if (lField == 12) ownerId = val;
                                }
                                else if (lWire == 5)
                                {
                                    pos += 4;
                                }
                                else if (lWire == 1)
                                {
                                    pos += 8;
                                }
                                else if (lWire == 2)
                                {
                                    int sLen = (int)ReadProtoVarint(b, ref pos, end);
                                    pos += sLen;
                                }
                                else
                                {
                                    break;
                                }
                            }
                            if (pkgId != 0 && (ownerId == accountId || ownerId == 0))
                            {
                                packages.Add(pkgId);
                            }
                        }
                        pos = end;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch { }
            return packages;
        }

        private static HashSet<uint> ReadAppIdsForPackages(string packageInfoPath, HashSet<uint> ownedPackages)
        {
            var appIds = new HashSet<uint>();
            try
            {
                byte[] pb = File.ReadAllBytes(packageInfoPath);
                if (pb.Length < 8) return appIds;

                uint pMagic = BitConverter.ToUInt32(pb, 0);
                if (pMagic != 0x06565528 && pMagic != 0x06565527) return appIds;

                int pPos = 8;
                while (pPos + 4 <= pb.Length)
                {
                    uint pkgId = BitConverter.ToUInt32(pb, pPos);
                    if (pkgId == 0xFFFFFFFF) break;

                    pPos += (pMagic == 0x06565528) ? 36 : 28;
                    bool isOwnedPkg = ownedPackages.Contains(pkgId);

                    int depth = 0;
                    bool inAppIds = false;
                    int appIdsDepth = -1;

                    while (pPos < pb.Length)
                    {
                        byte type = pb[pPos++];
                        if (type == 8)
                        {
                            if (inAppIds && depth == appIdsDepth) inAppIds = false;
                            depth--;
                            if (depth < 0) break;
                            continue;
                        }

                        int end = pPos;
                        while (end < pb.Length && pb[end] != 0) end++;
                        string key = Encoding.UTF8.GetString(pb, pPos, end - pPos);
                        pPos = end + 1;

                        if (type == 0)
                        {
                            depth++;
                            if (isOwnedPkg && string.Equals(key, "appids", StringComparison.OrdinalIgnoreCase))
                            {
                                inAppIds = true;
                                appIdsDepth = depth;
                            }
                        }
                        else if (type == 1)
                        {
                            while (pPos < pb.Length && pb[pPos] != 0) pPos++;
                            pPos++;
                        }
                        else if (type == 2)
                        {
                            uint val = BitConverter.ToUInt32(pb, pPos);
                            pPos += 4;
                            if (isOwnedPkg && inAppIds && depth == appIdsDepth)
                            {
                                appIds.Add(val);
                            }
                        }
                        else if (type == 3 || type == 4 || type == 6)
                        {
                            pPos += 4;
                        }
                        else if (type == 7 || type == 10)
                        {
                            pPos += 8;
                        }
                        else if (type == 5)
                        {
                            while (pPos + 1 < pb.Length && (pb[pPos] != 0 || pb[pPos + 1] != 0)) pPos += 2;
                            pPos += 2;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
            catch { }
            return appIds;
        }

        private static HashSet<uint> ReadHiddenSteamApps(string steamRoot, uint accountId)
        {
            var hidden = new HashSet<uint>();
            try
            {
                if (accountId == 0) return hidden;
                string csPath = Path.Combine(steamRoot, string.Format(@"userdata\{0}\config\cloudstorage\cloud-storage-namespace-1.json", accountId));
                if (!File.Exists(csPath)) return hidden;

                string json = File.ReadAllText(csPath, Encoding.UTF8);
                Match m = Regex.Match(json, @"""user-collections\.hidden""[\s\S]*?\\""added\\"":\[([\d,\s]*)\]");
                if (m.Success)
                {
                    string[] parts = m.Groups[1].Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string p in parts)
                    {
                        uint id;
                        if (uint.TryParse(p.Trim(), out id) && id > 0)
                        {
                            hidden.Add(id);
                        }
                    }
                }
            }
            catch { }
            return hidden;
        }

        private static HashSet<uint> ReadAppIdsFromLocalConfig(string localConfigPath)
        {
            var ids = new HashSet<uint>();
            try
            {
                string content = File.ReadAllText(localConfigPath, Encoding.UTF8);
                int appsIdx = content.IndexOf("\"apps\"", StringComparison.OrdinalIgnoreCase);
                if (appsIdx < 0) return ids;

                foreach (Match m in Regex.Matches(content.Substring(appsIdx), @"(?m)^\t{4,5}""(\d+)"""))
                {
                    uint id;
                    if (uint.TryParse(m.Groups[1].Value, out id) && id > 0)
                    {
                        ids.Add(id);
                    }
                }
            }
            catch { }
            return ids;
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

            // 4. Check local appinfo.vdf cache
            try
            {
                if (!_appInfoLoadedIntoCache)
                {
                    string steamRoot = GetSteamRootDirectory();
                    if (!string.IsNullOrEmpty(steamRoot))
                    {
                        var appInfo = ReadAppInfoVdf(Path.Combine(steamRoot, @"appcache\appinfo.vdf"));
                        lock (_cacheLock)
                        {
                            foreach (var kv in appInfo)
                            {
                                if (!string.IsNullOrEmpty(kv.Value.Name) && kv.Key <= int.MaxValue)
                                {
                                    _titleCache[(int)kv.Key] = kv.Value.Name;
                                }
                            }
                            _appInfoLoadedIntoCache = true;
                        }
                        lock (_cacheLock)
                        {
                            if (_titleCache.ContainsKey(appId)) return _titleCache[appId];
                        }
                    }
                }
            }
            catch { }

            // 5. Fallback to Steam Store API
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
