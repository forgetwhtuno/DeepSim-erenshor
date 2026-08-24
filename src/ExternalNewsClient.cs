using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ErenshorDeepSims
{
    // Real-world current-events lookup, deliberately separate from WikiClient (Erenshor lore) and
    // OfficialNewsClient (Erenshor patch/update Steam feed). External news has different recency,
    // trust, and memory rules: it must never be treated as Erenshor game fact, and it must never
    // become permanent Sim memory (see AGENTS.md source-of-truth hierarchy). CODE retrieves this
    // data; the LLM only discusses what is supplied here. This client never lets the model choose
    // an arbitrary URL and never scrapes free-form HTML.
    //
    // Two free, keyless, documented providers are chained with a bounded overall time budget:
    // Google News RSS (primary - simple XML, only needs headline/publisher/date/url) and GDELT
    // Doc 2.0 (fallback - JSON search API over global news coverage). Neither requires an API key;
    // ExternalNewsApiKeyConfig is reserved for a future keyed provider but unused by either of these.
    internal interface INewsTransport
    {
        string Get(string url, int timeoutMs, string accept, string userAgent);
    }

    // Production transport: a plain HttpWebRequest raced against a hard wall-clock deadline (see
    // NetworkTimeoutHelper - ReadWriteTimeout alone does not bound a slow-drip response).
    internal sealed class HttpWebRequestTransport : INewsTransport
    {
        public string Get(string url, int timeoutMs, string accept, string userAgent)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            request.UserAgent = userAgent;
            request.Accept = string.IsNullOrWhiteSpace(accept) ? "application/json" : accept;
            return NetworkTimeoutHelper.RunWithHardTimeout(request, timeoutMs);
        }
    }

    internal enum NewsProviderKind
    {
        GoogleRss,
        Gdelt
    }

    // One provider attempt, kept for /dsxnews diagnostics and framework debug logging. Never carries
    // an API key or raw response body - just enough to explain what happened.
    internal sealed class NewsProviderAttempt
    {
        public NewsProviderKind Provider;
        public bool Succeeded;
        public string FailureCategory; // "timeout", "network", "parse-or-provider-error", or null on success
        public int ResultCount;
        public long ElapsedMs;
        public bool Skipped; // true if the attempt was never made (budget exhausted)
    }

    internal sealed class ExternalNewsClient
    {
        private const string GdeltEndpoint = "https://api.gdeltproject.org/api/v2/doc/doc";
        private const string RssEndpoint = "https://news.google.com/rss/search";
        private const string UserAgent = "ErenshorDeepSims/0.8.2 (+local Lunaris mod)";

        // Repeated identical failures (provider outage) should not each pay the full lookup budget.
        private const int NegativeCacheSeconds = 45;
        private const int MinProviderBudgetMs = 1200;

        private readonly IDeepSimsLog _log;
        private readonly INewsTransport _transport;
        private readonly object _cacheLock = new object();
        private readonly Dictionary<string, CachedBundle> _cache = new Dictionary<string, CachedBundle>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _negativeCache = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        internal ExternalNewsClient(IDeepSimsLog log) : this(log, new HttpWebRequestTransport()) { }

        // Test seam: production always uses HttpWebRequestTransport; deterministic tests inject a
        // fake INewsTransport so provider-fallback behavior can be verified without real internet
        // access (see DeterministicRegressionTests.RunExternalNewsProviderTests).
        internal ExternalNewsClient(IDeepSimsLog log, INewsTransport transport)
        {
            _log = log;
            _transport = transport ?? new HttpWebRequestTransport();
        }

        internal Task<ExternalNewsBundle> SearchAsync(string apiUrl, string apiKey, string query, int maxResults, int timeoutSeconds, int maxChars, int cacheTtlMinutes)
        {
            return Task.Run(delegate { return Search(apiUrl, query, maxResults, timeoutSeconds, maxChars, cacheTtlMinutes); });
        }

        private ExternalNewsBundle Search(string apiUrl, string query, int maxResults, int timeoutSeconds, int maxChars, int cacheTtlMinutes)
        {
            string cleanQuery = string.IsNullOrWhiteSpace(query) ? "top world news" : query.Trim();
            if (cleanQuery.Length > 120) cleanQuery = cleanQuery.Substring(0, 120);
            int count = Math.Max(1, Math.Min(5, maxResults));
            string cacheKey = cleanQuery.ToLowerInvariant();

            lock (_cacheLock)
            {
                CachedBundle cached;
                if (_cache.TryGetValue(cacheKey, out cached) && (DateTime.UtcNow - cached.Utc).TotalMinutes < Math.Max(1, cacheTtlMinutes))
                    return Clone(cached.Bundle);

                DateTime negativeUntil;
                if (_negativeCache.TryGetValue(cacheKey, out negativeUntil) && DateTime.UtcNow < negativeUntil)
                    return NotFoundBundle(cleanQuery, "negative-cache-hit (retry in " + Math.Max(1, (int)(negativeUntil - DateTime.UtcNow).TotalSeconds) + "s)");
            }

            // apiKey is intentionally unused by either keyless provider; reserved for a future
            // keyed provider. Never logged, never placed in a URL query string here.
            string gdeltEndpoint = string.IsNullOrWhiteSpace(apiUrl) ? GdeltEndpoint : apiUrl.Trim();

            // Whole-lookup wall-clock budget: the configured seconds are a ceiling for the ENTIRE
            // provider chain, not per provider - a hung primary must not let the fallback also
            // consume a full independent timeout (see AGENTS.md P2.13a and README External News).
            int totalBudgetMs = Math.Max(2, timeoutSeconds) * 1000;
            Stopwatch overall = Stopwatch.StartNew();

            List<NewsProviderAttempt> attempts = new List<NewsProviderAttempt>();
            List<ExternalNewsItem> items = RunProvider(NewsProviderKind.GoogleRss, cleanQuery, count, gdeltEndpoint,
                BudgetFor(totalBudgetMs, overall.ElapsedMilliseconds, 0.6), attempts);

            if (items.Count == 0)
            {
                long remaining = totalBudgetMs - overall.ElapsedMilliseconds;
                if (remaining >= MinProviderBudgetMs)
                {
                    items = RunProvider(NewsProviderKind.Gdelt, cleanQuery, count, gdeltEndpoint, (int)remaining, attempts);
                }
                else
                {
                    attempts.Add(new NewsProviderAttempt { Provider = NewsProviderKind.Gdelt, Skipped = true });
                }
            }

            int rawResultCount = items.Count;
            items = ExternalNewsRelevance.Filter(cleanQuery, items);
            string diagnostics = DescribeAttempts(attempts);
            if (_log != null) _log.LogDebug("[DeepSims][KnowledgeRoute] source=group need=ExternalNews queryHash=" +
                SeedHash.Stable(cleanQuery).ToString("x8") + " provider=" + SuccessfulProvider(attempts) +
                " rawResults=" + rawResultCount + " relevantResults=" + items.Count +
                " status=" + (items.Count > 0 ? "success" : "no_relevant_results"));

            ExternalNewsBundle bundle = BuildBundle(cleanQuery, items, maxChars, diagnostics);
            bundle.RawResultCount = rawResultCount;
            bundle.RelevantResultCount = items.Count;
            bundle.Provider = SuccessfulProvider(attempts);
            lock (_cacheLock)
            {
                if (bundle.Combined.Found)
                {
                    _negativeCache.Remove(cacheKey);
                    _cache[cacheKey] = new CachedBundle { Utc = DateTime.UtcNow, Bundle = Clone(bundle) };
                    if (_cache.Count > 60) _cache.Clear();
                }
                else
                {
                    _negativeCache[cacheKey] = DateTime.UtcNow.AddSeconds(NegativeCacheSeconds);
                    if (_negativeCache.Count > 60) _negativeCache.Clear();
                }
            }
            return bundle;
        }

        private static int BudgetFor(int totalBudgetMs, long elapsedMs, double share)
        {
            long remaining = totalBudgetMs - elapsedMs;
            int budget = (int)Math.Max(MinProviderBudgetMs, remaining * share);
            return (int)Math.Min(budget, Math.Max(MinProviderBudgetMs, remaining));
        }

        private List<ExternalNewsItem> RunProvider(NewsProviderKind provider, string cleanQuery, int count, string gdeltEndpoint, int budgetMs, List<NewsProviderAttempt> attempts)
        {
            NewsProviderAttempt attempt = new NewsProviderAttempt { Provider = provider };
            Stopwatch clock = Stopwatch.StartNew();
            List<ExternalNewsItem> items = new List<ExternalNewsItem>();
            try
            {
                string raw = provider == NewsProviderKind.GoogleRss
                    ? Get(BuildRssUrl(cleanQuery), budgetMs, "application/rss+xml, application/xml, text/xml")
                    : Get(BuildGdeltUrl(gdeltEndpoint, cleanQuery, count), budgetMs, "application/json");
                items = provider == NewsProviderKind.GoogleRss ? ParseRss(raw, count) : ParseArticles(raw, count);
                attempt.Succeeded = items.Count > 0;
                attempt.ResultCount = items.Count;
            }
            catch (Exception ex)
            {
                attempt.Succeeded = false;
                attempt.FailureCategory = FailureCategory(ex);
            }
            attempt.ElapsedMs = clock.ElapsedMilliseconds;
            attempts.Add(attempt);
            return items;
        }

        private static string BuildGdeltUrl(string endpoint, string cleanQuery, int count)
        {
            // sort=datedesc (documented GDELT Doc 2.0 option) favors the most recent coverage,
            // matching what a "current events" lookup actually wants.
            return endpoint + "?query=" + Uri.EscapeDataString(cleanQuery) +
                "&mode=artlist&maxrecords=" + count + "&format=json&sort=datedesc&timespan=3days";
        }

        private static string BuildRssUrl(string cleanQuery)
        {
            return RssEndpoint + "?q=" + Uri.EscapeDataString(cleanQuery) + "&hl=en-US&gl=US&ceid=US:en";
        }

        private static string DescribeAttempts(List<NewsProviderAttempt> attempts)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < attempts.Count; i++)
            {
                NewsProviderAttempt a = attempts[i];
                if (i > 0) sb.Append(" ");
                string name = ProviderName(a.Provider);
                if (a.Skipped) { sb.Append(name).Append(" skipped (budget exhausted)."); continue; }
                if (a.Succeeded) sb.Append(name).Append(" returned ").Append(a.ResultCount).Append(" result(s) in ").Append(a.ElapsedMs).Append("ms.");
                else sb.Append(name).Append(" ").Append(FailureVerb(a.FailureCategory)).Append(" after ").Append(a.ElapsedMs).Append("ms.");
            }
            return sb.Length == 0 ? "no provider attempted." : sb.ToString();
        }

        private static string ProviderName(NewsProviderKind provider)
        {
            return provider == NewsProviderKind.GoogleRss ? "Google News RSS" : "GDELT";
        }

        private static string SuccessfulProvider(List<NewsProviderAttempt> attempts)
        {
            for (int i = 0; attempts != null && i < attempts.Count; i++)
                if (attempts[i] != null && attempts[i].Succeeded)
                    return attempts[i].Provider == NewsProviderKind.GoogleRss ? "google_rss" : "gdelt";
            return "none";
        }

        private static string FailureVerb(string category)
        {
            switch (category)
            {
                case "timeout": return "timed out";
                case "network": return "failed (network error)";
                case "empty": return "returned zero results";
                default: return "failed (parse/provider error)";
            }
        }

        private ExternalNewsBundle BuildBundle(string cleanQuery, List<ExternalNewsItem> items, int maxChars, string diagnostics)
        {
            ExternalNewsBundle bundle = new ExternalNewsBundle();
            bundle.Query = cleanQuery;
            bundle.Items = items;
            bundle.Diagnostics = diagnostics;
            WikiResult combined = new WikiResult();
            combined.Query = cleanQuery;
            combined.SourceLabel = "external real-world news search";
            bundle.Combined = combined;

            if (items.Count == 0)
            {
                combined.Title = "external news";
                combined.Extract = string.Empty;
                combined.Found = false;
                return bundle;
            }

            StringBuilder titles = new StringBuilder();
            StringBuilder extract = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                ExternalNewsItem item = items[i];
                if (i > 0) { titles.Append("; "); extract.Append(" | "); }
                titles.Append(item.Headline);
                extract.Append("[").Append(item.Publisher).Append(" - ").Append(FormatAge(item.PublishedUtc)).Append("] ").Append(item.Headline);
            }
            if (HasConflictingPublishers(items)) extract.Insert(0, "SOURCE DISAGREEMENT POSSIBLE (multiple independent publishers, treat as separate unconfirmed reports unless they agree): ");

            combined.Title = titles.ToString();
            combined.Extract = Trim(extract.ToString(), Math.Max(300, maxChars));
            combined.Url = items[0].Url;
            combined.Found = !string.IsNullOrWhiteSpace(combined.Extract);
            return bundle;
        }

        private static ExternalNewsBundle NotFoundBundle(string cleanQuery, string diagnostics)
        {
            ExternalNewsBundle bundle = new ExternalNewsBundle();
            bundle.Query = cleanQuery;
            bundle.Items = new List<ExternalNewsItem>();
            bundle.Diagnostics = diagnostics;
            WikiResult combined = new WikiResult();
            combined.Query = cleanQuery;
            combined.SourceLabel = "external real-world news search";
            combined.Title = "external news";
            combined.Extract = string.Empty;
            combined.Found = false;
            bundle.Combined = combined;
            return bundle;
        }

        private static ExternalNewsBundle Clone(ExternalNewsBundle src)
        {
            if (src == null) return null;
            ExternalNewsBundle copy = new ExternalNewsBundle();
            copy.Query = src.Query;
            copy.Diagnostics = src.Diagnostics;
            copy.RawResultCount = src.RawResultCount;
            copy.RelevantResultCount = src.RelevantResultCount;
            copy.Provider = src.Provider;
            copy.Combined = src.Combined == null ? null : new WikiResult
            {
                Query = src.Combined.Query,
                Title = src.Combined.Title,
                Extract = src.Combined.Extract,
                Url = src.Combined.Url,
                SourceLabel = src.Combined.SourceLabel,
                Found = src.Combined.Found
            };
            copy.Items = new List<ExternalNewsItem>();
            if (src.Items != null) foreach (ExternalNewsItem item in src.Items) copy.Items.Add(item);
            return copy;
        }

        private static bool HasConflictingPublishers(List<ExternalNewsItem> items)
        {
            if (items.Count < 2) return false;
            HashSet<string> publishers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ExternalNewsItem item in items) if (!string.IsNullOrWhiteSpace(item.Publisher)) publishers.Add(item.Publisher);
            return publishers.Count > 1;
        }

        private static string FormatAge(DateTime? publishedUtc)
        {
            if (publishedUtc == null) return "recent";
            TimeSpan age = DateTime.UtcNow - publishedUtc.Value;
            if (age.TotalMinutes < 0) return "just now";
            if (age.TotalHours < 1) return Math.Max(1, (int)age.TotalMinutes) + "m ago";
            if (age.TotalDays < 1) return (int)age.TotalHours + "h ago";
            return (int)age.TotalDays + "d ago";
        }

        private static string FailureCategory(Exception ex)
        {
            if (ex is TimeoutException || (ex != null && ex.Message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)) return "timeout";
            if (ex is WebException) return "network";
            return "parse-or-provider-error";
        }

        private string Get(string url, int timeoutMs, string accept)
        {
            timeoutMs = Math.Max(MinProviderBudgetMs, timeoutMs);
            try
            {
                return _transport.Get(url, timeoutMs, accept, UserAgent);
            }
            catch (WebException ex)
            {
                if (_log != null) _log.LogWarning("External news request failed: " + ex.Status);
                throw;
            }
            catch (TimeoutException ex)
            {
                if (_log != null) _log.LogWarning("External news request timed out: " + DiagnosticPrivacy.ExceptionType(ex));
                throw new InvalidOperationException("External news request timed out: " + ex.Message, ex);
            }
        }

        private static List<ExternalNewsItem> ParseArticles(string raw, int max)
        {
            List<ExternalNewsItem> list = new List<ExternalNewsItem>();
            if (string.IsNullOrWhiteSpace(raw)) return list;
            int arrayStart = raw.IndexOf("\"articles\"", StringComparison.Ordinal);
            if (arrayStart < 0) return list;
            int bracket = raw.IndexOf('[', arrayStart);
            if (bracket < 0) return list;

            DateTime retrievedUtc = DateTime.UtcNow;
            int pos = bracket + 1;
            int depth = 0;
            int objStart = -1;
            while (pos < raw.Length && list.Count < max)
            {
                char c = raw[pos];
                if (c == '{')
                {
                    if (depth == 0) objStart = pos;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && objStart >= 0)
                    {
                        string obj = raw.Substring(objStart, pos - objStart + 1);
                        ExternalNewsItem item = ParseOneArticle(obj, retrievedUtc);
                        if (item != null) list.Add(item);
                        objStart = -1;
                    }
                }
                else if (c == ']' && depth == 0) break;
                pos++;
            }
            return list;
        }

        private static List<ExternalNewsItem> ParseRss(string raw, int max)
        {
            List<ExternalNewsItem> list = new List<ExternalNewsItem>();
            if (string.IsNullOrWhiteSpace(raw)) return list;
            MatchCollection matches = Regex.Matches(raw, "<item>(.*?)</item>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            DateTime retrievedUtc = DateTime.UtcNow;
            for (int i = 0; i < matches.Count && list.Count < max; i++)
            {
                string item = matches[i].Groups[1].Value;
                string title = XmlText(item, "title");
                string link = XmlText(item, "link");
                string source = XmlText(item, "source");
                string published = XmlText(item, "pubDate");
                if (string.IsNullOrWhiteSpace(title)) continue;
                DateTime publishedUtc;
                DateTime? parsedUtc = DateTime.TryParse(published, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out publishedUtc)
                    ? (DateTime?)publishedUtc : null;
                list.Add(new ExternalNewsItem
                {
                    Headline = title,
                    Url = link,
                    Publisher = string.IsNullOrWhiteSpace(source) ? "news feed" : source,
                    PublishedUtc = parsedUtc,
                    RetrievedUtc = retrievedUtc
                });
            }
            return list;
        }

        private static string XmlText(string item, string element)
        {
            Match match = Regex.Match(item, "<" + element + "(?:\\s[^>]*)?>(.*?)</" + element + ">",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success) return string.Empty;
            return WebUtility.HtmlDecode(Regex.Replace(match.Groups[1].Value, "<!\\[CDATA\\[(.*?)\\]\\]>", "$1",
                RegexOptions.Singleline)).Trim();
        }

        private static ExternalNewsItem ParseOneArticle(string obj, DateTime retrievedUtc)
        {
            string title = ExtractString(obj, "title");
            if (string.IsNullOrWhiteSpace(title)) return null;
            ExternalNewsItem item = new ExternalNewsItem();
            item.Headline = Clean(title);
            item.Publisher = Clean(ExtractString(obj, "domain"));
            item.Url = ExtractString(obj, "url");
            item.RetrievedUtc = retrievedUtc;
            string seenDate = ExtractString(obj, "seendate");
            item.PublishedUtc = ParseGdeltDate(seenDate);
            item.Summary = item.Headline; // provider exposes headline only, no body snippet
            return item;
        }

        private static DateTime? ParseGdeltDate(string seenDate)
        {
            // GDELT format: yyyyMMdd'T'HHmmss'Z'
            if (string.IsNullOrWhiteSpace(seenDate)) return null;
            DateTime result;
            if (DateTime.TryParseExact(seenDate, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result))
                return result;
            return null;
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string s = Regex.Replace(value, @"<[^>]+>", " ");
            s = s.Replace("&quot;", "\"").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        private static string Trim(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string s = value.Trim();
            return s.Length <= max ? s : s.Substring(0, max).TrimEnd() + "...";
        }

        private static string ExtractString(string json, string property)
        {
            string needle = "\"" + property + "\"";
            int found = json.IndexOf(needle, StringComparison.Ordinal);
            if (found < 0) return string.Empty;
            int colon = json.IndexOf(':', found + needle.Length);
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
                            if (int.TryParse(json.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
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

        private sealed class CachedBundle
        {
            public DateTime Utc;
            public ExternalNewsBundle Bundle;
        }
    }

    // Normalized, trusted, bounded result of one external news search. Kept intentionally small:
    // the LLM prompt only ever sees a compact rendering of this, never raw article bodies.
    internal sealed class ExternalNewsItem
    {
        public string Headline;
        public string Publisher;
        public DateTime? PublishedUtc;
        public string Url;
        public string Summary;
        public DateTime RetrievedUtc;
    }

    internal sealed class ExternalNewsBundle
    {
        public string Query;
        public WikiResult Combined;
        public List<ExternalNewsItem> Items;
        public string Diagnostics; // provider attempts, for /dsxnews and debug logging only - never shown on ordinary /p replies
        public int RawResultCount;
        public int RelevantResultCount;
        public string Provider;
    }

    internal static class ExternalNewsRelevance
    {
        private static readonly HashSet<string> Stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "any", "about", "did", "do", "does", "heard", "latest", "news", "new",
            "recent", "today", "tonight", "current", "currently", "happen", "happened", "with", "the",
            "there", "what", "whats", "world", "update", "updates", "please", "anything"
        };

        internal static List<ExternalNewsItem> Filter(string query, IList<ExternalNewsItem> raw)
        {
            List<ExternalNewsItem> result = new List<ExternalNewsItem>();
            HashSet<string> terms = Terms(query);
            for (int i = 0; raw != null && i < raw.Count; i++)
            {
                ExternalNewsItem item = raw[i];
                if (item == null || string.IsNullOrWhiteSpace(item.Headline) || item.Headline.Trim().Length < 8 ||
                    string.IsNullOrWhiteSpace(item.Url)) continue;
                if (terms.Count == 0 || Matches(item, terms)) result.Add(item);
            }
            return result;
        }

        private static bool Matches(ExternalNewsItem item, HashSet<string> terms)
        {
            HashSet<string> haystack = Terms((item.Headline ?? string.Empty) + " " + (item.Publisher ?? string.Empty));
            foreach (string term in terms) if (haystack.Contains(term)) return true;
            return false;
        }

        private static HashSet<string> Terms(string text)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches((text ?? string.Empty).ToLowerInvariant(), @"[a-z0-9]{2,}"))
                if (!Stop.Contains(match.Value)) result.Add(match.Value);
            return result;
        }
    }

    // Deliberately keyword-only, no LLM, so a plain gameplay line about "news" from a class or NPC
    // topic never triggers a real internet request. This must only fire on clear player-initiated
    // current-events intent (see AGENTS.md / README External Recent News section).
    internal static class ExternalNewsQueryClassifier
    {
        private static readonly string[] StrongPhrases = new string[]
        {
            "what's going on with", "whats going on with",
            "anything new with", "any updates on", "did anything happen with",
            "any recent news", "recent news on", "recent news about",
            "latest news", "what's the latest news", "whats the latest news",
            "news today", "in the news",
            "what's happening with", "whats happening with",
            "what's happening in", "whats happening in"
        };

        private static readonly string[] TemporalWords = new string[]
        {
            "today", "yesterday", "recently", "lately", "this week", "this month", "this morning", "tonight"
        };

        private const string TemporalSuffix = @"(?:today|yesterday|recently|lately|this\s+week|this\s+month|this\s+morning|tonight)";

        // "recent"/"recently" near the word "news" (e.g. "any recent Star Citizen news") is a real
        // current-events signal even when a topic noun sits between the two words. A bare occurrence
        // of "news" alone (e.g. "good news everyone", "any news on Krakengard?") is not: ordinary
        // gameplay/lore chat must never trigger a third-party network request just because the word
        // "news" appears in it.
        private static readonly Regex RecentNewsPattern = new Regex(
            @"\brecent(?:ly)?\b[^.!?]{0,40}\bnews\b|\bnews\b[^.!?]{0,40}\brecent(?:ly)?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // A complete player question with no topic is still a clear request for current events.
        // Keep this anchored so ambient phrases such as "good news everyone" never trigger I/O.
        private static readonly Regex GenericNewsRequestPattern = new Regex(
            @"^\s*(?:(?:anyone|anybody)\s+)?(?:(?:hear|heard|got|have)\s+)?(?:any\s+)?news\s*[?!.]*\s*$|" +
            @"^\s*(?:anyone|anybody)\s+(?:hear|heard|got|have)\s+(?:any\s+)?news\s*[?!.]*\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Natural phrasings that carry an explicit temporal signal (e.g. "heard any news about NASA
        // lately?") but don't match the StrongPhrases substring list. Anchored full-message patterns
        // so ordinary lore/gameplay chat is not accidentally captured.
        private static readonly Regex[] NaturalCurrentEventPatterns = new Regex[]
        {
            new Regex(@"^\s*(?:anyone\s+|anybody\s+)?heard\s+(?:any\s+)?news\s+(?:about|on)\s+(?<topic>.+?)\s+" + TemporalSuffix + @"\s*[?!.]*\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"^\s*(?:anyone\s+|anybody\s+)?heard\s+anything\s+(?:about|on)\s+(?<topic>.+?)\s+" + TemporalSuffix + @"\s*[?!.]*\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"^\s*(?:heard\s+any\s+)(?<topic>.+?)\s+news\s+" + TemporalSuffix + @"\s*[?!.]*\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"^\s*(?:any\s+)?(?<topic>.+?)\s+news\s+" + TemporalSuffix + @"\s*[?!.]*\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"^\s*anything\s+happening\s+(?:with|about)\s+(?<topic>.+?)\s+" + TemporalSuffix + @"\s*[?!.]*\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"^\s*what\s+has\s+(?<topic>.+?)\s+been\s+up\s+to\s+" + TemporalSuffix + @"\s*[?!.]*\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"^\s*what's\s+(?:been\s+)?happening\s+(?:with|about)\s+(?<topic>.+?)\s+" + TemporalSuffix + @"\s*[?!.]*\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),

            // Untimed natural forms tested against live chat ("anyone hear any news on nasa?",
            // "heard anything about nasa?"). These have no temporal suffix, so the extracted topic
            // is screened against GameTopicKeywords below before the match is accepted - that keeps
            // ordinary Erenshor "news on <quest/npc/item>" phrasing routed to KnowledgeQueryClassifier
            // (wiki) instead of the real-world provider. A bare "any news on X" / "any news about X"
            // form (no "heard"/"hear" signal) is deliberately NOT matched here: that exact shape is
            // already claimed by KnowledgeQueryClassifier as a game-knowledge phrasing (see its
            // " any news on " / " any news about " strong keywords and the "any news on Krakengard?"
            // regression case below) and a topic-keyword denylist alone cannot safely tell a
            // real-world proper noun apart from an unrecognized Erenshor one.
            new Regex(@"^\s*(?:anyone\s+|anybody\s+)?(?:hear(?:d)?)\s+(?:any\s+)?news\s+(?:about|on)\s+(?<topic>.+?)\s*[?!.]*\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            new Regex(@"^\s*(?:anyone\s+|anybody\s+)?(?:hear(?:d)?)\s+anything\s+(?:about|on)\s+(?<topic>.+?)\s*[?!.]*\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        };

        // Ordinary Erenshor gameplay/lore nouns. When an untimed "news on/about X" style match's
        // extracted topic contains one of these, it is treated as game content, not a real-world
        // current-events question, and ShouldLookup/ExtractQuery defer to the wiki classifier instead.
        private static readonly string[] GameTopicKeywords = new string[]
        {
            "quest", "npc", "boss", "item", "drop", "loot", "vendor", "zone", "dungeon", "raid",
            "vessel", "forge", "class", "arcanist", "druid", "paladin", "reaver", "stormcaller",
            "windblade", "duelist", "erenshor", "camp", "guild", "patch", "update", "expansion"
        };

        private static bool IsGameTopic(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic)) return false;
            string t = topic.ToLowerInvariant();
            for (int i = 0; i < GameTopicKeywords.Length; i++)
                if (t.Contains(GameTopicKeywords[i])) return true;
            return false;
        }

        internal static bool ShouldLookup(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            string query;
            if (TryExtractNaturalCurrentEventsQuery(message, out query)) return true;
            if (GenericNewsRequestPattern.IsMatch(message)) return true;

            string m = message.ToLowerInvariant();
            for (int i = 0; i < StrongPhrases.Length; i++) if (m.Contains(StrongPhrases[i])) return true;

            if (RecentNewsPattern.IsMatch(m)) return true;

            if (m.Contains("what happened") || m.Contains("whats happened") || m.Contains("what's happened") ||
                m.Contains("what is happening") || m.Contains("what's happening") || m.Contains("whats happening"))
            {
                for (int i = 0; i < TemporalWords.Length; i++) if (m.Contains(TemporalWords[i])) return true;
            }

            return false;
        }

        internal static string ExtractQuery(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "top world news";
            string natural;
            if (TryExtractNaturalCurrentEventsQuery(message, out natural)) return natural;
            if (GenericNewsRequestPattern.IsMatch(message)) return "top world news";

            string m = message.Trim();
            string lower = m.ToLowerInvariant();

            string[] stripPrefixes = new string[]
            {
                "what's going on with", "whats going on with",
                "what's the latest news", "whats the latest news", "latest news",
                "anything new with", "any updates on", "did anything happen with",
                "any recent news on", "any recent news about", "any recent news",
                "recent news on", "recent news about", "recent news",
                "what's happening with", "whats happening with",
                "what's happening in", "whats happening in",
                "what happened with", "whats happened with", "what's happened with",
                "what happened in", "whats happened in", "what's happened in",
                "anything interesting happen in the news today",
                "anything interesting in the news today",
                "anything interesting happen in the news",
                "any news today", "any news about", "any news on", "any news",
                "in the news today", "in the news"
            };

            string remainder = null;
            for (int i = 0; i < stripPrefixes.Length; i++)
            {
                int idx = lower.IndexOf(stripPrefixes[i], StringComparison.Ordinal);
                if (idx < 0) continue;
                remainder = m.Substring(idx + stripPrefixes[i].Length);
                break;
            }
            if (remainder == null) remainder = m;

            remainder = Regex.Replace(remainder, @"[?!.]+$", string.Empty);
            remainder = Regex.Replace(remainder, @"\b(today|yesterday|recently|lately|this\s+week|this\s+month|this\s+morning|tonight)\b", string.Empty, RegexOptions.IgnoreCase);
            remainder = remainder.Trim(new char[] { ' ', '?', '!', '.', ',' });

            if (string.IsNullOrWhiteSpace(remainder) || remainder.Length < 2) return "top world news";
            if (remainder.Length > 80) remainder = remainder.Substring(0, 80);
            return remainder;
        }

        private static bool TryExtractNaturalCurrentEventsQuery(string message, out string query)
        {
            query = string.Empty;
            if (string.IsNullOrWhiteSpace(message)) return false;
            for (int i = 0; i < NaturalCurrentEventPatterns.Length; i++)
            {
                Match match = NaturalCurrentEventPatterns[i].Match(message);
                if (!match.Success) continue;
                string topic = CleanTopic(match.Groups["topic"].Value);
                if (topic.Length < 2) return false;
                if (IsGameTopic(topic)) return false;
                query = topic;
                return true;
            }
            return false;
        }

        private static string CleanTopic(string value)
        {
            string topic = (value ?? string.Empty).Trim();
            topic = Regex.Replace(topic, @"^(?:the\s+)?news\s+(?:about|on)\s+", string.Empty, RegexOptions.IgnoreCase);
            topic = Regex.Replace(topic, @"\b(?:today|yesterday|recently|lately|this\s+week|this\s+month|this\s+morning|tonight)\b\s*$", string.Empty, RegexOptions.IgnoreCase);
            topic = topic.Trim(new char[] { ' ', '?', '!', '.', ',', ':', ';' });
            return topic.Length > 80 ? topic.Substring(0, 80).TrimEnd() : topic;
        }
    }
}
