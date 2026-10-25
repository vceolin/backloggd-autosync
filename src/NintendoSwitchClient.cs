using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace BackloggdAutoSync
{
    public class NintendoLoginChallenge
    {
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

    public static class NintendoSwitchClient
    {
        private const string ClientId = "54789befb391a838";
        private const string RedirectUri = "npf54789befb391a838://auth";
        private const string GrantType = "urn:ietf:params:oauth:grant-type:jwt-bearer-session-token";
        private const string AuthorizeBaseUrl = "https://accounts.nintendo.com/connect/1.0.0/authorize";
        private const string SessionTokenUrl = "https://accounts.nintendo.com/connect/1.0.0/api/session_token";
        private const string TokenUrl = "https://accounts.nintendo.com/connect/1.0.0/api/token";
        private const string MyAccountUrl = "https://api.accounts.nintendo.com/2.0.0/users/me";
        private const string MoonBaseUrl = "https://app.lp1.znma.srv.nintendo.net";

        private const string MobileAppPkg = "com.nintendo.znma";
        private const string MobileAppVersion = "2.6.0";
        private const string MobileAppBuild = "700";
        private const string OsName = "ANDROID";
        private const string OsVersion = "34";
        private const string DeviceModel = "Pixel 4 XL";
        private static readonly string MoonUserAgent = string.Format(
            "moon_ANDROID/{0} ({1}; build:{2}; {3} {4})",
            MobileAppVersion, MobileAppPkg, MobileAppBuild, OsName, OsVersion);

        private static readonly string[] Scopes = new string[]
        {
            "openid",
            "user",
            "moonUser:administration",
            "moonDevice:create",
            "moonOwnedDevice:administration",
            "moonParentalControlSetting",
            "moonParentalControlSetting:update",
            "moonParentalControlSettingState",
            "moonPairingState",
            "moonSmartDevice:administration",
            "moonDailySummary",
            "moonMonthlySummary"
        };

        static NintendoSwitchClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
        }

        private static string GenerateRandomAlphaString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
            byte[] data = new byte[length];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(data);
            }
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                sb.Append(chars[data[i] % chars.Length]);
            }
            return sb.ToString();
        }

        private static string ComputeCodeChallenge(string verifier)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(verifier));
                string b64 = Convert.ToBase64String(hash);
                return b64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
            }
        }

        public static NintendoLoginChallenge CreateLoginChallenge()
        {
            string verifier = GenerateRandomAlphaString(50);
            string state = GenerateRandomAlphaString(50);
            string challenge = ComputeCodeChallenge(verifier);
            string scopeStr = string.Join("+", Array.ConvertAll(Scopes, s => Uri.EscapeDataString(s)));

            string url = string.Format(
                "{0}?client_id={1}&redirect_uri={2}&response_type=session_token_code&scope={3}&session_token_code_challenge={4}&session_token_code_challenge_method=S256&state={5}&theme=login_form",
                AuthorizeBaseUrl,
                Uri.EscapeDataString(ClientId),
                Uri.EscapeDataString(RedirectUri),
                scopeStr,
                Uri.EscapeDataString(challenge),
                Uri.EscapeDataString(state));

            return new NintendoLoginChallenge
            {
                LoginUrl = url,
                CodeVerifier = verifier,
                State = state
            };
        }

        public static string ExtractSessionTokenCode(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            string s = input.Trim();

            Match m = Regex.Match(s, @"session_token_code=([^&#\s]+)");
            if (m.Success)
            {
                return Uri.UnescapeDataString(m.Groups[1].Value);
            }

            if (!s.Contains("://") && !s.Contains("="))
            {
                return s;
            }

            return null;
        }

        public static string ExchangeCodeForSessionToken(string responseUrlOrCode, string codeVerifier)
        {
            string code = ExtractSessionTokenCode(responseUrlOrCode);
            if (string.IsNullOrEmpty(code))
            {
                throw new Exception("Could not find 'session_token_code' in the pasted link. Make sure you right-clicked 'Select this person' and copied the link address (starts with npf54789befb391a838://auth#...).");
            }

            string body = string.Format(
                "client_id={0}&session_token_code={1}&session_token_code_verifier={2}",
                Uri.EscapeDataString(ClientId),
                Uri.EscapeDataString(code),
                Uri.EscapeDataString(codeVerifier));

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(SessionTokenUrl);
            req.Method = "POST";
            req.ContentType = "application/x-www-form-urlencoded";
            req.Accept = "application/json";
            req.UserAgent = MoonUserAgent;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            byte[] bytes = Encoding.UTF8.GetBytes(body);
            req.ContentLength = bytes.Length;
            using (Stream st = req.GetRequestStream())
            {
                st.Write(bytes, 0, bytes.Length);
            }

            string respJson = ReadResponseText(req);
            var serializer = new JavaScriptSerializer();
            var dict = serializer.Deserialize<Dictionary<string, object>>(respJson);
            if (dict != null && dict.ContainsKey("session_token") && dict["session_token"] != null)
            {
                return Convert.ToString(dict["session_token"]);
            }

            throw new Exception("Nintendo did not return a session_token: " + respJson);
        }

        public static void RefreshTokensAndProfile(string sessionToken, out string idToken, out string accessToken, out string nickname, out string accountId)
        {
            idToken = null;
            accessToken = null;
            nickname = null;
            accountId = null;

            if (string.IsNullOrWhiteSpace(sessionToken))
            {
                throw new Exception("Nintendo session token is empty.");
            }

            var serializer = new JavaScriptSerializer();
            string reqJson = serializer.Serialize(new Dictionary<string, string>
            {
                { "client_id", ClientId },
                { "grant_type", GrantType },
                { "session_token", sessionToken.Trim() }
            });

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(TokenUrl);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Accept = "application/json";
            req.UserAgent = MoonUserAgent;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            byte[] bytes = Encoding.UTF8.GetBytes(reqJson);
            req.ContentLength = bytes.Length;
            using (Stream st = req.GetRequestStream())
            {
                st.Write(bytes, 0, bytes.Length);
            }

            string tokenResp = ReadResponseText(req);
            var tokenDict = serializer.Deserialize<Dictionary<string, object>>(tokenResp);
            if (tokenDict == null || !tokenDict.ContainsKey("id_token") || tokenDict["id_token"] == null)
            {
                throw new Exception("Failed to obtain Nintendo id_token: " + tokenResp);
            }

            idToken = Convert.ToString(tokenDict["id_token"]);
            accessToken = tokenDict.ContainsKey("access_token") && tokenDict["access_token"] != null
                ? Convert.ToString(tokenDict["access_token"])
                : idToken;

            // Fetch Nintendo Account profile info
            try
            {
                HttpWebRequest meReq = (HttpWebRequest)WebRequest.Create(MyAccountUrl);
                meReq.Method = "GET";
                meReq.Accept = "application/json";
                meReq.UserAgent = MoonUserAgent;
                meReq.Headers["Authorization"] = "Bearer " + accessToken;
                meReq.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                string meResp = ReadResponseText(meReq);
                var meDict = serializer.Deserialize<Dictionary<string, object>>(meResp);
                if (meDict != null)
                {
                    if (meDict.ContainsKey("nickname") && meDict["nickname"] != null)
                        nickname = Convert.ToString(meDict["nickname"]);
                    if (meDict.ContainsKey("id") && meDict["id"] != null)
                        accountId = Convert.ToString(meDict["id"]);
                }
            }
            catch
            {
                // Non-fatal if /users/me fails
            }
        }

        private static string SendMoonGet(string url, string idToken)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.UserAgent = MoonUserAgent;
            req.Accept = "application/json";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            req.Headers["X-Moon-App-Id"] = MobileAppPkg;
            req.Headers["X-Moon-Os"] = OsName;
            req.Headers["X-Moon-Os-Version"] = OsVersion;
            req.Headers["X-Moon-Model"] = DeviceModel;
            req.Headers["X-Moon-App-Display-Version"] = MobileAppVersion;
            req.Headers["X-Moon-App-Internal-Version"] = MobileAppBuild;
            req.Headers["X-Moon-TimeZone"] = "America/Sao_Paulo";
            req.Headers["X-Moon-Os-Language"] = "en-US";
            req.Headers["X-Moon-App-Language"] = "en-US";
            req.Headers["Authorization"] = "Bearer " + idToken;

            return ReadResponseText(req);
        }

        public static List<NintendoDeviceInfo> FetchOwnedDevices(string idToken)
        {
            string url = MoonBaseUrl + "/v2/actions/user/fetchOwnedDevices";
            string json = SendMoonGet(url, idToken);

            var list = new List<NintendoDeviceInfo>();
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = serializer.Deserialize<Dictionary<string, object>>(json);
            if (root == null) return list;

            ArrayList items = null;
            if (root.ContainsKey("ownedDevices") && root["ownedDevices"] is ArrayList)
                items = (ArrayList)root["ownedDevices"];
            else if (root.ContainsKey("items") && root["items"] is ArrayList)
                items = (ArrayList)root["items"];

            if (items != null)
            {
                foreach (object obj in items)
                {
                    Dictionary<string, object> d = obj as Dictionary<string, object>;
                    if (d == null) continue;

                    string devId = d.ContainsKey("deviceId") && d["deviceId"] != null ? Convert.ToString(d["deviceId"]) : null;
                    string label = d.ContainsKey("label") && d["label"] != null ? Convert.ToString(d["label"]) : null;
                    string serial = null;

                    Dictionary<string, object> innerDev = d.ContainsKey("device") ? d["device"] as Dictionary<string, object> : null;
                    if (innerDev != null)
                    {
                        if (string.IsNullOrEmpty(devId) && innerDev.ContainsKey("id") && innerDev["id"] != null)
                            devId = Convert.ToString(innerDev["id"]);
                        if (string.IsNullOrEmpty(label) && innerDev.ContainsKey("externalNickname") && innerDev["externalNickname"] != null)
                            label = Convert.ToString(innerDev["externalNickname"]);
                        if (innerDev.ContainsKey("serialNumber") && innerDev["serialNumber"] != null)
                            serial = Convert.ToString(innerDev["serialNumber"]);
                    }

                    if (!string.IsNullOrEmpty(devId))
                    {
                        list.Add(new NintendoDeviceInfo
                        {
                            DeviceId = devId,
                            Label = string.IsNullOrEmpty(label) ? "Nintendo Switch" : label,
                            SerialNumber = serial
                        });
                    }
                }
            }

            return list;
        }

        public static List<NintendoDailyGameRecord> FetchDailySummaries(string idToken, NintendoDeviceInfo device, int maxDaysBack = 14)
        {
            var records = new List<NintendoDailyGameRecord>();
            if (device == null || string.IsNullOrEmpty(device.DeviceId)) return records;

            string url = MoonBaseUrl + "/v2/actions/playSummary/fetchDailySummaries?deviceId=" + Uri.EscapeDataString(device.DeviceId);
            string json = SendMoonGet(url, idToken);

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = serializer.Deserialize<Dictionary<string, object>>(json);
            if (root == null) return records;

            ArrayList summaries = null;
            if (root.ContainsKey("dailySummaries") && root["dailySummaries"] is ArrayList)
                summaries = (ArrayList)root["dailySummaries"];
            else if (root.ContainsKey("items") && root["items"] is ArrayList)
                summaries = (ArrayList)root["items"];

            if (summaries == null) return records;

            DateTime cutoffDate = DateTime.Today.AddDays(-Math.Max(1, maxDaysBack));

            foreach (object sObj in summaries)
            {
                Dictionary<string, object> dayDict = sObj as Dictionary<string, object>;
                if (dayDict == null) continue;

                string dateStr = dayDict.ContainsKey("date") && dayDict["date"] != null ? Convert.ToString(dayDict["date"]) : null;
                if (string.IsNullOrEmpty(dateStr)) continue;

                DateTime parsedDate;
                if (DateTime.TryParse(dateStr, out parsedDate) && parsedDate < cutoffDate)
                {
                    continue;
                }

                // Aggregate minutes per ApplicationId on this date across players on the console
                var gameMinutesByApp = new Dictionary<string, NintendoDailyGameRecord>(StringComparer.OrdinalIgnoreCase);

                ArrayList players = dayDict.ContainsKey("players") ? dayDict["players"] as ArrayList : null;
                if (players != null)
                {
                    foreach (object pObj in players)
                    {
                        Dictionary<string, object> playerDict = pObj as Dictionary<string, object>;
                        if (playerDict == null) continue;

                        string playerNick = "";
                        Dictionary<string, object> prof = playerDict.ContainsKey("profile") ? playerDict["profile"] as Dictionary<string, object> : null;
                        if (prof != null && prof.ContainsKey("nickname") && prof["nickname"] != null)
                        {
                            playerNick = Convert.ToString(prof["nickname"]);
                        }

                        ArrayList playedGames = playerDict.ContainsKey("playedGames") ? playerDict["playedGames"] as ArrayList : null;
                        if (playedGames == null) continue;

                        foreach (object gObj in playedGames)
                        {
                            Dictionary<string, object> gDict = gObj as Dictionary<string, object>;
                            if (gDict == null) continue;

                            int mins = gDict.ContainsKey("playingTime") && gDict["playingTime"] != null
                                ? Convert.ToInt32(gDict["playingTime"])
                                : 0;
                            if (mins <= 0) continue;

                            Dictionary<string, object> meta = gDict.ContainsKey("meta") ? gDict["meta"] as Dictionary<string, object> : null;
                            string appId = meta != null && meta.ContainsKey("applicationId") && meta["applicationId"] != null
                                ? Convert.ToString(meta["applicationId"])
                                : "";
                            string title = meta != null && meta.ContainsKey("title") && meta["title"] != null
                                ? Convert.ToString(meta["title"]).Trim()
                                : "Switch Game";

                            string key = !string.IsNullOrEmpty(appId) ? appId : title;
                            if (gameMinutesByApp.ContainsKey(key))
                            {
                                gameMinutesByApp[key].PlayingTimeMinutes += mins;
                            }
                            else
                            {
                                gameMinutesByApp[key] = new NintendoDailyGameRecord
                                {
                                    DeviceId = device.DeviceId,
                                    DeviceLabel = device.Label,
                                    DateStr = dateStr,
                                    PlayerNickname = playerNick,
                                    ApplicationId = appId,
                                    Title = title,
                                    PlayingTimeMinutes = mins
                                };
                            }
                        }
                    }
                }

                foreach (var rec in gameMinutesByApp.Values)
                {
                    records.Add(rec);
                }
            }

            return records;
        }

        public static List<NintendoDailyGameRecord> FetchAllHistoricalMonthlySummaries(string idToken, NintendoDeviceInfo device, out int monthsFetched)
        {
            monthsFetched = 0;
            var recordsByDateAndApp = new Dictionary<string, NintendoDailyGameRecord>(StringComparer.OrdinalIgnoreCase);
            if (device == null || string.IsNullOrEmpty(device.DeviceId)) return new List<NintendoDailyGameRecord>();

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var appTitleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 1. Fetch latest monthly summary + list of all available historical months
            try
            {
                string latestUrl = MoonBaseUrl + "/v2/actions/playSummary/fetchLatestMonthlySummary?deviceId=" + Uri.EscapeDataString(device.DeviceId);
                string latestJson = SendMoonGet(latestUrl, idToken);
                var latestRoot = serializer.Deserialize<Dictionary<string, object>>(latestJson);
                if (latestRoot != null)
                {
                    int latestYear = latestRoot.ContainsKey("year") && latestRoot["year"] != null ? Convert.ToInt32(latestRoot["year"]) : 0;
                    int latestMonth = latestRoot.ContainsKey("month") && latestRoot["month"] != null ? Convert.ToInt32(latestRoot["month"]) : 0;

                    ParseMonthlySummaryObject(latestRoot, device, appTitleMap, recordsByDateAndApp);
                    monthsFetched++;

                    ArrayList available = latestRoot.ContainsKey("available") ? latestRoot["available"] as ArrayList : null;
                    if (available != null)
                    {
                        foreach (object availObj in available)
                        {
                            Dictionary<string, object> ym = availObj as Dictionary<string, object>;
                            if (ym == null) continue;

                            int y = ym.ContainsKey("year") && ym["year"] != null ? Convert.ToInt32(ym["year"]) : 0;
                            int m = ym.ContainsKey("month") && ym["month"] != null ? Convert.ToInt32(ym["month"]) : 0;
                            if (y <= 0 || m <= 0) continue;
                            if (y == latestYear && m == latestMonth) continue; // Already parsed from fetchLatestMonthlySummary

                            try
                            {
                                string monthUrl = string.Format(
                                    "{0}/v2/actions/playSummary/fetchMonthlySummary?deviceId={1}&year={2}&month={3}&containLatest=false",
                                    MoonBaseUrl, Uri.EscapeDataString(device.DeviceId), y, m);
                                string monthJson = SendMoonGet(monthUrl, idToken);
                                var monthRoot = serializer.Deserialize<Dictionary<string, object>>(monthJson);
                                if (monthRoot != null)
                                {
                                    ParseMonthlySummaryObject(monthRoot, device, appTitleMap, recordsByDateAndApp);
                                    monthsFetched++;
                                }
                            }
                            catch
                            {
                                // Continue fetching remaining available months if one month fails
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore if console has no completed monthly summaries yet
            }

            // 2. Also merge all available daily summaries (covers current in-progress month + recent 60 days)
            try
            {
                var recentDaily = FetchDailySummaries(idToken, device, 3650);
                foreach (var rec in recentDaily)
                {
                    if (!string.IsNullOrEmpty(rec.ApplicationId) && !string.IsNullOrEmpty(rec.Title))
                    {
                        appTitleMap[rec.ApplicationId] = rec.Title;
                    }
                    string key = rec.DateStr + "|" + (!string.IsNullOrEmpty(rec.ApplicationId) ? rec.ApplicationId : rec.Title);
                    if (!recordsByDateAndApp.ContainsKey(key) || rec.PlayingTimeMinutes > recordsByDateAndApp[key].PlayingTimeMinutes)
                    {
                        recordsByDateAndApp[key] = rec;
                    }
                }
            }
            catch { }

            foreach (var rec in recordsByDateAndApp.Values)
            {
                if (!string.IsNullOrEmpty(rec.ApplicationId) && appTitleMap.ContainsKey(rec.ApplicationId))
                {
                    rec.Title = appTitleMap[rec.ApplicationId];
                }
            }

            return new List<NintendoDailyGameRecord>(recordsByDateAndApp.Values);
        }

        private static void ParseMonthlySummaryObject(
            Dictionary<string, object> root,
            NintendoDeviceInfo device,
            Dictionary<string, string> appTitleMap,
            Dictionary<string, NintendoDailyGameRecord> recordsByDateAndApp)
        {
            Dictionary<string, object> summary = root.ContainsKey("summary") ? root["summary"] as Dictionary<string, object> : null;
            if (summary == null) return;

            Dictionary<string, object> overall = summary.ContainsKey("overall") ? summary["overall"] as Dictionary<string, object> : null;
            if (overall != null)
            {
                ExtractTitlesFromRanking(overall.ContainsKey("ranking") ? overall["ranking"] as ArrayList : null, appTitleMap);
            }

            ArrayList players = summary.ContainsKey("players") ? summary["players"] as ArrayList : null;
            if (players != null)
            {
                foreach (object pObj in players)
                {
                    Dictionary<string, object> pDict = pObj as Dictionary<string, object>;
                    if (pDict == null) continue;
                    Dictionary<string, object> pSum = pDict.ContainsKey("summary") ? pDict["summary"] as Dictionary<string, object> : null;
                    if (pSum != null)
                    {
                        ExtractTitlesFromRanking(pSum.ContainsKey("ranking") ? pSum["ranking"] as ArrayList : null, appTitleMap);
                    }
                }
            }

            if (overall == null) return;
            ArrayList dailyStats = overall.ContainsKey("dailyStats") ? overall["dailyStats"] as ArrayList : null;
            if (dailyStats == null) return;

            foreach (object dsObj in dailyStats)
            {
                Dictionary<string, object> dayStat = dsObj as Dictionary<string, object>;
                if (dayStat == null) continue;

                string dateStr = dayStat.ContainsKey("date") && dayStat["date"] != null ? Convert.ToString(dayStat["date"]) : null;
                if (string.IsNullOrEmpty(dateStr)) continue;

                Dictionary<string, object> gamesDict = dayStat.ContainsKey("games") ? dayStat["games"] as Dictionary<string, object> : null;
                if (gamesDict == null || gamesDict.Count == 0) continue;

                foreach (var kv in gamesDict)
                {
                    string appId = kv.Key;
                    Dictionary<string, object> gStat = kv.Value as Dictionary<string, object>;
                    if (gStat == null) continue;

                    int mins = gStat.ContainsKey("totalTime") && gStat["totalTime"] != null
                        ? Convert.ToInt32(gStat["totalTime"])
                        : 0;
                    if (mins <= 0) continue;

                    string title = appTitleMap.ContainsKey(appId) ? appTitleMap[appId] : ("Switch Title " + appId);
                    string recordKey = dateStr + "|" + appId;

                    if (!recordsByDateAndApp.ContainsKey(recordKey) || mins > recordsByDateAndApp[recordKey].PlayingTimeMinutes)
                    {
                        recordsByDateAndApp[recordKey] = new NintendoDailyGameRecord
                        {
                            DeviceId = device.DeviceId,
                            DeviceLabel = device.Label,
                            DateStr = dateStr,
                            PlayerNickname = "",
                            ApplicationId = appId,
                            Title = title,
                            PlayingTimeMinutes = mins
                        };
                    }
                }
            }
        }

        private static void ExtractTitlesFromRanking(ArrayList ranking, Dictionary<string, string> appTitleMap)
        {
            if (ranking == null) return;
            foreach (object rObj in ranking)
            {
                Dictionary<string, object> rDict = rObj as Dictionary<string, object>;
                if (rDict == null) continue;
                Dictionary<string, object> stat = rDict.ContainsKey("stat") ? rDict["stat"] as Dictionary<string, object> : null;
                if (stat == null) continue;
                Dictionary<string, object> meta = stat.ContainsKey("meta") ? stat["meta"] as Dictionary<string, object> : null;
                if (meta == null) continue;

                string appId = meta.ContainsKey("applicationId") && meta["applicationId"] != null ? Convert.ToString(meta["applicationId"]) : null;
                string title = meta.ContainsKey("title") && meta["title"] != null ? Convert.ToString(meta["title"]).Trim() : null;
                if (!string.IsNullOrEmpty(appId) && !string.IsNullOrEmpty(title))
                {
                    appTitleMap[appId] = title;
                }
            }
        }

        public static int GetStableSwitchAppId(string switchApplicationId, string title)
        {
            // Generate a deterministic negative integer AppId for Switch titles so they never collide with positive Steam AppIDs
            string seed = !string.IsNullOrEmpty(switchApplicationId) ? switchApplicationId.ToUpperInvariant() : (title ?? "").ToLowerInvariant();
            int hash = 5381;
            for (int i = 0; i < seed.Length; i++)
            {
                hash = ((hash << 5) + hash) ^ seed[i];
            }
            int positive = Math.Abs(hash == int.MinValue ? 100000 : hash);
            if (positive < 1000) positive += 1000;
            return -positive;
        }

        private static string ReadResponseText(HttpWebRequest req)
        {
            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }
            catch (WebException wex)
            {
                HttpWebResponse errResp = wex.Response as HttpWebResponse;
                if (errResp != null)
                {
                    string errBody = "";
                    try
                    {
                        using (StreamReader sr = new StreamReader(errResp.GetResponseStream(), Encoding.UTF8))
                        {
                            errBody = sr.ReadToEnd();
                        }
                    }
                    catch { }
                    throw new Exception(string.Format("Nintendo API returned HTTP {0}: {1}", (int)errResp.StatusCode, errBody));
                }
                throw;
            }
        }
    }
}
