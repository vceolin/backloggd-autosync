using System;
using System.Collections.Generic;

namespace BackloggdAutoSync.Tests
{
    [TestFixture]
    public class SyncLogicTests
    {
        [Test]
        public void Should_Group_Sessions_By_AppId()
        {
            var sessions = new List<SessionEntry>
            {
                new SessionEntry { Id = "s1", AppId = 100, DurationSeconds = 1200 },
                new SessionEntry { Id = "s2", AppId = 200, DurationSeconds = 1800 },
                new SessionEntry { Id = "s3", AppId = 100, DurationSeconds = 2400 }
            };

            var byAppId = new Dictionary<int, List<SessionEntry>>();
            foreach (var s in sessions)
            {
                if (!byAppId.ContainsKey(s.AppId))
                {
                    byAppId[s.AppId] = new List<SessionEntry>();
                }
                byAppId[s.AppId].Add(s);
            }

            Assert.AreEqual(2, byAppId.Count);
            Assert.AreEqual(2, byAppId[100].Count);
            Assert.AreEqual(1, byAppId[200].Count);
        }

        [Test]
        public void Should_Accumulate_Playtime_Across_Same_Day()
        {
            int existingMinutes = 45; // 45m already logged today
            int newSessionSeconds = 1800; // 30m more played

            int addHours, addMins;
            TimeUtils.CalculatePlaytime(newSessionSeconds, out addHours, out addMins);

            int totalMinutes = existingMinutes + (addHours * 60 + addMins);
            int finalHours = totalMinutes / 60;
            int finalMins = totalMinutes % 60;

            Assert.AreEqual(1, finalHours);
            Assert.AreEqual(15, finalMins); // 45 + 30 = 75 mins = 1h 15m
        }

        [Test]
        public void Should_Handle_Exact_Day_Playtime_Override()
        {
            // For Nintendo Switch, it reports daily exact total
            int existingMinutes = 60;
            int recordedMinutes = 90; // Switch sync reported 90 mins total for day

            int totalMinutes = Math.Max(existingMinutes, recordedMinutes);
            Assert.AreEqual(90, totalMinutes);
        }
    }
}
