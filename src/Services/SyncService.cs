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

        public List<CollectionEntry> RefreshCollection(bool fetchRemoteSources = true, Action<string> onProgress = null)
        {
            var settings = DataStore.LoadSettings();
            var collection = DataStore.LoadCollection();
            var mappings = DataStore.LoadMappings();
            var sessions = DataStore.LoadSessions();

            var byId = new Dictionary<string, CollectionEntry>(StringComparer.OrdinalIgnoreCase);
            var byAppId = new Dictionary<int, CollectionEntry>();
            foreach (var c in collection)
            {
                if (!string.IsNullOrEmpty(c.Id)) byId[c.Id] = c;
                if (c.AppId != 0 && !byAppId.ContainsKey(c.AppId)) byAppId[c.AppId] = c;
            }

            // 1. Discover all owned Steam games locally (installed + uninstalled)
            if (onProgress != null) onProgress("Scanning local Steam library (installed & uninstalled owned games)...");
            var ownedSteam = SteamLibrary.GetAllOwnedSteamGames(settings.IgnoredAppIds, true);
            foreach (var sg in ownedSteam)
            {
                string cid = "steam_" + sg.AppId;
                CollectionEntry entry;
                if (!byId.TryGetValue(cid, out entry) && !byAppId.TryGetValue(sg.AppId, out entry))
                {
                    entry = new CollectionEntry
                    {
                        Id = cid,
                        Source = "Steam",
                        AppId = sg.AppId,
                        LocalTitle = sg.Title,
                        IsInstalled = sg.IsInstalled,
                        IsHiddenInSteam = sg.IsHidden,
                        PlatformId = 6,
                        OwnershipId = 1, // Digital
                        StorefrontId = 1 // Steam
                    };
                    collection.Add(entry);
                    byId[cid] = entry;
                    byAppId[sg.AppId] = entry;
                }
                else
                {
                    entry.Id = cid;
                    entry.Source = "Steam";
                    entry.AppId = sg.AppId;
                    if (!string.IsNullOrEmpty(sg.Title)) entry.LocalTitle = sg.Title;
                    entry.IsInstalled = sg.IsInstalled;
                    entry.IsHiddenInSteam = sg.IsHidden;
                    if (entry.PlatformId <= 0) entry.PlatformId = 6;
                }

                var map = mappings.Find(m => m.AppId == sg.AppId);
                if (map != null && !string.IsNullOrEmpty(map.BackloggdGameId))
                {
                    if (entry.BackloggdGameId != map.BackloggdGameId)
                    {
                        entry.BackloggdGameId = map.BackloggdGameId;
                        entry.BackloggdTitle = map.BackloggdTitle;
                        entry.ReleaseYear = map.ReleaseYear ?? "";
                    }
                    else if (string.IsNullOrEmpty(entry.BackloggdTitle) && !string.IsNullOrEmpty(map.BackloggdTitle))
                    {
                        entry.BackloggdTitle = map.BackloggdTitle;
                    }
                }
            }

            // 2. Discover Nintendo Switch / Switch 2 games from sessions, mappings, and optionally Nintendo Store API
            var switchCandidates = new Dictionary<int, NintendoDailyGameRecord>();
            foreach (var s in sessions)
            {
                if (!string.Equals(s.Platform, "Switch", StringComparison.OrdinalIgnoreCase)) continue;
                if (NintendoSwitchClient.IsNonGameSwitchApp(s.SwitchApplicationId, s.GameTitle)) continue;

                int swId = s.AppId != 0 ? s.AppId : NintendoSwitchClient.GetStableSwitchAppId(s.SwitchApplicationId, s.GameTitle);
                if (!switchCandidates.ContainsKey(swId))
                {
                    var map = mappings.Find(m => m.AppId == swId);
                    string rawName = (map != null && !string.IsNullOrEmpty(map.SteamTitle)) ? map.SteamTitle : s.GameTitle;
                    switchCandidates[swId] = new NintendoDailyGameRecord
                    {
                        ApplicationId = s.SwitchApplicationId ?? (map != null ? map.SwitchApplicationId : ""),
                        Title = rawName ?? ""
                    };
                }
            }

            foreach (var m in mappings)
            {
                if (m.AppId >= 0) continue;
                if (NintendoSwitchClient.IsNonGameSwitchApp(m.SwitchApplicationId, m.SteamTitle)) continue;
                if (!switchCandidates.ContainsKey(m.AppId))
                {
                    switchCandidates[m.AppId] = new NintendoDailyGameRecord
                    {
                        ApplicationId = m.SwitchApplicationId ?? "",
                        Title = !string.IsNullOrEmpty(m.SteamTitle) ? m.SteamTitle : m.BackloggdTitle
                    };
                }
            }

            if (fetchRemoteSources && !string.IsNullOrWhiteSpace(settings.NintendoStoreSessionToken))
            {
                try
                {
                    if (onProgress != null) onProgress("Checking Nintendo Switch game history...");
                    List<NintendoAllTimeGameRecord> storeGames;
                    List<NintendoDailyGameRecord> storeDaily;
                    string nick;
                    string accessToken;
                    NintendoSwitchClient.FetchStorePlayHistory(settings.NintendoStoreSessionToken, out storeGames, out storeDaily, out nick, out accessToken);
                    if (storeGames != null)
                    {
                        foreach (var g in storeGames)
                        {
                            if (NintendoSwitchClient.IsNonGameSwitchApp(g.TitleId, g.TitleName)) continue;
                            int swId = NintendoSwitchClient.GetStableSwitchAppId(g.TitleId, g.TitleName);
                            switchCandidates[swId] = new NintendoDailyGameRecord
                            {
                                ApplicationId = g.TitleId ?? "",
                                Title = g.TitleName ?? ""
                            };
                        }
                    }
                }
                catch { }
            }

            foreach (var kv in switchCandidates)
            {
                int swId = kv.Key;
                var rec = kv.Value;
                string cid = "nx_" + Math.Abs(swId);

                CollectionEntry entry;
                if (!byId.TryGetValue(cid, out entry) && !byAppId.TryGetValue(swId, out entry))
                {
                    var map = mappings.Find(m => m.AppId == swId);
                    string checkTitle = ((rec.Title ?? "") + " " + (map != null ? map.BackloggdTitle : "")).ToLowerInvariant();
                    int defaultPlat = (checkTitle.Contains("switch 2") || checkTitle.Contains("mario kart world") || checkTitle.Contains("donkey kong bananza")) ? 508 : 130;

                    entry = new CollectionEntry
                    {
                        Id = cid,
                        Source = "Nintendo Switch",
                        AppId = swId,
                        SwitchApplicationId = rec.ApplicationId ?? "",
                        LocalTitle = rec.Title ?? "",
                        IsInstalled = true,
                        PlatformId = defaultPlat,
                        OwnershipId = 1, // Digital by default; user can switch to Physical (0)
                        StorefrontId = 8 // Nintendo eShop
                    };
                    collection.Add(entry);
                    byId[cid] = entry;
                    byAppId[swId] = entry;
                }
                else
                {
                    entry.Id = cid;
                    entry.Source = "Nintendo Switch";
                    entry.AppId = swId;
                    if (!string.IsNullOrEmpty(rec.ApplicationId)) entry.SwitchApplicationId = rec.ApplicationId;
                    if (!string.IsNullOrEmpty(rec.Title)) entry.LocalTitle = rec.Title;
                    if (entry.PlatformId != 130 && entry.PlatformId != 508) entry.PlatformId = 130;
                }

                var swMap = mappings.Find(m => m.AppId == swId);
                if (swMap != null && !string.IsNullOrEmpty(swMap.BackloggdGameId))
                {
                    entry.BackloggdGameId = swMap.BackloggdGameId;
                    if (!string.IsNullOrEmpty(swMap.BackloggdTitle)) entry.BackloggdTitle = swMap.BackloggdTitle;
                    if (!string.IsNullOrEmpty(swMap.ReleaseYear)) entry.ReleaseYear = swMap.ReleaseYear;
                }
            }

            // Remove any non-game Switch or Steam entries that might have been saved previously
            collection.RemoveAll(c =>
                (c.Source == "Nintendo Switch" && NintendoSwitchClient.IsNonGameSwitchApp(c.SwitchApplicationId, c.LocalTitle)) ||
                (c.Source == "Steam" && SteamLibrary.IsNonGameTitle(c.AppId, c.LocalTitle)));

            // 3. Cross-platform mapping reuse: if a game with the exact same normalized title is already mapped (e.g., on Switch or another Steam AppID), reuse that BackloggdGameId
            var knownByNormTitle = new Dictionary<string, GameMapping>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in mappings)
            {
                if (string.IsNullOrEmpty(m.BackloggdGameId)) continue;
                string n1 = GameTitleMatcher.NormalizeTitle(GameTitleMatcher.CleanGameTitle(m.SteamTitle));
                string n2 = GameTitleMatcher.NormalizeTitle(GameTitleMatcher.CleanGameTitle(m.BackloggdTitle));
                if (!string.IsNullOrEmpty(n1) && !knownByNormTitle.ContainsKey(n1)) knownByNormTitle[n1] = m;
                if (!string.IsNullOrEmpty(n2) && !knownByNormTitle.ContainsKey(n2)) knownByNormTitle[n2] = m;
            }
            bool crossMappedAny = false;
            foreach (var c in collection)
            {
                if (!string.IsNullOrEmpty(c.BackloggdGameId) || c.Excluded) continue;
                string normLocal = GameTitleMatcher.NormalizeTitle(GameTitleMatcher.CleanGameTitle(c.LocalTitle));
                GameMapping existingMatch;
                if (!string.IsNullOrEmpty(normLocal) && knownByNormTitle.TryGetValue(normLocal, out existingMatch))
                {
                    c.BackloggdGameId = existingMatch.BackloggdGameId;
                    c.BackloggdTitle = existingMatch.BackloggdTitle;
                    c.ReleaseYear = existingMatch.ReleaseYear ?? "";
                    if (c.AppId != 0 && !mappings.Exists(x => x.AppId == c.AppId))
                    {
                        mappings.Add(new GameMapping
                        {
                            AppId = c.AppId,
                            SwitchApplicationId = c.SwitchApplicationId,
                            BackloggdGameId = existingMatch.BackloggdGameId,
                            SteamTitle = c.LocalTitle,
                            BackloggdTitle = existingMatch.BackloggdTitle,
                            ReleaseYear = existingMatch.ReleaseYear,
                            ManualOverride = false,
                            LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        });
                        crossMappedAny = true;
                    }
                }
            }
            if (crossMappedAny)
            {
                DataStore.SaveMappings(mappings);
            }

            // 4. Reconcile with existing remote Backloggd Library Entries
            if (fetchRemoteSources && !string.IsNullOrWhiteSpace(settings.SessionCookie) && !string.IsNullOrWhiteSpace(settings.UserId))
            {
                try
                {
                    if (onProgress != null) onProgress("Fetching existing Backloggd Library Entries...");
                    var remoteEntries = BackloggdClient.FetchUserLibraryEntries(settings);
                    bool mappingsUpdated = false;

                    foreach (var rem in remoteEntries)
                    {
                        CollectionEntry matched = collection.Find(c =>
                            !string.IsNullOrEmpty(c.BackloggdLibraryEntryId) &&
                            c.BackloggdLibraryEntryId == rem.EntryId);

                        if (matched == null)
                        {
                            bool isPcRem = (rem.PlatformId == 6);
                            bool isSwitchRem = (rem.PlatformId == 130 || rem.PlatformId == 508);

                            matched = collection.Find(c =>
                                c.BackloggdGameId == rem.GameId &&
                                ((isPcRem && c.Source == "Steam") || (isSwitchRem && c.Source == "Nintendo Switch") || c.PlatformId == rem.PlatformId));
                        }

                        if (matched == null && !string.IsNullOrEmpty(rem.GameTitle))
                        {
                            string remClean = GameTitleMatcher.CleanGameTitle(rem.GameTitle);
                            bool isPcRem = (rem.PlatformId == 6);
                            bool isSwitchRem = (rem.PlatformId == 130 || rem.PlatformId == 508);

                            matched = collection.Find(c =>
                                string.IsNullOrEmpty(c.BackloggdLibraryEntryId) &&
                                ((isPcRem && c.Source == "Steam") || (isSwitchRem && c.Source == "Nintendo Switch")) &&
                                string.Equals(GameTitleMatcher.CleanGameTitle(c.LocalTitle), remClean, StringComparison.OrdinalIgnoreCase));

                            if (matched != null && string.IsNullOrEmpty(matched.BackloggdGameId))
                            {
                                matched.BackloggdGameId = rem.GameId;
                                matched.BackloggdTitle = rem.GameTitle;

                                if (matched.AppId != 0 && !mappings.Exists(m => m.AppId == matched.AppId))
                                {
                                    mappings.Add(new GameMapping
                                    {
                                        AppId = matched.AppId,
                                        SwitchApplicationId = matched.SwitchApplicationId,
                                        BackloggdGameId = rem.GameId,
                                        SteamTitle = matched.LocalTitle,
                                        BackloggdTitle = rem.GameTitle,
                                        ManualOverride = false,
                                        LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                                    });
                                    mappingsUpdated = true;
                                }
                            }
                        }

                        if (matched != null)
                        {
                            matched.BackloggdLibraryEntryId = rem.EntryId;
                            if (string.IsNullOrEmpty(matched.BackloggdGameId))
                            {
                                matched.BackloggdGameId = rem.GameId;
                                if (!string.IsNullOrEmpty(rem.GameTitle)) matched.BackloggdTitle = rem.GameTitle;
                            }
                            matched.PlatformId = rem.PlatformId;
                            matched.OwnershipId = rem.OwnershipId;
                            matched.StorefrontId = rem.StorefrontId;
                            if (!string.IsNullOrEmpty(rem.EditionId)) matched.EditionId = rem.EditionId;
                            matched.Synced = true;
                            matched.SyncError = "";
                        }
                    }

                    if (mappingsUpdated)
                    {
                        DataStore.SaveMappings(mappings);
                    }
                }
                catch (Exception ex)
                {
                    DataStore.Log("Error fetching remote library entries: " + ex.Message);
                }
            }

            collection.Sort((a, b) =>
            {
                int srcCmp = string.Compare(a.Source, b.Source, StringComparison.OrdinalIgnoreCase);
                if (srcCmp != 0) return srcCmp;
                return string.Compare(a.LocalTitle, b.LocalTitle, StringComparison.OrdinalIgnoreCase);
            });

            DataStore.SaveCollection(collection);
            return collection;
        }

        public int AutoMatchCollectionEntries(List<CollectionEntry> targetEntries, Action<int, int, string> onProgress = null)
        {
            if (targetEntries == null || targetEntries.Count == 0) return 0;

            var settings = DataStore.LoadSettings();
            if (string.IsNullOrWhiteSpace(settings.SessionCookie)) return 0;

            var collection = DataStore.LoadCollection();
            var mappings = DataStore.LoadMappings();
            var dataLock = new object();
            int matched = 0;
            int completed = 0;
            int total = targetEntries.Count;

            var options = new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 2 };
            System.Threading.Tasks.Parallel.ForEach(targetEntries, options, t =>
            {
                int idx = Interlocked.Increment(ref completed);
                if (onProgress != null && (idx % 3 == 0 || idx == total || idx == 1))
                {
                    try { onProgress(idx, total, t.LocalTitle); } catch { }
                }

                try
                {
                    var results = BackloggdClient.SearchGame(t.LocalTitle, settings);
                    var best = BackloggdClient.SelectBestMatch(t.LocalTitle, results);
                    if (best != null)
                    {
                        lock (dataLock)
                        {
                            var stored = collection.Find(x => x.Id == t.Id);
                            if (stored != null)
                            {
                                stored.BackloggdGameId = best.GameId;
                                stored.BackloggdTitle = best.Title;
                                stored.ReleaseYear = best.ReleaseYear ?? "";
                                stored.LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                            }

                            if (t.AppId != 0)
                            {
                                mappings.RemoveAll(m => m.AppId == t.AppId);
                                mappings.Add(new GameMapping
                                {
                                    AppId = t.AppId,
                                    SwitchApplicationId = t.SwitchApplicationId,
                                    BackloggdGameId = best.GameId,
                                    SteamTitle = t.LocalTitle,
                                    BackloggdTitle = best.Title,
                                    ReleaseYear = best.ReleaseYear,
                                    ManualOverride = false,
                                    LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                                });
                            }
                            matched++;
                            if (matched % 25 == 0)
                            {
                                DataStore.SaveCollection(collection);
                                DataStore.SaveMappings(mappings);
                            }
                        }
                    }
                    Thread.Sleep(120);
                }
                catch { }
            });

            DataStore.SaveCollection(collection);
            DataStore.SaveMappings(mappings);
            return matched;
        }
    }
}
