using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Win32;

namespace BackloggdAutoSync
{
    public class SteamTracker
    {
        private int _currentAppId = 0;
        private string _currentTitle = "";
        private DateTime _sessionStart;
        private string _sessionDateStr = "";
        private DateTime _lastCheckpointTime = DateTime.MinValue;
        private readonly SyncService _syncService;

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

        public SyncService SyncService { get { return _syncService; } }

        public event Action<int, string> OnGameStarted;
        public event Action<SessionEntry> OnSessionCompleted;
        public event Action<SessionEntry> OnSessionSynced
        {
            add { _syncService.OnSessionSynced += value; }
            remove { _syncService.OnSessionSynced -= value; }
        }
        public event Action<SessionEntry, string, List<GameSearchResult>> OnUnmappedGamePromptRequested
        {
            add { _syncService.OnUnmappedGamePromptRequested += value; }
            remove { _syncService.OnUnmappedGamePromptRequested -= value; }
        }

        public SteamTracker(SyncService syncService = null)
        {
            _syncService = syncService ?? new SyncService();
        }

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
                _currentTitle = SteamLibrary.ResolveSteamGameTitle(runningAppId);
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
                ThreadPool.QueueUserWorkItem(_ => _syncService.TryPreResolveMappingOnLaunch(launchedAppId, launchedTitle));
            }

            // Periodic checkpoint every 30 seconds while playing
            if (_currentAppId > 0 && (now - _lastCheckpointTime).TotalSeconds >= 30)
            {
                _lastCheckpointTime = now;
                SaveCurrentCheckpoint();
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
                ThreadPool.QueueUserWorkItem(_ => _syncService.SyncSessionEntryAsync(entry.Id, true));
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

        // Delegating helper methods for compatibility
        public string ResolveSteamGameTitle(int appId)
        {
            return SteamLibrary.ResolveSteamGameTitle(appId);
        }

        public static List<string> GetSteamAppsDirectories()
        {
            return SteamLibrary.GetSteamAppsDirectories();
        }

        public static List<InstalledSteamGame> GetInstalledSteamGames(List<int> ignoredAppIds = null)
        {
            return SteamLibrary.GetInstalledSteamGames(ignoredAppIds);
        }

        public void SyncSessionEntryAsync(string sessionId, bool allowInteractivePrompt = false)
        {
            _syncService.SyncSessionEntryAsync(sessionId, allowInteractivePrompt);
        }

        public void BatchSyncSessionsByGame(List<string> sessionIds, bool allowInteractivePrompt)
        {
            _syncService.BatchSyncSessionsByGame(sessionIds, allowInteractivePrompt);
        }

        public string FetchAndSyncNintendoSwitchSummaries(bool allowInteractivePrompt, out int newOrUpdatedCount, bool fetchEntireHistory = false)
        {
            return _syncService.FetchAndSyncNintendoSwitchSummaries(allowInteractivePrompt, out newOrUpdatedCount, fetchEntireHistory);
        }
    }
}
