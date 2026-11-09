using System;
using System.Collections.Generic;

namespace BackloggdAutoSync.Tests
{
    [TestFixture]
    public class DataStoreTests
    {
        [Test]
        public void Should_Have_Sensible_Default_Settings()
        {
            var s = new AppSettings();
            Assert.AreEqual(60, s.MinSessionSeconds);
            Assert.IsTrue(s.AutoSyncOnExit);
            Assert.IsTrue(s.SplitAtMidnight);
            Assert.IsTrue(s.ShowNotifications);
            Assert.IsFalse(s.StartMinimized);
            Assert.AreEqual(14, s.NintendoSyncDaysBack);
            Assert.IsTrue(s.NintendoAutoSync);
            Assert.IsNotNull(s.IgnoredAppIds);
            Assert.IsTrue(s.IgnoredAppIds.Contains(228980)); // Steamworks Common Redistributables
            Assert.IsTrue(s.IgnoredAppIds.Contains(431960)); // Wallpaper Engine
        }

        [Test]
        public void Should_Serialize_And_Deserialize_SessionEntry()
        {
            var entry = new SessionEntry
            {
                Id = "test_sess_12345",
                Platform = "Steam",
                AppId = 105600,
                GameTitle = "Terraria",
                BackloggdGameId = "789",
                DateStr = "2026-10-25",
                StartTimeIso = "2026-10-25T14:00:00",
                EndTimeIso = "2026-10-25T15:30:00",
                DurationSeconds = 5400,
                Synced = true,
                TotalDayMinutesOnBackloggd = 90
            };

            var serializer = DataStore.CreateSerializer();
            string json = serializer.Serialize(entry);
            var deserialized = serializer.Deserialize<SessionEntry>(json);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual(entry.Id, deserialized.Id);
            Assert.AreEqual(entry.Platform, deserialized.Platform);
            Assert.AreEqual(entry.AppId, deserialized.AppId);
            Assert.AreEqual(entry.GameTitle, deserialized.GameTitle);
            Assert.AreEqual(entry.BackloggdGameId, deserialized.BackloggdGameId);
            Assert.AreEqual(entry.DurationSeconds, deserialized.DurationSeconds);
            Assert.AreEqual(entry.Synced, deserialized.Synced);
            Assert.AreEqual(entry.TotalDayMinutesOnBackloggd, deserialized.TotalDayMinutesOnBackloggd);
        }

        [Test]
        public void Should_Serialize_And_Deserialize_GameMapping()
        {
            var map = new GameMapping
            {
                AppId = 620,
                BackloggdGameId = "portal-2",
                SteamTitle = "Portal 2",
                BackloggdTitle = "Portal 2",
                ReleaseYear = "2011",
                ManualOverride = true
            };

            var serializer = DataStore.CreateSerializer();
            string json = serializer.Serialize(map);
            var deserialized = serializer.Deserialize<GameMapping>(json);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual(map.AppId, deserialized.AppId);
            Assert.AreEqual(map.BackloggdGameId, deserialized.BackloggdGameId);
            Assert.AreEqual(map.BackloggdTitle, deserialized.BackloggdTitle);
            Assert.AreEqual(map.ManualOverride, deserialized.ManualOverride);
        }

        [Test]
        public void Should_Serialize_And_Deserialize_ActiveSessionCheckpoint()
        {
            var cp = new ActiveSessionCheckpoint
            {
                AppId = 1145360,
                GameTitle = "Hades",
                DateStr = "2026-10-25",
                StartTimeIso = "2026-10-25T20:00:00",
                LastHeartbeatIso = "2026-10-25T20:30:00",
                ElapsedSeconds = 1800
            };

            var serializer = DataStore.CreateSerializer();
            string json = serializer.Serialize(cp);
            var deserialized = serializer.Deserialize<ActiveSessionCheckpoint>(json);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual(cp.AppId, deserialized.AppId);
            Assert.AreEqual(cp.GameTitle, deserialized.GameTitle);
            Assert.AreEqual(cp.ElapsedSeconds, deserialized.ElapsedSeconds);
        }
    }
}
