using System;
using System.Collections.Generic;
using System.Threading;

namespace BackloggdAutoSync
{
    public class SyncService
    {
        private static readonly object _syncLock = new object();

        public event Action<SessionEntry> OnSessionSynced;
        public event Action<SessionEntry, string, List<GameSearchResult>> OnUnmappedGamePromptRequested;

        public void TryPreResolveMappingOnLaunch(int appId, string rawTitle)
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

                string cleanTitle = GameTitleMatcher.CleanGameTitle(rawTitle);
                var results = BackloggdClient.SearchGame(cleanTitle, settings);
                if (results.Count == 0 && cleanTitle != rawTitle)
                {
                    results = BackloggdClient.SearchGame(rawTitle, settings);
                }

                var best = GameTitleMatcher.SelectBestMatch(cleanTitle, results);
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
                        string rawTitle = entry.AppId > 0 ? SteamLibrary.ResolveSteamGameTitle(entry.AppId) : (entry.GameTitle ?? "");
                        string cleanTitle = GameTitleMatcher.CleanGameTitle(rawTitle);

                        var searchResults = BackloggdClient.SearchGame(cleanTitle, settings);
                        if (searchResults.Count == 0 && cleanTitle != rawTitle)
                        {
                            searchResults = BackloggdClient.SearchGame(rawTitle, settings);
                        }

                        var best = GameTitleMatcher.SelectBestMatch(cleanTitle, searchResults);
                        if (best == null)
                        {
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
            if (string.IsNullOrWhiteSpace(settings.NintendoStoreSessionToken))
            {
                return "No Nintendo Account connected yet.";
            }

            var allDailyRecords = new List<NintendoDailyGameRecord>();
            var allTimeStoreGames = new List<NintendoAllTimeGameRecord>();

            List<NintendoAllTimeGameRecord> storeGames;
            List<NintendoDailyGameRecord> storeDaily;
            string storeNick;
            string storeAccessToken;
            NintendoSwitchClient.FetchStorePlayHistory(settings.NintendoStoreSessionToken, out storeGames, out storeDaily, out storeNick, out storeAccessToken);
            if (!string.IsNullOrEmpty(storeNick) && settings.NintendoAccountNickname != storeNick)
            {
                settings.NintendoAccountNickname = storeNick;
                DataStore.SaveSettings(settings);
            }
            if (storeDaily != null) allDailyRecords.AddRange(storeDaily);
            if (storeGames != null)
            {
                allTimeStoreGames.AddRange(storeGames);
            }

            var sessions = DataStore.LoadSessions();
            var mappings = DataStore.LoadMappings();
            var sessionIdsToSync = new List<string>();

            // Remove any non-game media/emulator hub mappings and sessions
            int removedNonGames = sessions.RemoveAll(s =>
                string.Equals(s.Platform, "Switch", StringComparison.OrdinalIgnoreCase) &&
                NintendoSwitchClient.IsNonGameSwitchApp(s.SwitchApplicationId, s.GameTitle));
            int removedNonGameMaps = mappings.RemoveAll(m =>
                m.AppId < 0 && NintendoSwitchClient.IsNonGameSwitchApp(m.SwitchApplicationId, m.SteamTitle));
            if (removedNonGameMaps > 0)
            {
                DataStore.SaveMappings(mappings);
            }

            // Remove legacy synthetic single-day nx_alltime_* entries now that we have true per-game daily breakdowns
            var appIdsWithRemovedAllTime = new HashSet<int>();
            var titleIdsWithRemovedAllTime = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = sessions.Count - 1; i >= 0; i--)
            {
                var s = sessions[i];
                if (s.AllTimeTotalMinutes > 0 || (s.Id != null && s.Id.StartsWith("nx_alltime_", StringComparison.OrdinalIgnoreCase)))
                {
                    if (s.AppId != 0) appIdsWithRemovedAllTime.Add(s.AppId);
                    if (!string.IsNullOrEmpty(s.SwitchApplicationId)) titleIdsWithRemovedAllTime.Add(s.SwitchApplicationId);
                    sessions.RemoveAt(i);
                }
            }

