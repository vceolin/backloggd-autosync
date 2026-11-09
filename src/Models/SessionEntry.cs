using System;

namespace BackloggdAutoSync
{
    public class SessionEntry
    {
        public string Id { get; set; }
        public string Platform { get; set; } // "Steam", "Switch", "Manual"
        public int AppId { get; set; }
        public string SwitchApplicationId { get; set; }
        public string GameTitle { get; set; }
        public string BackloggdGameId { get; set; }
        public string DateStr { get; set; } // YYYY-MM-DD
        public string StartTimeIso { get; set; }
        public string EndTimeIso { get; set; }
        public int DurationSeconds { get; set; }
        public bool SetExactDayPlaytime { get; set; }
        public int AllTimeTotalMinutes { get; set; }
        public string FirstPlayedDate { get; set; }
        public bool Synced { get; set; }
        public bool AccumulatedOnBackloggd { get; set; }
        public int TotalDayMinutesOnBackloggd { get; set; }
        public string SyncError { get; set; }
        public int RetryCount { get; set; }
    }
}
