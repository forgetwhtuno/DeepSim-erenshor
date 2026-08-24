using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ErenshorDeepSims
{
    // Narrow current-information source for patch/update/expansion questions. This uses Valve's
    // public ISteamNews GetNewsForApp endpoint for Erenshor's app id; it is not a general web search.
    internal sealed class OfficialNewsClient
    {
        private readonly IDeepSimsLog _log;
        private readonly object _cacheLock = new object();
        private DateTime _cachedUtc = DateTime.MinValue;
        private string _cachedRaw = string.Empty;

        internal OfficialNewsClient(IDeepSimsLog log) { _log = log; }

        internal Task<WikiResult> SearchAsync(string endpoint, string query, int timeoutSeconds, int maxChars)
        {
            return Task.Run(delegate
            {
                string cleanQuery = string.IsNullOrWhiteSpace(query) ? "latest update" : query.Trim();
                string url = string.IsNullOrWhiteSpace(endpoint)
                    ? "https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid=2382520&count=12&maxlength=1200"
                    : endpoint.Trim();

                string raw;
                lock (_cacheLock)
                {
                    if ((DateTime.UtcNow - _cachedUtc).TotalMinutes < 10 && !string.IsNullOrWhiteSpace(_cachedRaw)) raw = _cachedRaw;
                    else raw = null;
                }
                if (raw == null)
                {
                    raw = Get(url, timeoutSeconds);
                    lock (_cacheLock) { _cachedRaw = raw; _cachedUtc = DateTime.UtcNow; }
                }

                List<NewsItem> items = ParseItems(raw, 12);
                WikiResult result = new WikiResult();
                result.Query = cleanQuery;
                result.SourceLabel = "official Erenshor Steam news";
                result.Url = "https://store.steampowered.com/news/app/2382520";
                if (items.Count == 0)
                {
                    result.Title = "Steam news";
                    result.Extract = string.Empty;
                    result.Found = false;
                    return result;
                }

                List<NewsItem> selected = SelectRelevant(items, cleanQuery, 3);
                StringBuilder title = new StringBuilder();
                StringBuilder extract = new StringBuilder();
                for (int i = 0; i < selected.Count; i++)
                {
                    NewsItem item = selected[i];
                    if (i > 0) { title.Append("; "); extract.Append(" | "); }
                    title.Append(item.Title);
                    extract.Append("[").Append(item.DateText).Append(" - ").Append(item.Title).Append("] ")
                        .Append(Trim(item.Contents, 520));
                }
                result.Title = title.ToString();
                result.Extract = Trim(extract.ToString(), Math.Max(350, maxChars));
                result.Found = !string.IsNullOrWhiteSpace(result.Extract);
                if (_log != null) _log.LogDebug("Official news lookup matched " + selected.Count + " item(s); " + DiagnosticPrivacy.DescribeChars("query", cleanQuery) + " extractChars=" + (result.Extract == null ? 0 : result.Extract.Length));
                return result;
            });
        }

        private string Get(string url, int timeoutSeconds)
        {
            int timeoutMs = Math.Max(2, timeoutSeconds) * 1000;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.UserAgent = "ErenshorDeepSims/0.8.2 (+local Lunaris mod)";
            request.Accept = "application/json";
            try
            {
                // See NetworkTimeoutHelper: ReadWriteTimeout alone does not bound a slow-drip
                // response, so race the whole request+read against a hard wall-clock deadline.
                return NetworkTimeoutHelper.RunWithHardTimeout(request, timeoutMs);
            }
            catch (WebException ex)
            {
                if (_log != null) _log.LogWarning("Official Erenshor Steam-news request failed: " + ex.Status);
                throw;
            }
            catch (TimeoutException ex)
            {
                if (_log != null) _log.LogWarning("Official Erenshor Steam-news request timed out: " + DiagnosticPrivacy.ExceptionType(ex));
                throw new InvalidOperationException("Official Erenshor Steam-news request timed out: " + ex.Message, ex);
            }
        }

        private static List<NewsItem> ParseItems(string raw, int max)
        {
            List<NewsItem> list = new List<NewsItem>();
            if (string.IsNullOrWhiteSpace(raw)) return list;
            int pos = raw.IndexOf("\"newsitems\"", StringComparison.Ordinal);
            if (pos < 0) pos = 0;
            while (list.Count < max)
            {
                int titleKey = FindProperty(raw, "title", pos);
                if (titleKey < 0) break;
                int contentsKey = FindProperty(raw, "contents", titleKey + 7);
                if (contentsKey < 0) break;
                int dateKey = FindProperty(raw, "date", contentsKey + 10);
                string title = ExtractString(raw, "title", titleKey);
                string contents = ExtractString(raw, "contents", contentsKey);
                long unix = ExtractLong(raw, "date", dateKey);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    NewsItem item = new NewsItem();
                    item.Title = Clean(title);
                    item.Contents = Clean(contents);
                    item.UnixDate = unix;
                    item.DateText = unix > 0 ? UnixDate(unix) : "recent";
                    list.Add(item);
                }
                pos = Math.Max(contentsKey + 10, dateKey + 6);
            }
            return list;
        }

        private static List<NewsItem> SelectRelevant(List<NewsItem> items, string query, int max)
        {
            string q = (query ?? string.Empty).ToLowerInvariant();
            string[] tokens = Regex.Split(q, @"[^a-z0-9]+");
            List<ScoredNews> scored = new List<ScoredNews>();
            for (int i = 0; i < items.Count; i++)
            {
                NewsItem item = items[i];
                string hay = ((item.Title ?? string.Empty) + " " + (item.Contents ?? string.Empty)).ToLowerInvariant();
                int score = Math.Max(0, 20 - i); // recency bias
                for (int t = 0; t < tokens.Length; t++)
                {
                    string token = tokens[t];
                    if (token.Length < 4 || token == "latest" || token == "recent" || token == "think") continue;
                    if ((item.Title ?? string.Empty).ToLowerInvariant().Contains(token)) score += 12;
                    else if (hay.Contains(token)) score += 5;
                }
                if (q.Contains("expansion") && (hay.Contains("expansion") || hay.Contains("planar march"))) score += 30;
                if ((q.Contains("patch") || q.Contains("update") || q.Contains("notes")) && (hay.Contains("patch") || hay.Contains("update") || hay.Contains("version"))) score += 12;
                scored.Add(new ScoredNews(item, score));
            }
            scored.Sort(delegate(ScoredNews a, ScoredNews b) { return b.Score.CompareTo(a.Score); });
            List<NewsItem> result = new List<NewsItem>();
            for (int i = 0; i < scored.Count && result.Count < max; i++) result.Add(scored[i].Item);
            return result;
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string s = Regex.Replace(value, @"\[/?[^\]]+\]", " ");
            s = Regex.Replace(s, @"<[^>]+>", " ");
            s = s.Replace("&quot;", "\"").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        private static int FindProperty(string json, string property, int start)
        {
            if (string.IsNullOrEmpty(json)) return -1;
            string needle = "\"" + property + "\"";
            int pos = Math.Max(0, start);
            while (pos < json.Length)
            {
                int found = json.IndexOf(needle, pos, StringComparison.Ordinal);
                if (found < 0) return -1;
                int after = found + needle.Length;
                while (after < json.Length && char.IsWhiteSpace(json[after])) after++;
                if (after < json.Length && json[after] == ':') return found;
                pos = found + needle.Length;
            }
            return -1;
        }

        private static string ExtractString(string json, string property, int start)
        {
            int key = FindProperty(json, property, start);
            if (key < 0) return string.Empty;
            int colon = json.IndexOf(':', key + property.Length + 2);
            if (colon < 0) return string.Empty;
            int pos = colon + 1;
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
            if (pos >= json.Length || json[pos] != '"') return string.Empty;
            pos++;
            StringBuilder sb = new StringBuilder();
            while (pos < json.Length)
            {
                char c = json[pos++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                if (pos >= json.Length) break;
                char esc = json[pos++];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (pos + 4 <= json.Length)
                        {
                            int code;
                            if (int.TryParse(json.Substring(pos, 4), System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out code))
                            {
                                sb.Append((char)code);
                                pos += 4;
                            }
                        }
                        break;
                    default: sb.Append(esc); break;
                }
            }
            return sb.ToString();
        }

        private static long ExtractLong(string json, string property, int start)
        {
            int key = FindProperty(json, property, start);
            if (key < 0) return 0;
            int colon = json.IndexOf(':', key + property.Length + 2);
            if (colon < 0) return 0;
            int pos = colon + 1;
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
            int end = pos;
            while (end < json.Length && char.IsDigit(json[end])) end++;
            long value;
            return long.TryParse(json.Substring(pos, Math.Max(0, end - pos)), out value) ? value : 0;
        }

        private static string UnixDate(long unix)
        {
            try { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unix).ToString("yyyy-MM-dd"); }
            catch { return "recent"; }
        }

        private static string Trim(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string s = value.Trim();
            return s.Length <= max ? s : s.Substring(0, max).TrimEnd() + "...";
        }

        private sealed class NewsItem
        {
            public string Title;
            public string Contents;
            public long UnixDate;
            public string DateText;
        }

        private sealed class ScoredNews
        {
            public NewsItem Item;
            public int Score;
            public ScoredNews(NewsItem item, int score) { Item = item; Score = score; }
        }
    }

    internal static class OfficialNewsQueryClassifier
    {
        internal static bool ShouldLookup(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            string m = message.ToLowerInvariant();
            string[] signals = new string[]
            {
                "latest expansion", "newest expansion", "recent expansion", "new expansion",
                "latest patch", "newest patch", "patch notes", "latest update", "newest update",
                "recent update", "recent patch", "what changed", "planar march"
            };
            for (int i = 0; i < signals.Length; i++) if (m.Contains(signals[i])) return true;
            // Bare "latest news" is real-world news. Official Erenshor news requires an
            // explicit Erenshor/game/Steam context so generic world-news questions route
            // to ExternalNewsClient instead.
            if (m.Contains("latest news") &&
                (m.Contains("erenshor") || m.Contains("steam") || m.Contains("game"))) return true;
            return false;
        }

        internal static string ExtractQuery(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "latest update";
            string m = message.Trim().ToLowerInvariant();
            if (m.Contains("expansion") || m.Contains("planar march")) return "latest expansion";
            if (m.Contains("patch") || m.Contains("notes")) return "latest patch notes";
            if (m.Contains("update") || m.Contains("changed")) return "latest update";
            return message.Trim();
        }
    }
}