            int perGameHistoriesFetched = 0;
            if (!string.IsNullOrEmpty(storeAccessToken) && allTimeStoreGames.Count > 0)
            {
                foreach (var g in allTimeStoreGames)
                {
                    if (string.IsNullOrEmpty(g.TitleId) || NintendoSwitchClient.IsNonGameSwitchApp(g.TitleId, g.TitleName))
                        continue;

                    int switchAppId = NintendoSwitchClient.GetStableSwitchAppId(g.TitleId, g.TitleName);
                    int totalSec = g.TotalPlayedMinutes * 60;
                    if (totalSec < 60) continue;

                    int localDailyMinutes = 0;
                    int localDailyCount = 0;
                    foreach (var s in sessions)
                    {
                        if (s.AllTimeTotalMinutes <= 0 &&
                            (s.AppId == switchAppId || string.Equals(s.SwitchApplicationId, g.TitleId, StringComparison.OrdinalIgnoreCase)))
                        {
                            localDailyMinutes += (int)Math.Round(s.DurationSeconds / 60.0);
                            localDailyCount++;
                        }
                    }

                    bool needsFullDailyBreakdown = fetchEntireHistory
                        || localDailyCount < g.TotalPlayedDays
                        || localDailyMinutes < g.TotalPlayedMinutes
                        || appIdsWithRemovedAllTime.Contains(switchAppId)
                        || titleIdsWithRemovedAllTime.Contains(g.TitleId);

                    if (needsFullDailyBreakdown)
                    {
                        try
                        {
                            var gameDays = NintendoSwitchClient.FetchStoreGameDailyHistory(storeAccessToken, g.TitleId, g.TitleName);
                            if (gameDays != null && gameDays.Count > 0)
                            {
                                allDailyRecords.AddRange(gameDays);
                                perGameHistoriesFetched++;
                            }
                        }
                        catch (Exception ex)
                        {
                            DataStore.Log(string.Format("Warning fetching daily breakdown for {0} ({1}): {2}", g.TitleName, g.TitleId, ex.Message));
                        }
                    }
                }
            }

            // Deduplicate allDailyRecords by (switchAppId, DateStr) taking max minutes and preferring full title names
            var mergedDailyByKey = new Dictionary<string, NintendoDailyGameRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var rec in allDailyRecords)
            {
                if (rec == null || string.IsNullOrEmpty(rec.DateStr)) continue;
                if (NintendoSwitchClient.IsNonGameSwitchApp(rec.ApplicationId, rec.Title)) continue;

                int switchAppId = NintendoSwitchClient.GetStableSwitchAppId(rec.ApplicationId, rec.Title);
                string key = switchAppId.ToString() + "|" + rec.DateStr;
                if (mergedDailyByKey.ContainsKey(key))
                {
                    var existing = mergedDailyByKey[key];
                    if (rec.PlayingTimeMinutes > existing.PlayingTimeMinutes)
                    {
                        existing.PlayingTimeMinutes = rec.PlayingTimeMinutes;
                    }
                    if (!string.IsNullOrEmpty(rec.Title) &&
                        (string.IsNullOrEmpty(existing.Title) || existing.Title.StartsWith("Switch Title ", StringComparison.OrdinalIgnoreCase)))
                    {
                        existing.Title = rec.Title;
                    }
                    if (string.IsNullOrEmpty(existing.ApplicationId) && !string.IsNullOrEmpty(rec.ApplicationId))
                    {
                        existing.ApplicationId = rec.ApplicationId;
                    }
                }
                else
                {
                    mergedDailyByKey[key] = new NintendoDailyGameRecord
                    {
                        DeviceId = rec.DeviceId,
                        DeviceLabel = rec.DeviceLabel,
                        DateStr = rec.DateStr,
                        PlayerNickname = rec.PlayerNickname,
                        ApplicationId = rec.ApplicationId,
                        Title = rec.Title,
                        PlayingTimeMinutes = rec.PlayingTimeMinutes
                    };
                }
            }

