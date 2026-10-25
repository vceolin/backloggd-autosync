using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace BackloggdAutoSync
{
    public class InstalledSteamGame
    {
        public int AppId { get; set; }
        public string Title { get; set; }

        public override string ToString()
        {
            return string.Format("{0} (AppID: {1})", Title, AppId);
        }
    }

    public class SteamTracker
    {
        private int _currentAppId = 0;
        private string _currentTitle = "";
        private DateTime _sessionStart;
        private string _sessionDateStr = "";
        private DateTime _lastCheckpointTime = DateTime.MinValue;
        private readonly Dictionary<int, string> _titleCache = new Dictionary<int, string>();
        private readonly object _syncLock = new object();

        public int CurrentAppId { get { return _currentAppId; } }
        public string CurrentTitle { get { return _currentTitle; } }
        public DateTime SessionStart { get { return _sessionStart; } }
        public int CurrentElapsedSeconds
        {
            get
            {
                if (_currentAppId <= 0) return 0;
                return Math.Max(0, (int)(DateTime.Now - _sessionStart).TotalSeconds);
            }
        }

        public event Action<int, string> OnGameStarted;
        public event Action<SessionEntry> OnSessionCompleted;
        public event Action<SessionEntry> OnSessionSynced;
        public event Action<SessionEntry, string, List<GameSearchResult>> OnUnmappedGamePromptRequested;

        public void RecoverCrashCheckpoint(AppSettings settings)
        {
            var cp = DataStore.LoadCheckpoint();
            if (cp != null && cp.AppId > 0)
            {
                DataStore.SaveCheckpoint(null);
                if (cp.ElapsedSeconds >= settings.MinSessionSeconds)
                {
                    DataStore.Log(string.Format("Recovered interrupted session from checkpoint: {0} (AppID {1}), {2}s on {3}",
                        cp.GameTitle, cp.AppId, cp.ElapsedSeconds, cp.DateStr));
                    FinalizeAndRecordSession(
                        cp.AppId,
                        cp.GameTitle,
                        cp.DateStr,
                        cp.StartTimeIso,
                        cp.LastHeartbeatIso ?? DateTime.Now.ToString("o"),
                        cp.ElapsedSeconds,
                        settings);
                }
            }
        }

        public void Poll(AppSettings settings)
        {
            int runningAppId = GetRunningSteamAppId();
            if (runningAppId > 0 && settings.IgnoredAppIds != null && settings.IgnoredAppIds.Contains(runningAppId))
            {
                runningAppId = 0;
            }

            DateTime now = DateTime.Now;
            string todayStr = now.ToString("yyyy-MM-dd");

            // Handle midnight split if a game is currently running across midnight
            if (_currentAppId > 0 && runningAppId == _currentAppId && settings.SplitAtMidnight && _sessionDateStr != todayStr)
            {
                int elapsedBeforeMidnight = Math.Max(0, (int)(now - _sessionStart).TotalSeconds);
                string prevDateStr = _sessionDateStr;
                string prevStartIso = _sessionStart.ToString("o");
                string midnightIso = now.Date.ToString("o");

                DataStore.Log(string.Format("Midnight crossover for {0} (AppID {1}): splitting {2}s onto {3}",
                    _currentTitle, _currentAppId, elapsedBeforeMidnight, prevDateStr));

                _sessionStart = now;
                _sessionDateStr = todayStr;
                SaveCurrentCheckpoint();

                if (elapsedBeforeMidnight >= settings.MinSessionSeconds)
                {
                    FinalizeAndRecordSession(_currentAppId, _currentTitle, prevDateStr, prevStartIso, midnightIso, elapsedBeforeMidnight, settings);
                }
                return;
            }

            // If a running game stopped or switched to another game
            if (_currentAppId > 0 && runningAppId != _currentAppId)
            {
                int endedAppId = _currentAppId;
                string endedTitle = _currentTitle;
                string endedDateStr = _sessionDateStr;
                string startIso = _sessionStart.ToString("o");
                string endIso = now.ToString("o");
                int elapsedSec = Math.Max(0, (int)(now - _sessionStart).TotalSeconds);

                _currentAppId = 0;
                _currentTitle = "";
                _sessionDateStr = "";
                DataStore.SaveCheckpoint(null);

                DataStore.Log(string.Format("Game exited: {0} (AppID {1}) after {2}s", endedTitle, endedAppId, elapsedSec));

                if (elapsedSec >= settings.MinSessionSeconds)
                {
                    FinalizeAndRecordSession(endedAppId, endedTitle, endedDateStr, startIso, endIso, elapsedSec, settings);
                }
                else
                {
                    DataStore.Log(string.Format("Ignored short session ({0}s < {1}s threshold) for {2}",
                        elapsedSec, settings.MinSessionSeconds, endedTitle));
                }
            }

            // If a new game started
            if (runningAppId > 0 && _currentAppId == 0)
            {
                _currentAppId = runningAppId;
                _sessionStart = now;
                _sessionDateStr = todayStr;
                _currentTitle = ResolveSteamGameTitle(runningAppId);
                _lastCheckpointTime = now;
                SaveCurrentCheckpoint();

                DataStore.Log(string.Format("Game launched: {0} (AppID {1})", _currentTitle, _currentAppId));
                if (OnGameStarted != null)
                {
                    OnGameStarted(_currentAppId, _currentTitle);
                }

                // Pre-resolve mapping in background when game starts so it's ready before game exits
                int launchedAppId = _currentAppId;
                string launchedTitle = _currentTitle;
                ThreadPool.QueueUserWorkItem(_ => TryPreResolveMappingOnLaunch(launchedAppId, launchedTitle));
            }

            // Periodic checkpoint every 30 seconds while playing
            if (_currentAppId > 0 && (now - _lastCheckpointTime).TotalSeconds >= 30)
            {
                _lastCheckpointTime = now;
                SaveCurrentCheckpoint();
            }
        }

        private void TryPreResolveMappingOnLaunch(int appId, string rawTitle)
        {
            try
            {
                var mappings = DataStore.LoadMappings();
                if (mappings.Exists(m => m.AppId == appId && !string.IsNullOrEmpty(m.BackloggdGameId)))
                {
                    return;
                }

                var settings = DataStore.LoadSettings();
                if (string.IsNullOrWhiteSpace(settings.SessionCookie)) return;

                string cleanTitle = BackloggdClient.CleanGameTitle(rawTitle);
                var results = BackloggdClient.SearchGame(cleanTitle, settings);
                if (results.Count == 0 && cleanTitle != rawTitle)
                {
                    results = BackloggdClient.SearchGame(rawTitle, settings);
                }

                var best = BackloggdClient.SelectBestMatch(cleanTitle, results);
                if (best != null)
                {
                    mappings = DataStore.LoadMappings();
                    if (!mappings.Exists(m => m.AppId == appId))
                    {
                        mappings.Add(new GameMapping
                        {
                            AppId = appId,
                            BackloggdGameId = best.GameId,
                            SteamTitle = rawTitle,
                            BackloggdTitle = best.Title,
                            ReleaseYear = best.ReleaseYear,
                            ManualOverride = false,
                            LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        });
                        DataStore.SaveMappings(mappings);
                        DataStore.Log(string.Format("Pre-matched on launch: {0} (AppID {1}) -> {2} (ID {3})",
                            rawTitle, appId, best.Title, best.GameId));
                    }
                }
            }
            catch
            {
                // Ignore background pre-resolve errors
            }
        }

        private void SaveCurrentCheckpoint()
        {
            if (_currentAppId <= 0) return;
            var cp = new ActiveSessionCheckpoint
            {
                AppId = _currentAppId,
                GameTitle = _currentTitle,
                DateStr = _sessionDateStr,
                StartTimeIso = _sessionStart.ToString("o"),
                LastHeartbeatIso = DateTime.Now.ToString("o"),
                ElapsedSeconds = CurrentElapsedSeconds
            };
            DataStore.SaveCheckpoint(cp);
        }

        private void FinalizeAndRecordSession(int appId, string title, string dateStr, string startIso, string endIso, int durationSec, AppSettings settings)
        {
            var mappings = DataStore.LoadMappings();
            var existingMap = mappings.Find(m => m.AppId == appId);

            var entry = new SessionEntry
            {
                Id = string.Format("sess_{0}_{1}", DateTime.Now.Ticks, appId),
                Platform = "Steam",
                AppId = appId,
                GameTitle = existingMap != null && !string.IsNullOrEmpty(existingMap.BackloggdTitle) ? existingMap.BackloggdTitle : title,
                BackloggdGameId = existingMap != null ? existingMap.BackloggdGameId : null,
                DateStr = dateStr,
                StartTimeIso = startIso,
                EndTimeIso = endIso,
                DurationSeconds = durationSec,
                SetExactDayPlaytime = false,
                Synced = false,
                SyncError = null,
                RetryCount = 0
            };

            var sessions = DataStore.LoadSessions();
            sessions.Insert(0, entry);
            DataStore.SaveSessions(sessions);

            if (OnSessionCompleted != null)
            {
                OnSessionCompleted(entry);
            }

            if (settings.AutoSyncOnExit)
            {
                ThreadPool.QueueUserWorkItem(_ => SyncSessionEntryAsync(entry.Id, true));
            }
        }

        public void SyncSessionEntryAsync(string sessionId, bool allowInteractivePrompt = false)
        {
            lock (_syncLock)
            {
                var settings = DataStore.LoadSettings();
                var sessions = DataStore.LoadSessions();
                var entry = sessions.Find(s => s.Id == sessionId);
                if (entry == null || entry.Synced) return;

                if (string.IsNullOrWhiteSpace(settings.SessionCookie))
                {
                    entry.SyncError = "No Backloggd session cookie configured in Settings.";
                    DataStore.SaveSessions(sessions);
                    if (OnSessionSynced != null) OnSessionSynced(entry);
                    return;
                }

                try
                {
                    var mappings = DataStore.LoadMappings();
                    var map = entry.AppId != 0 ? mappings.Find(m => m.AppId == entry.AppId) : null;

                    if ((map == null || string.IsNullOrEmpty(map.BackloggdGameId)) && string.IsNullOrEmpty(entry.BackloggdGameId))
                    {
                        string rawTitle = entry.AppId > 0 ? ResolveSteamGameTitle(entry.AppId) : (entry.GameTitle ?? "");
                        string cleanTitle = BackloggdClient.CleanGameTitle(rawTitle);

                        var searchResults = BackloggdClient.SearchGame(cleanTitle, settings);
                        if (searchResults.Count == 0 && cleanTitle != rawTitle)
                        {
                            searchResults = BackloggdClient.SearchGame(rawTitle, settings);
                        }

                        var best = BackloggdClient.SelectBestMatch(cleanTitle, searchResults);
                        if (best == null)
                        {
                            // Prompt user interactively with auto-suggestions if available
                            if (allowInteractivePrompt && OnUnmappedGamePromptRequested != null)
                            {
                                entry.SyncError = "Waiting for game selection...";
                                DataStore.SaveSessions(sessions);
                                OnUnmappedGamePromptRequested(entry, rawTitle, searchResults);
                                return;
                            }

                            if (searchResults.Count == 0)
                            {
                                throw new Exception(string.Format("No match found for \"{0}\". Double-click this session to pick a Backloggd game.", rawTitle));
                            }
                            else
                            {
                                var topNames = new List<string>();
                                for (int i = 0; i < Math.Min(3, searchResults.Count); i++)
                                {
                                    topNames.Add("\"" + searchResults[i].Title + "\"");
                                }
                                throw new Exception(string.Format("Multiple matches ({0}). Double-click this session to choose.", string.Join(", ", topNames.ToArray())));
                            }
                        }

                        map = new GameMapping
                        {
                            AppId = entry.AppId,
                            SwitchApplicationId = entry.SwitchApplicationId,
                            BackloggdGameId = best.GameId,
                            SteamTitle = rawTitle,
                            BackloggdTitle = best.Title,
                            ReleaseYear = best.ReleaseYear,
                            ManualOverride = false,
                            LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        };
                        if (entry.AppId != 0)
                        {
                            mappings.RemoveAll(m => m.AppId == entry.AppId);
                            mappings.Add(map);
                            DataStore.SaveMappings(mappings);
                        }
                    }

                    if (map != null)
                    {
                        entry.BackloggdGameId = map.BackloggdGameId;
                        if (!string.IsNullOrEmpty(map.BackloggdTitle))
                        {
                            entry.GameTitle = map.BackloggdTitle;
                        }
                    }

                    int defaultPlatformId = string.Equals(entry.Platform, "Switch", StringComparison.OrdinalIgnoreCase) ? 130 : 6;
                    SyncResult res = BackloggdClient.SyncSession(
                        entry.BackloggdGameId,
                        entry.DurationSeconds,
                        entry.DateStr,
                        settings,
                        null,
                        defaultPlatformId,
                        entry.SetExactDayPlaytime);

                    if (res.Success)
                    {
                        entry.Synced = true;
                        entry.SyncError = null;
                        entry.AccumulatedOnBackloggd = res.Accumulated;
                        entry.TotalDayMinutesOnBackloggd = res.FinalDayHours * 60 + res.FinalDayMinutes;
                        DataStore.Log(string.Format("Synced session to Backloggd: {0} ({1}) -> {2}h {3}m total on {4}",
                            entry.GameTitle, entry.DateStr, res.FinalDayHours, res.FinalDayMinutes, entry.DateStr));
                    }
                    else
                    {
                        entry.Synced = false;
                        entry.RetryCount += 1;
                        entry.SyncError = res.Error;
                        DataStore.Log(string.Format("Failed to sync {0}: {1}", entry.GameTitle, res.Error));
                    }
                }
                catch (Exception ex)
                {
                    entry.Synced = false;
                    entry.RetryCount += 1;
                    entry.SyncError = ex.Message;
                    DataStore.Log(string.Format("Error syncing session {0}: {1}", entry.GameTitle, ex.Message));
                }

                DataStore.SaveSessions(sessions);
                if (OnSessionSynced != null) OnSessionSynced(entry);
            }
        }

        public string FetchAndSyncNintendoSwitchSummaries(bool allowInteractivePrompt, out int newOrUpdatedCount, bool fetchEntireHistory = false)
        {
            newOrUpdatedCount = 0;
            var settings = DataStore.LoadSettings();
            if (string.IsNullOrWhiteSpace(settings.NintendoSessionToken))
            {
                return "No Nintendo Account connected yet.";
            }

            string idToken, accessToken, nickname, accountId;
            NintendoSwitchClient.RefreshTokensAndProfile(settings.NintendoSessionToken, out idToken, out accessToken, out nickname, out accountId);

            if (!string.IsNullOrEmpty(nickname) && settings.NintendoAccountNickname != nickname)
            {
                settings.NintendoAccountNickname = nickname;
                settings.NintendoAccountId = accountId;
                DataStore.SaveSettings(settings);
            }

            var devices = NintendoSwitchClient.FetchOwnedDevices(idToken);
            if (devices.Count == 0)
            {
                return "No Nintendo Switch consoles linked to Nintendo Switch Parental Controls on this account.";
            }

            var allDailyRecords = new List<NintendoDailyGameRecord>();
            int totalMonthsFetched = 0;
            foreach (var dev in devices)
            {
                if (fetchEntireHistory)
                {
                    int mCount;
                    var histRecs = NintendoSwitchClient.FetchAllHistoricalMonthlySummaries(idToken, dev, out mCount);
                    totalMonthsFetched += mCount;
                    allDailyRecords.AddRange(histRecs);
                }
                else
                {
                    var recs = NintendoSwitchClient.FetchDailySummaries(idToken, dev, settings.NintendoSyncDaysBack);
                    allDailyRecords.AddRange(recs);
                }
            }

            var sessions = DataStore.LoadSessions();
            var mappings = DataStore.LoadMappings();
            var sessionIdsToSync = new List<string>();

            foreach (var rec in allDailyRecords)
            {
                int switchAppId = NintendoSwitchClient.GetStableSwitchAppId(rec.ApplicationId, rec.Title);
                int durationSec = rec.PlayingTimeMinutes * 60;
                if (durationSec < settings.MinSessionSeconds) continue;

                var existingSession = sessions.Find(s =>
                    s.DateStr == rec.DateStr &&
                    (s.AppId == switchAppId || (!string.IsNullOrEmpty(rec.ApplicationId) && s.SwitchApplicationId == rec.ApplicationId)));

                if (existingSession != null)
                {
                    // Update if Nintendo reported more playtime on this date than previously recorded
                    if (durationSec > existingSession.DurationSeconds || !existingSession.Synced)
                    {
                        existingSession.DurationSeconds = Math.Max(existingSession.DurationSeconds, durationSec);
                        existingSession.Platform = "Switch";
                        existingSession.SetExactDayPlaytime = true;
                        existingSession.Synced = false;
                        existingSession.SyncError = null;
                        sessionIdsToSync.Add(existingSession.Id);
                        newOrUpdatedCount++;
                    }
                }
                else
                {
                    var existingMap = mappings.Find(m => m.AppId == switchAppId || (!string.IsNullOrEmpty(rec.ApplicationId) && m.SwitchApplicationId == rec.ApplicationId));
                    var newEntry = new SessionEntry
                    {
                        Id = string.Format("nx_{0}_{1}", rec.DateStr.Replace("-", ""), Math.Abs(switchAppId)),
                        Platform = "Switch",
                        AppId = switchAppId,
                        SwitchApplicationId = rec.ApplicationId,
                        GameTitle = (existingMap != null && !string.IsNullOrEmpty(existingMap.BackloggdTitle)) ? existingMap.BackloggdTitle : rec.Title,
                        BackloggdGameId = existingMap != null ? existingMap.BackloggdGameId : null,
                        DateStr = rec.DateStr,
                        StartTimeIso = rec.DateStr + "T12:00:00",
                        EndTimeIso = rec.DateStr + "T12:00:00",
                        DurationSeconds = durationSec,
                        SetExactDayPlaytime = true,
                        Synced = false
                    };
                    sessions.Insert(0, newEntry);
                    sessionIdsToSync.Add(newEntry.Id);
                    newOrUpdatedCount++;
                }
            }

            if (sessionIdsToSync.Count > 0)
            {
                // Sort sessions newest date first
                sessions.Sort((a, b) => string.Compare(b.DateStr, a.DateStr, StringComparison.Ordinal));
                DataStore.SaveSessions(sessions);

                BatchSyncSessionsByGame(sessionIdsToSync, allowInteractivePrompt);
            }

            if (fetchEntireHistory)
            {
                return string.Format("Full History Sync complete across {0} console(s) ({1} monthly archive(s) + recent daily logs): {2} daily session(s) synced/updated.",
                    devices.Count, totalMonthsFetched, newOrUpdatedCount);
            }

            return string.Format("Checked {0} Switch console(s): {1} daily game session(s) synced/updated.", devices.Count, newOrUpdatedCount);
        }

        public void BatchSyncSessionsByGame(List<string> sessionIds, bool allowInteractivePrompt)
        {
            if (sessionIds == null || sessionIds.Count == 0) return;

            lock (_syncLock)
            {
                var settings = DataStore.LoadSettings();
                var sessions = DataStore.LoadSessions();
                var mappings = DataStore.LoadMappings();

                var idSet = new HashSet<string>(sessionIds);
                var targetEntries = sessions.FindAll(s => idSet.Contains(s.Id) && !s.Synced);
                if (targetEntries.Count == 0) return;

                // Group by AppId so we resolve each game's mapping once and batch-POST all its dates in a single request
                var byAppId = new Dictionary<int, List<SessionEntry>>();
                foreach (var entry in targetEntries)
                {
                    if (!byAppId.ContainsKey(entry.AppId))
                    {
                        byAppId[entry.AppId] = new List<SessionEntry>();
                    }
                    byAppId[entry.AppId].Add(entry);
                }

                bool promptedOnce = false;
                foreach (var kv in byAppId)
                {
                    int appId = kv.Key;
                    List<SessionEntry> group = kv.Value;
                    SessionEntry first = group[0];

                    // If only 1 date for this game, use standard SyncSessionEntryAsync
                    if (group.Count == 1)
                    {
                        SyncSessionEntryAsync(first.Id, allowInteractivePrompt && !promptedOnce);
                        if (!first.Synced) promptedOnce = true;
                        continue;
                    }

                    try
                    {
                        var map = appId != 0 ? mappings.Find(m => m.AppId == appId) : null;
                        if ((map == null || string.IsNullOrEmpty(map.BackloggdGameId)) && string.IsNullOrEmpty(first.BackloggdGameId))
                        {
                            string rawTitle = appId > 0 ? ResolveSteamGameTitle(appId) : (first.GameTitle ?? "");
                            string cleanTitle = BackloggdClient.CleanGameTitle(rawTitle);
                            var searchResults = BackloggdClient.SearchGame(cleanTitle, settings);
                            if (searchResults.Count == 0 && cleanTitle != rawTitle)
                            {
                                searchResults = BackloggdClient.SearchGame(rawTitle, settings);
                            }

                            var best = BackloggdClient.SelectBestMatch(cleanTitle, searchResults);
                            if (best == null)
                            {
                                if (allowInteractivePrompt && !promptedOnce && OnUnmappedGamePromptRequested != null)
                                {
                                    promptedOnce = true;
                                    foreach (var g in group) g.SyncError = "Waiting for game selection...";
                                    DataStore.SaveSessions(sessions);
                                    OnUnmappedGamePromptRequested(first, rawTitle, searchResults);
                                }
                                else
                                {
                                    foreach (var g in group)
                                    {
                                        g.SyncError = string.Format("Unmapped game \"{0}\". Double-click to select on Backloggd.", rawTitle);
                                    }
                                    DataStore.SaveSessions(sessions);
                                }
                                continue;
                            }

                            map = new GameMapping
                            {
                                AppId = appId,
                                SwitchApplicationId = first.SwitchApplicationId,
                                BackloggdGameId = best.GameId,
                                SteamTitle = rawTitle,
                                BackloggdTitle = best.Title,
                                ReleaseYear = best.ReleaseYear,
                                ManualOverride = false,
                                LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            };
                            mappings.RemoveAll(m => m.AppId == appId);
                            mappings.Add(map);
                            DataStore.SaveMappings(mappings);
                        }

                        string gameId = (map != null && !string.IsNullOrEmpty(map.BackloggdGameId)) ? map.BackloggdGameId : first.BackloggdGameId;
                        string resolvedTitle = (map != null && !string.IsNullOrEmpty(map.BackloggdTitle)) ? map.BackloggdTitle : first.GameTitle;

                        foreach (var g in group)
                        {
                            g.BackloggdGameId = gameId;
                            if (!string.IsNullOrEmpty(resolvedTitle)) g.GameTitle = resolvedTitle;
                        }

                        int defaultPlatformId = string.Equals(first.Platform, "Switch", StringComparison.OrdinalIgnoreCase) ? 130 : 6;
                        SyncResult res = BackloggdClient.SyncBatchSessionsForGame(gameId, group, settings, defaultPlatformId);
                        if (res.Success)
                        {
                            foreach (var g in group)
                            {
                                g.Synced = true;
                                g.SyncError = null;
                                g.TotalDayMinutesOnBackloggd = (int)Math.Round(g.DurationSeconds / 60.0);
                            }
                            DataStore.Log(string.Format("Batch-synced {0} historical day(s) for {1} to Backloggd -> {2}h {3}m total playthrough",
                                group.Count, resolvedTitle, res.FinalDayHours, res.FinalDayMinutes));
                        }
                        else
                        {
                            foreach (var g in group)
                            {
                                g.Synced = false;
                                g.RetryCount += 1;
                                g.SyncError = res.Error;
                            }
                        }
                        DataStore.SaveSessions(sessions);
                        if (OnSessionSynced != null) OnSessionSynced(first);
                    }
                    catch (Exception ex)
                    {
                        foreach (var g in group)
                        {
                            g.Synced = false;
                            g.SyncError = ex.Message;
                        }
                        DataStore.SaveSessions(sessions);
                    }
                }
            }
        }

        public static int GetRunningSteamAppId()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("RunningAppID");
                        if (val != null)
                        {
                            int appId = Convert.ToInt32(val);
                            if (appId > 0) return appId;
                        }
                    }
                }
            }
            catch
            {
            }
            return 0;
        }

        public string ResolveSteamGameTitle(int appId)
        {
            if (_titleCache.ContainsKey(appId))
            {
                return _titleCache[appId];
            }

            // 1. Check saved mappings
            var mappings = DataStore.LoadMappings();
            var m = mappings.Find(x => x.AppId == appId);
            if (m != null && !string.IsNullOrEmpty(m.SteamTitle) && !m.SteamTitle.StartsWith("Steam App "))
            {
                _titleCache[appId] = m.SteamTitle;
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
                            _titleCache[appId] = regName.Trim();
                            return _titleCache[appId];
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
                    _titleCache[appId] = acfTitle.Trim();
                    return _titleCache[appId];
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
                            _titleCache[appId] = rawName;
                            return rawName;
                        }
                    }
                }
            }
            catch { }

            return "Steam App " + appId;
        }

        private static bool IsNullOrSpace(string s)
        {
            return string.IsNullOrWhiteSpace(s);
        }

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

        private static string FindTitleFromSteamManifests(int appId)
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
    }
}
