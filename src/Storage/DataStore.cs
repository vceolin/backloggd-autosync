using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace BackloggdAutoSync
{
    public static class DataStore
    {
        private static string ResolveBaseDir()
        {
            var entry = System.Reflection.Assembly.GetEntryAssembly();
            if (entry != null && !string.IsNullOrEmpty(entry.Location))
            {
                return Path.GetDirectoryName(entry.Location);
            }
            var own = typeof(DataStore).Assembly;
            if (own != null && !string.IsNullOrEmpty(own.Location))
            {
                return Path.GetDirectoryName(own.Location);
            }
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        public static readonly string DataDir = Path.Combine(ResolveBaseDir(), "data");
        public static readonly string SettingsPath = Path.Combine(DataDir, "settings.json");
        public static readonly string MappingsPath = Path.Combine(DataDir, "mappings.json");
        public static readonly string SessionsPath = Path.Combine(DataDir, "sessions.json");
        public static readonly string CollectionPath = Path.Combine(DataDir, "collection.json");
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

        public static JavaScriptSerializer CreateSerializer()
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
                        if (loaded.NintendoSyncDaysBack <= 0) loaded.NintendoSyncDaysBack = 14;
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

        public static List<CollectionEntry> LoadCollection()
        {
            try
            {
                EnsureDataDir();
                if (File.Exists(CollectionPath))
                {
                    string json = File.ReadAllText(CollectionPath, Encoding.UTF8);
                    var list = CreateSerializer().Deserialize<List<CollectionEntry>>(json);
                    if (list != null) return list;
                }
            }
            catch (Exception ex)
            {
                Log("Failed to load collection: " + ex.Message);
            }
            return new List<CollectionEntry>();
        }

        public static void SaveCollection(List<CollectionEntry> collection)
        {
            try
            {
                lock (_lock)
                {
                    EnsureDataDir();
                    string json = CreateSerializer().Serialize(collection);
                    File.WriteAllText(CollectionPath, json, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Log("Failed to save collection: " + ex.Message);
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
