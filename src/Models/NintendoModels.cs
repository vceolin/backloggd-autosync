using System;

namespace BackloggdAutoSync
{
    public class NintendoLoginChallenge
    {
        public string ClientId { get; set; }
        public string LoginUrl { get; set; }
        public string CodeVerifier { get; set; }
        public string State { get; set; }
    }

    public class NintendoDeviceInfo
    {
        public string DeviceId { get; set; }
        public string Label { get; set; }
        public string SerialNumber { get; set; }

        public override string ToString()
        {
            return string.Format("{0} (ID: {1})", string.IsNullOrEmpty(Label) ? "Nintendo Switch" : Label, DeviceId);
        }
    }

    public class NintendoDailyGameRecord
    {
        public string DeviceId { get; set; }
        public string DeviceLabel { get; set; }
        public string DateStr { get; set; } // YYYY-MM-DD
        public string PlayerNickname { get; set; }
        public string ApplicationId { get; set; } // 16-char hex title ID e.g. 0100152000022000
        public string Title { get; set; }
        public int PlayingTimeMinutes { get; set; }
    }

    public class NintendoAllTimeGameRecord
    {
        public string TitleId { get; set; }
        public string TitleName { get; set; }
        public string Platform { get; set; }
        public string FirstPlayedDate { get; set; } // YYYY-MM-DD
        public string LastPlayedDate { get; set; }  // YYYY-MM-DD
        public int TotalPlayedDays { get; set; }
        public int TotalPlayedMinutes { get; set; }
    }
}
