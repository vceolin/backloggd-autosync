using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace BackloggdAutoSync
{
    public class SessionValidationResult
    {
        public bool Valid { get; set; }
        public string CsrfToken { get; set; }
        public string Username { get; set; }
        public string UserId { get; set; }
        public string Error { get; set; }
    }

    public static class BackloggdClient
    {
        private const string BaseUrl = "https://backloggd.com";
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

        static BackloggdClient()
        {
            // Ensure TLS 1.2 is enabled on .NET Framework 4.x
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
        }

        public static string NormalizeCookie(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string c = raw.Trim();

            Match sessionMatch = Regex.Match(c, @"(?:^|;\s*)_backloggd_session=([^;]+)");
            if (sessionMatch.Success)
            {
                c = sessionMatch.Groups[1].Value.Trim();
            }
            else
            {
                c = Regex.Replace(c, @"^(?:Cookie:\s*)?(?:_backloggd_session=)?", "", RegexOptions.IgnoreCase);
                int semiIdx = c.IndexOf(';');
                if (semiIdx >= 0) c = c.Substring(0, semiIdx).Trim();
            }

            c = c.Trim('"', '\'').Trim();

            if (!c.Contains("%") && (c.Contains("/") || c.Contains("+") || c.Contains("=")))
            {
                c = Uri.EscapeDataString(c);
            }

            return c;
        }

        public static string ExtractCsrfToken(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            Match m = Regex.Match(html, @"<meta\s+name=[""']csrf-token[""']\s+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                m = Regex.Match(html, @"content=[""']([^""']+)[""']\s+name=[""']csrf-token[""']", RegexOptions.IgnoreCase);
            }
            return m.Success ? m.Groups[1].Value : null;
        }

        public static void ExtractUserInfo(string html, out string username, out string userId)
        {
            username = null;
            userId = null;
            if (string.IsNullOrEmpty(html)) return;

            Match profileMatch = Regex.Match(html, @"href=[""']/u/([a-zA-Z0-9_-]+)/[""'][^>]*>Profile</a>", RegexOptions.IgnoreCase);
            if (profileMatch.Success) username = profileMatch.Groups[1].Value;

            if (string.IsNullOrEmpty(username))
            {
                Match myProfileMatch = Regex.Match(html, @"href=[""']/u/([a-zA-Z0-9_-]+)[""'][^>]*>\s*<i[^>]*></i>\s*My Profile", RegexOptions.IgnoreCase);
                if (myProfileMatch.Success) username = myProfileMatch.Groups[1].Value;
            }

            if (string.IsNullOrEmpty(username))
            {
                Match quickLogsMatch = Regex.Match(html, @"username=[""']([a-zA-Z0-9_-]+)[""']", RegexOptions.IgnoreCase);
                if (quickLogsMatch.Success) username = quickLogsMatch.Groups[1].Value;
            }

            Match uidMatch = Regex.Match(html, @"user_id=[""'](\d+)[""']", RegexOptions.IgnoreCase);
            if (uidMatch.Success) userId = uidMatch.Groups[1].Value;
        }

        private class HttpResp
        {
            public int StatusCode;
            public string Body;
            public string Error;
            public bool Ok { get { return StatusCode >= 200 && StatusCode < 300; } }
        }

        private static HttpResp SendRequest(string url, string method, string cleanCookie, Dictionary<string, string> extraHeaders, string body = null)
        {
            var resp = new HttpResp();
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = method;
                req.UserAgent = UserAgent;
                req.Timeout = 20000;
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                req.Headers["Accept-Language"] = "en-US,en;q=0.9";
                req.Headers["Upgrade-Insecure-Requests"] = "1";
                if (!string.IsNullOrEmpty(cleanCookie))
                {
                    req.Headers["Cookie"] = "_backloggd_session=" + cleanCookie;
                }

                if (extraHeaders != null)
                {
                    foreach (var kv in extraHeaders)
                    {
                        if (string.Equals(kv.Key, "Accept", StringComparison.OrdinalIgnoreCase))
                            req.Accept = kv.Value;
                        else if (string.Equals(kv.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                            req.ContentType = kv.Value;
                        else
                            req.Headers[kv.Key] = kv.Value;
                    }
                }

                if (body != null)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    req.ContentLength = bytes.Length;
                    using (Stream reqStream = req.GetRequestStream())
                    {
                        reqStream.Write(bytes, 0, bytes.Length);
                    }
                }

                using (HttpWebResponse webResp = (HttpWebResponse)req.GetResponse())
                {
                    resp.StatusCode = (int)webResp.StatusCode;
                    using (StreamReader sr = new StreamReader(webResp.GetResponseStream(), Encoding.UTF8))
                    {
                        resp.Body = sr.ReadToEnd();
                    }
                }
            }
            catch (WebException wex)
            {
                HttpWebResponse errResp = wex.Response as HttpWebResponse;
                if (errResp != null)
                {
                    resp.StatusCode = (int)errResp.StatusCode;
                    try
                    {
                        using (StreamReader sr = new StreamReader(errResp.GetResponseStream(), Encoding.UTF8))
                        {
                            resp.Body = sr.ReadToEnd();
                        }
                    }
                    catch { }
                }
                else
                {
                    resp.StatusCode = 0;
                    resp.Error = wex.Message;
                }
            }
            catch (Exception ex)
            {
                resp.StatusCode = 0;
                resp.Error = ex.Message;
            }
            return resp;
        }

        public static string FetchFreshCsrfToken(string cookie)
        {
            string cleanCookie = NormalizeCookie(cookie);
            var res = SendRequest(BaseUrl + "/", "GET", cleanCookie, null);
            if (res.Ok)
            {
                return ExtractCsrfToken(res.Body);
            }
            return null;
        }

        public static SessionValidationResult ValidateSession(string rawCookie)
        {
            var result = new SessionValidationResult();
            if (string.IsNullOrWhiteSpace(rawCookie))
            {
                result.Error = "Session cookie is empty.";
                return result;
            }

            string cleanCookie = NormalizeCookie(rawCookie);
            var res = SendRequest(BaseUrl + "/", "GET", cleanCookie, null);
            if (res.StatusCode == 403)
            {
                result.Error = "Backloggd returned 403 (BunnyCDN bot check or expired session). Try again in a few seconds.";
                return result;
            }
            if (!res.Ok)
            {
                result.Error = string.IsNullOrEmpty(res.Error)
                    ? "Backloggd returned HTTP " + res.StatusCode
                    : "Network error: " + res.Error;
                return result;
            }

            string html = res.Body ?? "";
            if (html.Contains("bunny-shield") || html.Contains("Establishing a secure connection"))
            {
                result.Error = "Backloggd showed a CDN shield check. Please try again in a moment.";
                return result;
            }

            if (html.Contains("/users/sign_in") && !html.Contains("/u/"))
            {
                result.Error = "Session cookie is invalid or expired (not logged in).";
                return result;
            }

            string csrf = ExtractCsrfToken(html);
            if (string.IsNullOrEmpty(csrf))
            {
                result.Error = "Could not extract CSRF token from Backloggd.";
                return result;
            }

            string username, userId;
            ExtractUserInfo(html, out username, out userId);

            if (string.IsNullOrEmpty(userId))
            {
                var settingsRes = SendRequest(BaseUrl + "/settings/", "GET", cleanCookie, null);
                if (settingsRes.Ok)
                {
                    string u2, id2;
                    ExtractUserInfo(settingsRes.Body, out u2, out id2);
                    if (!string.IsNullOrEmpty(id2)) userId = id2;
                    if (string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(u2)) username = u2;
                }
            }

            if (string.IsNullOrEmpty(username))
            {
                result.Error = "Cookie is valid for Backloggd guest page, but no logged-in user profile was found. Please verify your _backloggd_session cookie.";
                return result;
            }

            result.Valid = true;
            result.CsrfToken = csrf;
            result.Username = username;
            result.UserId = userId;
            return result;
        }

        public static List<GameSearchResult> SearchGame(string title, AppSettings settings)
        {
            var results = new List<GameSearchResult>();
            if (string.IsNullOrWhiteSpace(title)) return results;

            string cleanCookie = NormalizeCookie(settings.SessionCookie);
            string url = BaseUrl + "/search/autocomplete/?query=" + Uri.EscapeDataString(title.Trim());

            var headers = new Dictionary<string, string>
            {
                { "Accept", "text/vnd.turbo-stream.html" },
                { "X-Requested-With", "XMLHttpRequest" }
            };
            if (!string.IsNullOrEmpty(settings.CsrfToken))
            {
                headers["X-CSRF-Token"] = settings.CsrfToken;
            }

            var res = SendRequest(url, "GET", cleanCookie, headers);
            if (!res.Ok)
            {
                throw new Exception("Backloggd search returned HTTP " + res.StatusCode);
            }

            string html = res.Body ?? "";
            MatchCollection matches = Regex.Matches(html, @"<a\s+href=[""']/native/log/edit/(\d+)/?[""'][^>]*>([\s\S]*?)</a>", RegexOptions.IgnoreCase);
            foreach (Match m in matches)
            {
                string gameId = m.Groups[1].Value;
                string block = m.Groups[2].Value;

                Match titleMatch = Regex.Match(block, @"<p\s+class=[""'][^""']*game-title[^""']*[""']>([^<]+)</p>", RegexOptions.IgnoreCase);
                Match yearMatch = Regex.Match(block, @"<p\s+class=[""'][^""']*subtitle-text[^""']*[""']>([^<]*)</p>", RegexOptions.IgnoreCase);

                if (titleMatch.Success)
                {
                    string decodedTitle = WebUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());
                    string year = yearMatch.Success ? yearMatch.Groups[1].Value.Trim() : "";
                    results.Add(new GameSearchResult
                    {
                        GameId = gameId,
                        Title = decodedTitle,
                        ReleaseYear = year
                    });
                }
            }

            return results;
        }

        public static string CleanGameTitle(string rawTitle)
        {
            if (string.IsNullOrWhiteSpace(rawTitle)) return "";
            string t = Regex.Replace(rawTitle, @"[™®©]", "");
            t = Regex.Replace(t, @"\s*-\s*(?:Digital\s+)?(?:Deluxe|Collector's|Definitive|Game of the Year|GOTY|Enhanced|Special|Director's Cut|Remastered|Complete|Ultimate|Standard|Edition).*$", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\s*\((?:Standard|Deluxe|Definitive|Collector's|Enhanced|Remastered|GOTY)\s+Edition\)", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\s+", " ").Trim();
            return t;
        }

        public static GameSearchResult SelectBestMatch(string targetTitle, List<GameSearchResult> results)
        {
            if (results == null || results.Count == 0) return null;

            string normTarget = Regex.Replace(targetTitle.ToLowerInvariant(), @"[^a-z0-9]", "");
            if (string.IsNullOrEmpty(normTarget)) return null;

            // 1. Exact normalized match
            foreach (var r in results)
            {
                string normRes = Regex.Replace(r.Title.ToLowerInvariant(), @"[^a-z0-9]", "");
                if (normRes == normTarget) return r;
            }

            // 2. Prefix match
            foreach (var r in results)
            {
                string normRes = Regex.Replace(r.Title.ToLowerInvariant(), @"[^a-z0-9]", "");
                if (!string.IsNullOrEmpty(normRes) && (normRes.StartsWith(normTarget) || normTarget.StartsWith(normRes)))
                {
                    return r;
                }
            }

            // If only 1 result came back from Backloggd autocomplete, use it
            if (results.Count == 1) return results[0];

            return null;
        }

        public static GameLogStatus GetGameLogStatus(string gameId, AppSettings settings)
        {
            string cleanCookie = NormalizeCookie(settings.SessionCookie);
            string url = BaseUrl + "/log/edit/" + gameId;

            var headers = new Dictionary<string, string>
            {
                { "Accept", "application/json" },
                { "X-Requested-With", "XMLHttpRequest" }
            };
            if (!string.IsNullOrEmpty(settings.CsrfToken))
            {
                headers["X-CSRF-Token"] = settings.CsrfToken;
            }

            var res = SendRequest(url, "GET", cleanCookie, headers);
            if (res.StatusCode == 401 || res.StatusCode == 403 || res.StatusCode == 422)
            {
                string freshCsrf = FetchFreshCsrfToken(cleanCookie);
                if (!string.IsNullOrEmpty(freshCsrf))
                {
                    settings.CsrfToken = freshCsrf;
                    DataStore.SaveSettings(settings);
                    headers["X-CSRF-Token"] = freshCsrf;
                    res = SendRequest(url, "GET", cleanCookie, headers);
                }
            }

            if (!res.Ok)
            {
                throw new Exception(string.Format("Failed to fetch log status for game {0} (HTTP {1})", gameId, res.StatusCode));
            }

            var serializer = new JavaScriptSerializer();
            var data = serializer.Deserialize<Dictionary<string, object>>(res.Body);
            var status = new GameLogStatus();

            if (data == null) return status;

            Dictionary<string, object> gameLogDict = data.ContainsKey("game_log") ? data["game_log"] as Dictionary<string, object> : null;
            if (gameLogDict != null)
            {
                if (gameLogDict.ContainsKey("id") && gameLogDict["id"] != null)
                    status.GameLogId = Convert.ToInt32(gameLogDict["id"]);
                if (gameLogDict.ContainsKey("status") && gameLogDict["status"] != null)
                    status.Status = Convert.ToString(gameLogDict["status"]);
                if (gameLogDict.ContainsKey("is_play") && gameLogDict["is_play"] != null)
                    status.IsPlay = Convert.ToBoolean(gameLogDict["is_play"]);
                if (gameLogDict.ContainsKey("is_playing") && gameLogDict["is_playing"] != null)
                    status.IsPlaying = Convert.ToBoolean(gameLogDict["is_playing"]);
                if (gameLogDict.ContainsKey("is_backlog") && gameLogDict["is_backlog"] != null)
                    status.IsBacklog = Convert.ToBoolean(gameLogDict["is_backlog"]);
                if (gameLogDict.ContainsKey("is_wishlist") && gameLogDict["is_wishlist"] != null)
                    status.IsWishlist = Convert.ToBoolean(gameLogDict["is_wishlist"]);
                if (gameLogDict.ContainsKey("game_liked") && gameLogDict["game_liked"] != null)
                    status.GameLiked = Convert.ToBoolean(gameLogDict["game_liked"]);
            }

            string mostRecentPtId = null;
            if (data.ContainsKey("most_recent_playthrough_id") && data["most_recent_playthrough_id"] != null)
            {
                mostRecentPtId = Convert.ToString(data["most_recent_playthrough_id"]);
            }

            Dictionary<string, object> ptsDict = data.ContainsKey("playthroughs") ? data["playthroughs"] as Dictionary<string, object> : null;
            Dictionary<string, object> pt = (!string.IsNullOrEmpty(mostRecentPtId) && ptsDict != null && ptsDict.ContainsKey(mostRecentPtId))
                ? ptsDict[mostRecentPtId] as Dictionary<string, object>
                : null;

            if (pt != null)
            {
                status.IsLogged = true;
                status.PlaythroughId = ToIntSafe(mostRecentPtId, -1);
                if (pt.ContainsKey("title") && pt["title"] != null)
                    status.PlaythroughTitle = Convert.ToString(pt["title"]);
                if (pt.ContainsKey("platform") && pt["platform"] != null)
                    status.PlatformId = ToIntSafe(pt["platform"], 6);
                if (pt.ContainsKey("rating") && pt["rating"] != null)
                    status.Rating = ToIntSafe(pt["rating"], 0);
                if (pt.ContainsKey("review") && pt["review"] != null)
                    status.Review = Convert.ToString(pt["review"]);
                if (pt.ContainsKey("review_spoilers") && pt["review_spoilers"] != null)
                    status.ReviewSpoilers = Convert.ToBoolean(pt["review_spoilers"]);
                if (pt.ContainsKey("start_date") && pt["start_date"] != null)
                    status.StartDate = Convert.ToString(pt["start_date"]);
                if (pt.ContainsKey("finish_date") && pt["finish_date"] != null)
                    status.FinishDate = Convert.ToString(pt["finish_date"]);
                if (pt.ContainsKey("is_master") && pt["is_master"] != null)
                    status.IsMaster = Convert.ToBoolean(pt["is_master"]);
                if (pt.ContainsKey("is_replay") && pt["is_replay"] != null)
                    status.IsReplay = Convert.ToBoolean(pt["is_replay"]);
                if (pt.ContainsKey("hours_finished") && pt["hours_finished"] != null)
                    status.HoursFinished = ToIntSafe(pt["hours_finished"], 0);
                if (pt.ContainsKey("mins_finished") && pt["mins_finished"] != null)
                    status.MinsFinished = ToIntSafe(pt["mins_finished"], 0);
                if (pt.ContainsKey("hours_mastered") && pt["hours_mastered"] != null)
                    status.HoursMastered = ToIntSafe(pt["hours_mastered"], 0);
                if (pt.ContainsKey("mins_mastered") && pt["mins_mastered"] != null)
                    status.MinsMastered = ToIntSafe(pt["mins_mastered"], 0);
                if (pt.ContainsKey("hours_played") && pt["hours_played"] != null)
                    status.HoursPlayed = ToIntSafe(pt["hours_played"], 0);
                if (pt.ContainsKey("mins_played") && pt["mins_played"] != null)
                    status.MinsPlayed = ToIntSafe(pt["mins_played"], 0);

                ArrayList playDatesList = pt.ContainsKey("play_dates") ? pt["play_dates"] as ArrayList : null;
                if (playDatesList != null)
                {
                    foreach (object item in playDatesList)
                    {
                        Dictionary<string, object> pd = item as Dictionary<string, object>;
                        if (pd != null)
                        {
                            var entry = new PlayDateEntry
                            {
                                id = pd.ContainsKey("id") && pd["id"] != null ? ToIntSafe(pd["id"], -1) : -1,
                                range_start_date = pd.ContainsKey("range_start_date") && pd["range_start_date"] != null ? Convert.ToString(pd["range_start_date"]) : "",
                                range_end_date = pd.ContainsKey("range_end_date") && pd["range_end_date"] != null ? Convert.ToString(pd["range_end_date"]) : "",
                                hours = pd.ContainsKey("hours") && pd["hours"] != null ? ToIntSafe(pd["hours"], 0) : 0,
                                minutes = pd.ContainsKey("minutes") && pd["minutes"] != null ? ToIntSafe(pd["minutes"], 0) : 0,
                                status = pd.ContainsKey("status") ? pd["status"] : null,
                                note = pd.ContainsKey("note") && pd["note"] != null ? Convert.ToString(pd["note"]) : "",
                                edited = false,
                                start_date = pd.ContainsKey("start_date") && pd["start_date"] != null ? Convert.ToString(pd["start_date"]) : null,
                                finish_date = pd.ContainsKey("finish_date") && pd["finish_date"] != null ? Convert.ToString(pd["finish_date"]) : null,
                                privacy = pd.ContainsKey("privacy") ? pd["privacy"] : null
                            };
                            ArrayList tagsArr = pd.ContainsKey("tags") ? pd["tags"] as ArrayList : null;
                            if (tagsArr != null)
                            {
                                foreach (object t in tagsArr)
                                {
                                    if (t != null) entry.tags.Add(Convert.ToString(t));
                                }
                            }
                            status.PlayDates.Add(entry);
                        }
                    }
                }
            }

            return status;
        }

        private static int ToIntSafe(object val, int fallback)
        {
            if (val == null) return fallback;
            try
            {
                return (int)Math.Round(Convert.ToDouble(val));
            }
            catch
            {
                return fallback;
            }
        }

        public static SyncResult SyncSession(string gameId, int durationSec, string dateStr, AppSettings settings, string note = null, int defaultPlatformId = 6, bool setExactDayTotal = false)
        {
            var result = new SyncResult
            {
                GameId = gameId,
                DateStr = dateStr
            };

            DateTime parsedDate;
            if (!DateTime.TryParse(dateStr, out parsedDate))
            {
                parsedDate = DateTime.Today;
                dateStr = parsedDate.ToString("yyyy-MM-dd");
            }
            string nextDayStr = parsedDate.AddDays(1).ToString("yyyy-MM-dd");

            int addHours = durationSec / 3600;
            int addMins = (int)Math.Round((durationSec % 3600) / 60.0);
            if (addMins == 60)
            {
                addHours += 1;
                addMins = 0;
            }
            if (addHours == 0 && addMins == 0 && durationSec >= 30)
            {
                addMins = 1;
            }

            GameLogStatus logStatus = GetGameLogStatus(gameId, settings);

            string defaultPtTitle = defaultPlatformId == 130 ? "Nintendo Switch" : "Windows PC";
            int playthroughId;
            string playthroughTitle;
            int platformId = (logStatus.IsLogged && logStatus.PlatformId > 0) ? logStatus.PlatformId : defaultPlatformId;
            int rating = logStatus.Rating;
            List<PlayDateEntry> existingDates = new List<PlayDateEntry>(logStatus.PlayDates);
            bool isNewLog = false;
            bool accumulated = false;
            int finalDayHours = addHours;
            int finalDayMins = addMins;

            if (logStatus.IsLogged && logStatus.PlaythroughId.HasValue)
            {
                playthroughId = logStatus.PlaythroughId.Value;
                playthroughTitle = !string.IsNullOrEmpty(logStatus.PlaythroughTitle) ? logStatus.PlaythroughTitle : defaultPtTitle;

                int existingIdx = existingDates.FindIndex(d => d.range_start_date == dateStr);
                if (existingIdx >= 0)
                {
                    PlayDateEntry existing = existingDates[existingIdx];
                    int totalMinutes = setExactDayTotal
                        ? Math.Max(existing.hours * 60 + existing.minutes, addHours * 60 + addMins)
                        : (existing.hours * 60 + existing.minutes + addHours * 60 + addMins);
                    finalDayHours = totalMinutes / 60;
                    finalDayMins = totalMinutes % 60;

                    existing.hours = finalDayHours;
                    existing.minutes = finalDayMins;
                    existing.edited = true;
                    if (!string.IsNullOrEmpty(note))
                    {
                        existing.note = string.IsNullOrEmpty(existing.note) ? note : (existing.note + "; " + note);
                    }
                    accumulated = !setExactDayTotal;
                }
                else
                {
                    existingDates.Add(new PlayDateEntry
                    {
                        id = -(existingDates.Count + 1),
                        range_start_date = dateStr,
                        range_end_date = nextDayStr,
                        hours = addHours,
                        minutes = addMins,
                        status = 5,
                        note = note ?? "",
                        edited = true,
                        start_date = null,
                        finish_date = null,
                        privacy = null,
                        tags = new List<string>()
                    });
                }
            }
            else
            {
                isNewLog = true;
                playthroughId = -1;
                playthroughTitle = defaultPtTitle;
                existingDates.Add(new PlayDateEntry
                {
                    id = -1,
                    range_start_date = dateStr,
                    range_end_date = nextDayStr,
                    hours = addHours,
                    minutes = addMins,
                    status = 5,
                    note = note ?? "",
                    edited = true,
                    start_date = null,
                    finish_date = null,
                    privacy = null,
                    tags = new List<string>()
                });
            }

            List<PlayDateEntry> editedDates = existingDates.FindAll(d => d.edited);

            int totalMinsAll = 0;
            foreach (var d in existingDates)
            {
                totalMinsAll += d.hours * 60 + d.minutes;
            }
            int ptHours = totalMinsAll / 60;
            int ptMins = totalMinsAll % 60;

            var formPairs = new List<KeyValuePair<string, string>>();
            formPairs.Add(new KeyValuePair<string, string>("game_id", gameId));

            // playthroughs[0]
            formPairs.Add(new KeyValuePair<string, string>("playthroughs[0][id]", playthroughId.ToString()));
            formPairs.Add(new KeyValuePair<string, string>("playthroughs[0][title]", playthroughTitle));
            formPairs.Add(new KeyValuePair<string, string>("playthroughs[0][platform]", platformId.ToString()));
            formPairs.Add(new KeyValuePair<string, string>("playthroughs[0][rating]", rating.ToString()));
            formPairs.Add(new KeyValuePair<string, string>("playthroughs[0][sync_sessions]", "true"));
            formPairs.Add(new KeyValuePair<string, string>("playthroughs[0][hours_played]", ptHours.ToString()));
            formPairs.Add(new KeyValuePair<string, string>("playthroughs[0][mins_played]", ptMins.ToString()));
            if (!isNewLog)
            {
                if (logStatus.Review != null)
                    formPairs.Add(new KeyValuePair<string, string>("playthroughs[0][review]", logStatus.Review));
                formPairs.Add(new KeyValuePair<string, string>("playthroughs[0][review_spoilers]", logStatus.ReviewSpoilers ? "true" : "false"));
                if (!string.IsNullOrEmpty(logStatus.StartDate))
                    formPairs.Add(new KeyValuePair<string, string>("playthroughs[0][start_date]", logStatus.StartDate));
                if (!string.IsNullOrEmpty(logStatus.FinishDate))
                    formPairs.Add(new KeyValuePair<string, string>("playthroughs[0][finish_date]", logStatus.FinishDate));
                formPairs.Add(new KeyValuePair<string, string>("playthroughs[0][is_master]", logStatus.IsMaster ? "true" : "false"));
                formPairs.Add(new KeyValuePair<string, string>("playthroughs[0][is_replay]", logStatus.IsReplay ? "true" : "false"));
            }

            // dates[playthroughId][i]
            string ptKey = playthroughId.ToString();
            for (int i = 0; i < editedDates.Count; i++)
            {
                var d = editedDates[i];
                string prefix = string.Format("dates[{0}][{1}]", ptKey, i);
                formPairs.Add(new KeyValuePair<string, string>(prefix + "[id]", d.id.ToString()));
                formPairs.Add(new KeyValuePair<string, string>(prefix + "[range_start_date]", d.range_start_date ?? ""));
                formPairs.Add(new KeyValuePair<string, string>(prefix + "[range_end_date]", d.range_end_date ?? ""));
                formPairs.Add(new KeyValuePair<string, string>(prefix + "[hours]", d.hours.ToString()));
                formPairs.Add(new KeyValuePair<string, string>(prefix + "[minutes]", d.minutes.ToString()));
                formPairs.Add(new KeyValuePair<string, string>(prefix + "[status]", d.status != null ? Convert.ToString(d.status) : ""));
                formPairs.Add(new KeyValuePair<string, string>(prefix + "[note]", d.note ?? ""));
                formPairs.Add(new KeyValuePair<string, string>(prefix + "[edited]", d.edited ? "true" : "false"));
                formPairs.Add(new KeyValuePair<string, string>(prefix + "[start_date]", d.start_date ?? ""));
                formPairs.Add(new KeyValuePair<string, string>(prefix + "[finish_date]", d.finish_date ?? ""));
                formPairs.Add(new KeyValuePair<string, string>(prefix + "[privacy]", d.privacy != null ? Convert.ToString(d.privacy) : ""));
                if (d.tags != null)
                {
                    foreach (var tag in d.tags)
                    {
                        formPairs.Add(new KeyValuePair<string, string>(prefix + "[tags][]", tag));
                    }
                }
            }

            // log
            formPairs.Add(new KeyValuePair<string, string>("log[id]", (!isNewLog && logStatus.GameLogId.HasValue) ? logStatus.GameLogId.Value.ToString() : ""));
            formPairs.Add(new KeyValuePair<string, string>("log[status]", !string.IsNullOrEmpty(logStatus.Status) ? logStatus.Status : "playing"));
            formPairs.Add(new KeyValuePair<string, string>("log[is_play]", logStatus.IsPlay ? "true" : "false"));
            formPairs.Add(new KeyValuePair<string, string>("log[is_playing]", (isNewLog || (!logStatus.IsPlay && !logStatus.IsPlaying)) ? "true" : (logStatus.IsPlaying ? "true" : "false")));
            formPairs.Add(new KeyValuePair<string, string>("log[is_backlog]", logStatus.IsBacklog ? "true" : "false"));
            formPairs.Add(new KeyValuePair<string, string>("log[is_wishlist]", logStatus.IsWishlist ? "true" : "false"));
            formPairs.Add(new KeyValuePair<string, string>("log[game_liked]", logStatus.GameLiked ? "true" : "false"));
            formPairs.Add(new KeyValuePair<string, string>("modal_type", "full"));

            var sb = new StringBuilder();
            for (int i = 0; i < formPairs.Count; i++)
            {
                if (i > 0) sb.Append("&");
                sb.Append(Uri.EscapeDataString(formPairs[i].Key));
                sb.Append("=");
                sb.Append(Uri.EscapeDataString(formPairs[i].Value ?? ""));
            }
            string formBody = sb.ToString();

            string saveUrl = !string.IsNullOrEmpty(settings.UserId)
                ? string.Format("{0}/api/user/{1}/log/{2}", BaseUrl, settings.UserId, gameId)
                : string.Format("{0}/native/log/{1}", BaseUrl, gameId);

            string cleanCookie = NormalizeCookie(settings.SessionCookie);
            var headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/x-www-form-urlencoded; charset=UTF-8" },
                { "Accept", "application/json, text/javascript, */*; q=0.01" },
                { "X-Requested-With", "XMLHttpRequest" }
            };
            if (!string.IsNullOrEmpty(settings.CsrfToken))
            {
                headers["X-CSRF-Token"] = settings.CsrfToken;
            }

            var res = SendRequest(saveUrl, "POST", cleanCookie, headers, formBody);
            if (res.StatusCode == 422 || res.StatusCode == 401 || res.StatusCode == 403)
            {
                string freshCsrf = FetchFreshCsrfToken(cleanCookie);
                if (!string.IsNullOrEmpty(freshCsrf))
                {
                    settings.CsrfToken = freshCsrf;
                    DataStore.SaveSettings(settings);
                    headers["X-CSRF-Token"] = freshCsrf;
                    res = SendRequest(saveUrl, "POST", cleanCookie, headers, formBody);
                }
            }

            if (!res.Ok)
            {
                result.Success = false;
                result.Error = string.Format("Backloggd save failed (HTTP {0}): {1}", res.StatusCode, res.Error ?? (res.Body != null && res.Body.Length > 120 ? res.Body.Substring(0, 120) : res.Body));
                return result;
            }

            result.Success = true;
            result.FinalDayHours = finalDayHours;
            result.FinalDayMinutes = finalDayMins;
            result.Accumulated = accumulated;
            return result;
        }
    }
}
