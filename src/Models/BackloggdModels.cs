using System;
using System.Collections.Generic;

namespace BackloggdAutoSync
{
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
        public string Review { get; set; }
        public bool ReviewSpoilers { get; set; }
        public string StartDate { get; set; }
        public string FinishDate { get; set; }
        public bool IsMaster { get; set; }
        public bool IsReplay { get; set; }
        public int HoursFinished { get; set; }
        public int MinsFinished { get; set; }
        public int HoursMastered { get; set; }
        public int MinsMastered { get; set; }
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

    public class SessionValidationResult
    {
        public bool Valid { get; set; }
        public string CsrfToken { get; set; }
        public string Username { get; set; }
        public string UserId { get; set; }
        public string Error { get; set; }
    }
}
