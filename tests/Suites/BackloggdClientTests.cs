using System;

namespace BackloggdAutoSync.Tests
{
    [TestFixture]
    public class BackloggdClientTests
    {
        [Test]
        public void Should_Normalize_Raw_Cookie()
        {
            Assert.AreEqual("my_raw_cookie_123", BackloggdClient.NormalizeCookie("my_raw_cookie_123"));
            Assert.AreEqual("session_val", BackloggdClient.NormalizeCookie("_backloggd_session=session_val"));
            Assert.AreEqual("session_val", BackloggdClient.NormalizeCookie("Cookie: _backloggd_session=session_val; path=/;"));
            Assert.AreEqual("session_val", BackloggdClient.NormalizeCookie("  \"_backloggd_session=session_val\"  "));
            Assert.AreEqual("", BackloggdClient.NormalizeCookie(""));
            Assert.AreEqual("", BackloggdClient.NormalizeCookie(null));
        }

        [Test]
        public void Should_Escape_Cookie_With_Special_Characters()
        {
            // If cookie contains / or + or = without %, it should be escaped
            string raw = "abc/def+123==";
            string normalized = BackloggdClient.NormalizeCookie(raw);
            Assert.IsTrue(normalized.Contains("%2F") || normalized.Contains("%2B") || normalized.Contains("%3D"));
        }

        [Test]
        public void Should_Extract_Csrf_Token()
        {
            string html1 = "<head><meta name=\"csrf-token\" content=\"test_csrf_token_xyz\" /></head>";
            Assert.AreEqual("test_csrf_token_xyz", BackloggdClient.ExtractCsrfToken(html1));

            string html2 = "<head><meta content=\"reversed_csrf_token_abc\" name=\"csrf-token\" /></head>";
            Assert.AreEqual("reversed_csrf_token_abc", BackloggdClient.ExtractCsrfToken(html2));

            string htmlNoCsrf = "<html><body>No token here</body></html>";
            Assert.IsNull(BackloggdClient.ExtractCsrfToken(htmlNoCsrf));
            Assert.IsNull(BackloggdClient.ExtractCsrfToken(null));
        }

        [Test]
        public void Should_Extract_User_Info()
        {
            string html = "<html><body><a href=\"/u/coolplayer/\">Profile</a><div user_id=\"987654\"></div></body></html>";
            string username, userId;
            BackloggdClient.ExtractUserInfo(html, out username, out userId);

            Assert.AreEqual("coolplayer", username);
            Assert.AreEqual("987654", userId);
        }

        [Test]
        public void Should_Extract_User_Info_From_Quick_Logs()
        {
            string html = "<div username=\"speedrunner\" user_id=\"12345\"></div>";
            string username, userId;
            BackloggdClient.ExtractUserInfo(html, out username, out userId);

            Assert.AreEqual("speedrunner", username);
            Assert.AreEqual("12345", userId);
        }

        [Test]
        public void Should_Compute_Exponential_Backoff_With_Jitter_And_Retry_After()
        {
            int b1 = BackloggdClient.ComputeExponentialBackoffMs(1, 0, 2000, 60000);
            int b2 = BackloggdClient.ComputeExponentialBackoffMs(2, 0, 2000, 60000);
            int b3 = BackloggdClient.ComputeExponentialBackoffMs(3, 0, 2000, 60000);
            int bWithRetryAfter = BackloggdClient.ComputeExponentialBackoffMs(1, 15, 2000, 60000);

            Assert.IsTrue(b1 >= 2100 && b1 < 2800);
            Assert.IsTrue(b2 >= 4100 && b2 < 4800);
            Assert.IsTrue(b3 >= 8100 && b3 < 8800);
            Assert.IsTrue(bWithRetryAfter >= 15100 && bWithRetryAfter < 15800);
        }
    }
}
