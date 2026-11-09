using System;

namespace BackloggdAutoSync
{
    public static class TimeUtils
    {
        public static string FormatDuration(int totalSeconds)
        {
            if (totalSeconds < 0) totalSeconds = 0;
            int hours = totalSeconds / 3600;
            int mins = (totalSeconds % 3600) / 60;
            int secs = totalSeconds % 60;

            if (hours > 0)
                return string.Format("{0}h {1}m {2}s", hours, mins, secs);
            if (mins > 0)
                return string.Format("{0}m {1}s", mins, secs);
            return string.Format("{0}s", secs);
        }

        public static void CalculatePlaytime(int durationSeconds, out int hours, out int minutes)
        {
            if (durationSeconds < 0) durationSeconds = 0;

            hours = durationSeconds / 3600;
            minutes = (int)Math.Round((durationSeconds % 3600) / 60.0);

            if (minutes >= 60)
            {
                hours += 1;
                minutes = 0;
            }

            // Minimal session threshold: at least 30s counts as 1 minute on Backloggd
            if (hours == 0 && minutes == 0 && durationSeconds >= 30)
            {
                minutes = 1;
            }
        }
    }
}