            foreach (var rec in mergedDailyByKey.Values)
            {
                int switchAppId = NintendoSwitchClient.GetStableSwitchAppId(rec.ApplicationId, rec.Title);
                int durationSec = rec.PlayingTimeMinutes * 60;
                if (durationSec < 60) continue;

                var existingMap = mappings.Find(m =>
                    m.AppId == switchAppId ||
                    (!string.IsNullOrEmpty(rec.ApplicationId) && string.Equals(m.SwitchApplicationId, rec.ApplicationId, StringComparison.OrdinalIgnoreCase)));

                var existingSession = sessions.Find(s =>
                    s.DateStr == rec.DateStr &&
                    s.AllTimeTotalMinutes <= 0 &&
                    (s.AppId == switchAppId || (!string.IsNullOrEmpty(rec.ApplicationId) && string.Equals(s.SwitchApplicationId, rec.ApplicationId, StringComparison.OrdinalIgnoreCase))));

                if (existingSession != null)
                {
                    bool forceResync = appIdsWithRemovedAllTime.Contains(switchAppId) ||
                                       (!string.IsNullOrEmpty(rec.ApplicationId) && titleIdsWithRemovedAllTime.Contains(rec.ApplicationId));

                    if (existingMap != null && !string.IsNullOrEmpty(existingMap.BackloggdTitle))
                    {
                        existingSession.GameTitle = existingMap.BackloggdTitle;
                        existingSession.BackloggdGameId = existingMap.BackloggdGameId;
                    }
                    else if (!string.IsNullOrEmpty(rec.Title) &&
                             (string.IsNullOrEmpty(existingSession.GameTitle) || existingSession.GameTitle.StartsWith("Switch Title ", StringComparison.OrdinalIgnoreCase)))
                    {
                        existingSession.GameTitle = rec.Title;
                    }

                    if (durationSec > existingSession.DurationSeconds || !existingSession.Synced || forceResync)
                    {
                        existingSession.DurationSeconds = Math.Max(existingSession.DurationSeconds, durationSec);
                        existingSession.Platform = "Switch";
                        existingSession.SetExactDayPlaytime = true;
                        existingSession.Synced = false;
                        existingSession.SyncError = null;
                        if (!sessionIdsToSync.Contains(existingSession.Id)) sessionIdsToSync.Add(existingSession.Id);
                        newOrUpdatedCount++;
                    }
                }
                else
                {
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

            if (sessionIdsToSync.Count > 0 || appIdsWithRemovedAllTime.Count > 0 || removedNonGames > 0)
            {
                sessions.Sort((a, b) => string.Compare(b.DateStr, a.DateStr, StringComparison.Ordinal));
                DataStore.SaveSessions(sessions);

                if (sessionIdsToSync.Count > 0)
                {
                    BatchSyncSessionsByGame(sessionIdsToSync, allowInteractivePrompt);
                }
            }

            if (perGameHistoriesFetched > 0)
            {
                return string.Format("Nintendo Switch sync complete ({0} game history breakdown(s) fetched): {1} daily session(s) synced/updated.",
                    perGameHistoriesFetched, newOrUpdatedCount);
            }

            return string.Format("Checked Nintendo Switch activity: {0} daily game session(s) synced/updated.", newOrUpdatedCount);
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
                var resolvedGroupsByGameId = new Dictionary<string, List<SessionEntry>>(StringComparer.OrdinalIgnoreCase);

                foreach (var kv in byAppId)
                {
                    int appId = kv.Key;
                    List<SessionEntry> group = kv.Value;
                    SessionEntry first = group[0];

                    try
                    {
                        var map = appId != 0 ? mappings.Find(m => m.AppId == appId) : null;
                        if ((map == null || string.IsNullOrEmpty(map.BackloggdGameId)) && string.IsNullOrEmpty(first.BackloggdGameId))
                        {
                            string rawTitle = appId > 0 ? SteamLibrary.ResolveSteamGameTitle(appId) : (first.GameTitle ?? "");
                            string cleanTitle = GameTitleMatcher.CleanGameTitle(rawTitle);
                            var searchResults = BackloggdClient.SearchGame(cleanTitle, settings);
                            if (searchResults.Count == 0 && cleanTitle != rawTitle)
                            {
                                searchResults = BackloggdClient.SearchGame(rawTitle, settings);
                            }

                            var best = GameTitleMatcher.SelectBestMatch(cleanTitle, searchResults);
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

                        if (!resolvedGroupsByGameId.ContainsKey(gameId))
                        {
                            resolvedGroupsByGameId[gameId] = new List<SessionEntry>();
                        }
                        resolvedGroupsByGameId[gameId].AddRange(group);
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

                foreach (var kv in resolvedGroupsByGameId)
                {
                    string gameId = kv.Key;
                    List<SessionEntry> targetGroup = kv.Value;
                    SessionEntry first = targetGroup[0];
                    string resolvedTitle = first.GameTitle;

                    try
                    {
                        bool isSwitch = string.Equals(first.Platform, "Switch", StringComparison.OrdinalIgnoreCase);
                        int defaultPlatformId = isSwitch ? 130 : 6;

                        // For Switch games (SetExactDayPlaytime), pass all local daily sessions for this gameId so Backloggd reconciliation has the full calendar
                        List<SessionEntry> entriesToReconcile = targetGroup;
                        if (isSwitch)
                        {
                            var allForGame = sessions.FindAll(s =>
                                s.AllTimeTotalMinutes <= 0 &&
                                string.Equals(s.Platform, "Switch", StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(s.BackloggdGameId, gameId, StringComparison.OrdinalIgnoreCase));
                            if (allForGame.Count > targetGroup.Count)
                            {
                                entriesToReconcile = allForGame;
                            }
                        }

                        SyncResult res = BackloggdClient.SyncBatchSessionsForGame(gameId, entriesToReconcile, settings, defaultPlatformId);
                        if (res.Success)
                        {
                            foreach (var g in entriesToReconcile)
                            {
                                g.Synced = true;
                                g.SyncError = null;
                                g.TotalDayMinutesOnBackloggd = (int)Math.Round(g.DurationSeconds / 60.0);
                            }
                            DataStore.Log(string.Format("Batch-synced {0} daily session(s) ({1} total days) for {2} to Backloggd -> {3}h {4}m total playthrough",
                                targetGroup.Count, entriesToReconcile.Count, resolvedTitle, res.FinalDayHours, res.FinalDayMinutes));
                        }
                        else
                        {
                            foreach (var g in targetGroup)
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
                        foreach (var g in targetGroup)
                        {
                            g.Synced = false;
                            g.SyncError = ex.Message;
                        }
                        DataStore.SaveSessions(sessions);
                    }
                }
            }
        }
    }
}
