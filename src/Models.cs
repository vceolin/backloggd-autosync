using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace BackloggdAutoSync
{
    public class AppSettings
    {
        public string SessionCookie { get; set; }
        public string CsrfToken { get; set; }
        public string UserId { get; set; }
        public string Username { get; set; }
        public int MinSessionSeconds { get; set; }
        public bool ShowNotifications { get; set; }
        public bool AutoSyncOnExit { get; set; }
        public bool SplitAtMidnight { get; set; }
        public bool StartMinimized { get; set; }
        public List<int> IgnoredAppIds { get; set; }

        public AppSettings()
        {
            SessionCookie = "";
            CsrfToken = "";
            UserId = "";
            Username = "";
            MinSessionSeconds = 60;
            ShowNotifications = true;
            AutoSyncOnExit = true;
            SplitAtMidnight = true;
            StartMinimized = false;
            IgnoredAppIds = new List<int>
            {
                228980, // Steamworks Common Redistributables
                250820, // SteamVR
                1070560, // Steam Linux Runtime
                1391110, // Steam Linux Runtime - Soldier
                1628350, // Steam Linux Runtime - Sniper
                431960   // Wallpaper Engine (often left running in background; user can remove from ignore list if desired)
            };
        }
    }

    public class GameMapping
    {
        public int AppId { get; set; }
        public string BackloggdGameId { get; set; }
        public string SteamTitle { get; set; }
        public string BackloggdTitle { get; set; }
        public string ReleaseYear { get; set; }
        public bool ManualOverride { get; set; }
        public string LastUpdated { get; set; }
    }

    public class SessionEntry
    {
        public string Id { get; set; }
        public int AppId { get; set; }
        public string GameTitle { get; set; }
        public string BackloggdGameId { get; set; }
        public string DateStr { get; set; } // YYYY-MM-DD
        public string StartTimeIso { get; set; }
        public string EndTimeIso { get; set; }
        public int DurationSeconds { get; set; }
        public bool Synced { get; set; }
        public bool AccumulatedOnBackloggd { get; set; }
        public int TotalDayMinutesOnBackloggd { get; set; }
        public string SyncError { get; set; }
        public int RetryCount { get; set; }
    }

    public class ActiveSessionCheckpoint
    {
        public int AppId { get; set; }
        public string GameTitle { get; set; }
        public string DateStr { get; set; }
        public string StartTimeIso { get; set; }
        public string LastHeartbeatIso { get; set; }
        public int ElapsedSeconds { get; set; }
    }

    public class GameSearchResult
    {
        public string GameId { get; set; }
        public string Title { get; set; }
        public string ReleaseYear { get; set; }

        public override string ToString()
        {
            return string.IsNullOrEmpty(ReleaseYear)
                ? string.Format("{0} (ID: {1})", Title, GameId)
                : string.Format("{0} ({1}) [ID: {2}]", Title, ReleaseYear, GameId);
        }
    }

    public class PlayDateEntry
    {
        public int id { get; set; }
        public string range_start_date { get; set; }
        public string range_end_date { get; set; }
        public int hours { get; set; }
        public int minutes { get; set; }
        public object status { get; set; }
        public string note { get; set; }
        public bool edited { get; set; }
        public string start_date { get; set; }
        public string finish_date { get; set; }
        public object privacy { get; set; }
        public List<string> tags { get; set; }

        public PlayDateEntry()
        {
            note = "";
            tags = new List<string>();
        }
    }

    public class GameLogStatus
    {
        public bool IsLogged { get; set; }
        public int? GameLogId { get; set; }
        public int? PlaythroughId { get; set; }
        public string PlaythroughTitle { get; set; }
        public int PlatformId { get; set; }
        public int Rating { get; set; }
        public string Status { get; set; }
        public bool IsPlay { get; set; }
        public bool IsPlaying { get; set; }
        public bool IsBacklog { get; set; }
        public bool IsWishlist { get; set; }
        public bool GameLiked { get; set; }
        public int HoursPlayed { get; set; }
        public int MinsPlayed { get; set; }
        public List<PlayDateEntry> PlayDates { get; set; }

        public GameLogStatus()
        {
            PlaythroughTitle = "Windows PC";
            PlatformId = 6; // Windows PC
            Status = "playing";
            IsPlaying = true;
            PlayDates = new List<PlayDateEntry>();
        }
    }

    public class SyncResult
    {
        public bool Success { get; set; }
        public string GameId { get; set; }
        public string DateStr { get; set; }
        public int FinalDayHours { get; set; }
        public int FinalDayMinutes { get; set; }
        public bool Accumulated { get; set; }
        public string Error { get; set; }
    }

    public static class DataStore
    {
        public static readonly string DataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        public static readonly string SettingsPath = Path.Combine(DataDir, "settings.json");
        public static readonly string MappingsPath = Path.Combine(DataDir, "mappings.json");
        public static readonly string SessionsPath = Path.Combine(DataDir, "sessions.json");
        public static readonly string CheckpointPath = Path.Combine(DataDir, "active_session.json");
        public static readonly string LogFilePath = Path.Combine(DataDir, "activity.log");

        private static readonly object _lock = new object();

        public static void EnsureDataDir()
        {
            if (!Directory.Exists(DataDir))
            {
                Directory.CreateDirectory(DataDir);
            }
        }

        public static void Log(string message)
        {
            try
            {
                lock (_lock)
                {
                    EnsureDataDir();
                    string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}{2}", DateTime.Now, message, Environment.NewLine);
                    File.AppendAllText(LogFilePath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Ignore logging errors
            }
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            return s;
        }

        public static AppSettings LoadSettings()
        {
            try
            {
                EnsureDataDir();
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                    var loaded = CreateSerializer().Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        if (loaded.IgnoredAppIds == null) loaded.IgnoredAppIds = new List<int>();
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Failed to load settings: " + ex.Message);
            }
            return new AppSettings();
        }

        public static void SaveSettings(AppSettings settings)
        {
            try
            {
                lock (_lock)
                {
                    EnsureDataDir();
                    string json = CreateSerializer().Serialize(settings);
                    File.WriteAllText(SettingsPath, json, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Log("Failed to save settings: " + ex.Message);
            }
        }

        public static List<GameMapping> LoadMappings()
        {
            try
            {
                EnsureDataDir();
                if (File.Exists(MappingsPath))
                {
                    string json = File.ReadAllText(MappingsPath, Encoding.UTF8);
                    var list = CreateSerializer().Deserialize<List<GameMapping>>(json);
                    if (list != null) return list;
                }
            }
            catch (Exception ex)
            {
                Log("Failed to load mappings: " + ex.Message);
            }
            return new List<GameMapping>();
        }

        public static void SaveMappings(List<GameMapping> mappings)
        {
            try
            {
                lock (_lock)
                {
                    EnsureDataDir();
                    string json = CreateSerializer().Serialize(mappings);
                    File.WriteAllText(MappingsPath, json, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Log("Failed to save mappings: " + ex.Message);
            }
        }

        public static List<SessionEntry> LoadSessions()
        {
            try
            {
                EnsureDataDir();
                if (File.Exists(SessionsPath))
                {
                    string json = File.ReadAllText(SessionsPath, Encoding.UTF8);
                    var list = CreateSerializer().Deserialize<List<SessionEntry>>(json);
                    if (list != null) return list;
                }
            }
            catch (Exception ex)
            {
                Log("Failed to load sessions: " + ex.Message);
            }
            return new List<SessionEntry>();
        }

        public static void SaveSessions(List<SessionEntry> sessions)
        {
            try
            {
                lock (_lock)
                {
                    EnsureDataDir();
                    string json = CreateSerializer().Serialize(sessions);
                    File.WriteAllText(SessionsPath, json, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Log("Failed to save sessions: " + ex.Message);
            }
        }

        public static ActiveSessionCheckpoint LoadCheckpoint()
        {
            try
            {
                EnsureDataDir();
                if (File.Exists(CheckpointPath))
                {
                    string json = File.ReadAllText(CheckpointPath, Encoding.UTF8);
                    return CreateSerializer().Deserialize<ActiveSessionCheckpoint>(json);
                }
            }
            catch
            {
            }
            return null;
        }

        public static void SaveCheckpoint(ActiveSessionCheckpoint cp)
        {
            try
            {
                lock (_lock)
                {
                    EnsureDataDir();
                    if (cp == null)
                    {
                        if (File.Exists(CheckpointPath)) File.Delete(CheckpointPath);
                    }
                    else
                    {
                        string json = CreateSerializer().Serialize(cp);
                        File.WriteAllText(CheckpointPath, json, Encoding.UTF8);
                    }
                }
            }
            catch
            {
            }
        }
    }
}
