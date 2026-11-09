using System;

namespace BackloggdAutoSync.Tests
{
    [TestFixture]
    public class TimeUtilsTests
    {
        [Test]
        public void Should_Format_Zero_Seconds()
        {
            Assert.AreEqual("0s", TimeUtils.FormatDuration(0));
            Assert.AreEqual("0s", TimeUtils.FormatDuration(-5));
        }

        [Test]
        public void Should_Format_Seconds_Only()
        {
            Assert.AreEqual("45s", TimeUtils.FormatDuration(45));
            Assert.AreEqual("59s", TimeUtils.FormatDuration(59));
        }

        [Test]
        public void Should_Format_Minutes_And_Seconds()
        {
            Assert.AreEqual("1m 0s", TimeUtils.FormatDuration(60));
            Assert.AreEqual("2m 5s", TimeUtils.FormatDuration(125));
            Assert.AreEqual("59m 59s", TimeUtils.FormatDuration(3599));
        }

        [Test]
        public void Should_Format_Hours_Minutes_And_Seconds()
        {
            Assert.AreEqual("1h 0m 0s", TimeUtils.FormatDuration(3600));
            Assert.AreEqual("1h 1m 5s", TimeUtils.FormatDuration(3665));
            Assert.AreEqual("2h 30m 15s", TimeUtils.FormatDuration(9015));
        }

        [Test]
        public void Should_Calculate_Playtime_Thresholds()
        {
            int h, m;

            // Under 30 seconds should not round up to 1 minute
            TimeUtils.CalculatePlaytime(15, out h, out m);
            Assert.AreEqual(0, h);
            Assert.AreEqual(0, m);

            // 30 seconds or more rounds to 1 minute minimum
            TimeUtils.CalculatePlaytime(30, out h, out m);
            Assert.AreEqual(0, h);
            Assert.AreEqual(1, m);

            TimeUtils.CalculatePlaytime(50, out h, out m);
            Assert.AreEqual(0, h);
            Assert.AreEqual(1, m);

            // 90 seconds rounds to 2 minutes
            TimeUtils.CalculatePlaytime(90, out h, out m);
            Assert.AreEqual(0, h);
            Assert.AreEqual(2, m);
        }

        [Test]
        public void Should_Calculate_Playtime_Hour_Rollover()
        {
            int h, m;

            // 3570 seconds is 59 minutes and 30 seconds -> rounds to 60 min -> 1h 0m
            TimeUtils.CalculatePlaytime(3570, out h, out m);
            Assert.AreEqual(1, h);
            Assert.AreEqual(0, m);

            // Exactly 1 hour
            TimeUtils.CalculatePlaytime(3600, out h, out m);
            Assert.AreEqual(1, h);
            Assert.AreEqual(0, m);

            // 1 hour and 30 minutes
            TimeUtils.CalculatePlaytime(5400, out h, out m);
            Assert.AreEqual(1, h);
            Assert.AreEqual(30, m);
        }
    }
}
