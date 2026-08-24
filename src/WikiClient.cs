using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ErenshorDeepSims
{
    internal class WikiClient
    {
        private readonly IDeepSimsLog _log;
        private readonly object _cacheLock = new object();
        private readonly Dictionary<string, CachedWikiResult> _cache = new Dictionary<string, CachedWikiResult>(StringComparer.OrdinalIgnoreCase);

        internal WikiClient(IDeepSimsLog log)
        {
            _log = log;
        }

        internal Task<WikiResult> SearchAsync(string apiUrl, string query, int timeoutSeconds, int maxChars)
        {
            return Task.Run(delegate
            {
                string cleanQuery = string.IsNullOrWhiteSpace(query) ? string.Empty : query.Trim();
                if (cleanQuery.Length == 0) return NotFound(cleanQuery);
                if (cleanQuery.Length > 180) cleanQuery = cleanQuery.Substring(0, 180);

                string cacheKey = cleanQuery.ToLowerInvariant();
                lock (_cacheLock)
                {
                    CachedWikiResult cached;
                    if (_cache.TryGetValue(cacheKey, out cached) && (DateTime.UtcNow - cached.Utc).TotalMinutes < 30)
                        return Clone(cached.Result);
                }

                string endpoint = string.IsNullOrWhiteSpace(apiUrl) ? "https://erenshor.wiki.gg/api.php" : apiUrl.Trim();

                // Prefer an exact/current entity page before broad full-text search. Redirect handling is
                // deliberately preserved: legacy names such as Duelist may still resolve to Windblade.
                List<string> exactCandidates = BuildExactTitleCandidates(cleanQuery);
                for (int e = 0; e < exactCandidates.Count; e++)
                {
                    try
                    {
                        PageExtract exact = FetchRenderedPage(endpoint, exactCandidates[e], timeoutSeconds);
                        if (exact != null && !string.IsNullOrWhiteSpace(exact.Extract))
                        {
                            string relevanceQuery = cleanQuery.StartsWith("Item Quality", StringComparison.OrdinalIgnoreCase)
                                ? "Merging Vessel +1 Standard quality forge"
                                : cleanQuery;
                            int exactScore = ScorePageRelevance(exact.Title, exact.Extract, relevanceQuery);
                            string relevant = exactScore >= 20
                                ? SelectRelevantWindow(exact.Extract, relevanceQuery, Math.Max(300, maxChars), exact.Title)
                                : string.Empty;
                            if (!string.IsNullOrWhiteSpace(relevant))
                            {
                                WikiResult exactResult = new WikiResult();
                                exactResult.Query = cleanQuery;
                                exactResult.Title = exact.Title;
                                exactResult.Extract = CleanWikiText("[" + exact.Title + "] " + relevant, Math.Max(300, maxChars));
                                exactResult.Url = "https://erenshor.wiki.gg/wiki/" + Uri.EscapeDataString(exact.Title.Replace(' ', '_'));
                                exactResult.SourceLabel = "Erenshor community wiki";
                                exactResult.Found = true;
                                Cache(cacheKey, exactResult);
                                if (_log != null) _log.LogDebug("Wiki exact-title lookup matched; titleChars=" + (exactResult.Title == null ? 0 : exactResult.Title.Length) + " extractChars=" + (exactResult.Extract == null ? 0 : exactResult.Extract.Length));
                                return Clone(exactResult);
                            }
                        }
                    }
                    catch (Exception exactEx)
                    {
                        if (_log != null) _log.LogDebug("Wiki exact-title candidate did not resolve: " + exactEx.GetType().Name);
                    }
                }

                int perPageChars = Math.Min(700, Math.Max(260, maxChars / 2));
                string url = endpoint + (endpoint.Contains("?") ? "&" : "?") +
                    "action=query&generator=search&gsrnamespace=0&gsrlimit=3&gsrsearch=" + Uri.EscapeDataString(cleanQuery) +
                    "&prop=extracts&exintro=1&explaintext=1&exchars=" + perPageChars +
                    "&redirects=1&format=json&formatversion=2";

                string raw = Get(url, timeoutSeconds);
                List<PageExtract> pages = ExtractPages(raw, 3);
                if (pages.Count == 0)
                {
                    WikiResult miss = NotFound(cleanQuery);
                    Cache(cacheKey, miss);
                    if (_log != null) _log.LogDebug("Wiki lookup returned no pages; queryChars=" + cleanQuery.Length);
                    return miss;
                }

                int pageBudget = Math.Max(320, maxChars / Math.Max(1, pages.Count));
                List<RankedPageExtract> ranked = new List<RankedPageExtract>();
                for (int i = 0; i < pages.Count; i++)
                {
                    PageExtract page = pages[i];
                    string pageText = page.Extract;
                    if (string.IsNullOrWhiteSpace(pageText) || pageText.Length < 80)
                    {
                        try
                        {
                            pageText = FetchRenderedPageText(endpoint, page.Title, timeoutSeconds);
                            if (_log != null && !string.IsNullOrWhiteSpace(pageText))
                                _log.LogDebug("Wiki parse fallback '" + page.Title + "' supplied " + pageText.Length + " chars.");
                        }
                        catch (Exception parseEx)
                        {
                            if (_log != null) _log.LogWarning("Wiki parse fallback failed for '" + page.Title + "': " + DiagnosticPrivacy.ExceptionType(parseEx));
                        }
                    }

                    pageText = RemovePlaintextBoilerplate(pageText);
                    int score = ScorePageRelevance(page.Title, pageText, cleanQuery);
                    string relevant = score >= 16 ? SelectRelevantWindow(pageText, cleanQuery, pageBudget, page.Title) : string.Empty;
                    if (string.IsNullOrWhiteSpace(relevant)) continue;
                    ranked.Add(new RankedPageExtract { Title = page.Title, Extract = relevant, Score = score });
                }

                ranked.Sort(delegate(RankedPageExtract a, RankedPageExtract b)
                {
                    int byScore = b.Score.CompareTo(a.Score);
                    return byScore != 0 ? byScore : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
                });

                if (ranked.Count == 0)
                {
                    WikiResult miss = NotFound(cleanQuery);
                    Cache(cacheKey, miss);
                    if (_log != null) _log.LogDebug("Wiki lookup results lacked useful relevance; queryChars=" + cleanQuery.Length);
                    return miss;
                }

                StringBuilder titles = new StringBuilder();
                StringBuilder combined = new StringBuilder();
                string firstUrl = string.Empty;
                for (int i = 0; i < ranked.Count; i++)
                {
                    RankedPageExtract page = ranked[i];
                    if (i > 0) titles.Append("; ");
                    titles.Append(page.Title);
                    if (string.IsNullOrEmpty(firstUrl))
                        firstUrl = "https://erenshor.wiki.gg/wiki/" + Uri.EscapeDataString(page.Title.Replace(' ', '_'));
                    if (combined.Length > 0) combined.Append(" | ");
                    combined.Append("[").Append(page.Title).Append("] ").Append(page.Extract);
                }

                string groundedText = CleanWikiText(combined.ToString(), Math.Max(300, maxChars));
                WikiResult result = new WikiResult();
                result.Query = cleanQuery;
                result.Title = titles.ToString();
                result.Extract = groundedText;
                result.Url = firstUrl;
                result.SourceLabel = "Erenshor community wiki";
                result.Found = !string.IsNullOrWhiteSpace(groundedText);
                Cache(cacheKey, result);
                if (_log != null) _log.LogDebug("Wiki lookup matched " + ranked.Count + " page(s); queryChars=" + cleanQuery.Length + " extractChars=" + (result.Extract == null ? 0 : result.Extract.Length));
                return Clone(result);
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
                return NetworkTimeoutHelper.RunWithHardTimeout(request, timeoutMs);
            }
            catch (WebException ex)
            {
                string detail = ex.Message;
                if (ex.Response != null)
                {
                    try
                    {
                        using (StreamReader reader = new StreamReader(ex.Response.GetResponseStream()))
                        {
                            string body = reader.ReadToEnd();
                            if (!string.IsNullOrWhiteSpace(body)) detail = body;
                        }
                    }
                    catch { }
                }
                if (_log != null) _log.LogWarning("Erenshor wiki request failed: " + ex.Status);
                throw new InvalidOperationException("Erenshor wiki lookup failed: " + detail, ex);
            }
            catch (TimeoutException ex)
            {
                if (_log != null) _log.LogWarning("Erenshor wiki request timed out: " + DiagnosticPrivacy.ExceptionType(ex));
                throw new InvalidOperationException("Erenshor wiki lookup timed out: " + ex.Message, ex);
            }
        }

        private static List<PageExtract> ExtractPages(string raw, int maxPages)
        {
            List<PageExtract> result = new List<PageExtract>();
            if (string.IsNullOrEmpty(raw)) return result;
            int pagesStart = raw.IndexOf("\"pages\"", StringComparison.Ordinal);
            if (pagesStart < 0) return result;
            int pos = pagesStart;
            while (result.Count < Math.Max(1, maxPages))
            {
                int titleKey = FindJsonProperty(raw, "title", pos);
                if (titleKey < 0) break;
                string title = ExtractJsonString(raw, "title", titleKey);
                if (string.IsNullOrWhiteSpace(title)) break;

                int nextTitle = FindJsonProperty(raw, "title", titleKey + 7);
                int extractKey = FindJsonProperty(raw, "extract", titleKey);
                string extract = string.Empty;
                if (extractKey >= 0 && (nextTitle < 0 || extractKey < nextTitle))
                    extract = ExtractJsonString(raw, "extract", extractKey) ?? string.Empty;

                PageExtract page = new PageExtract();
                page.Title = title;
                page.Extract = RemovePlaintextBoilerplate(CleanWikiText(extract, 900));
                result.Add(page);
                pos = nextTitle >= 0 ? nextTitle : raw.Length;
                if (nextTitle < 0) break;
            }
            return result;
        }

        private PageExtract FetchRenderedPage(string endpoint, string title, int timeoutSeconds)
        {
            string parseUrl = endpoint + (endpoint.Contains("?") ? "&" : "?") +
                "action=parse&page=" + Uri.EscapeDataString(title) +
                "&prop=text&disableeditsection=1&disablelimitreport=1&redirects=1&format=json&formatversion=2";
            string raw = Get(parseUrl, timeoutSeconds);
            string canonicalTitle = ExtractJsonString(raw, "title", 0);
            string html = ExtractJsonString(raw, "text", 0);
            if (!string.IsNullOrWhiteSpace(html))
            {
                PageExtract parsed = new PageExtract();
                parsed.Title = string.IsNullOrWhiteSpace(canonicalTitle) ? title : canonicalTitle;
                parsed.Extract = StripHtml(html);
                return parsed;
            }

            string wikiUrl = endpoint + (endpoint.Contains("?") ? "&" : "?") +
                "action=parse&page=" + Uri.EscapeDataString(title) +
                "&prop=wikitext&redirects=1&format=json&formatversion=2";
            raw = Get(wikiUrl, timeoutSeconds);
            canonicalTitle = ExtractJsonString(raw, "title", 0);
            string wikitext = ExtractJsonString(raw, "wikitext", 0);
            string cleaned = CleanWikitext(wikitext);
            if (string.IsNullOrWhiteSpace(cleaned)) return null;
            PageExtract fallback = new PageExtract();
            fallback.Title = string.IsNullOrWhiteSpace(canonicalTitle) ? title : canonicalTitle;
            fallback.Extract = cleaned;
            return fallback;
        }

        private string FetchRenderedPageText(string endpoint, string title, int timeoutSeconds)
        {
            PageExtract page = FetchRenderedPage(endpoint, title, timeoutSeconds);
            return page == null ? string.Empty : page.Extract;
        }

        private static List<string> BuildExactTitleCandidates(string query)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(query)) return result;
            string q = query.Trim(' ', '\t', '\r', '\n', '?', '!', '.', ',', ':', ';', '"');
            AddCandidate(result, q);

            if (q.StartsWith("Item Quality ", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(q, "Item Quality", StringComparison.OrdinalIgnoreCase))
                AddCandidate(result, "Item Quality");

            if (string.Equals(q, "duelist", StringComparison.OrdinalIgnoreCase)) AddCandidate(result, "Windblade");

            string[] suffixes = new string[]
            {
                " quests", " quest", " items", " item", " bosses", " boss", " npcs", " npc",
                " drops", " drop", " location", " locations", " class", " classes", " guide"
            };
            string lower = q.ToLowerInvariant();
            for (int i = 0; i < suffixes.Length; i++)
            {
                if (!lower.EndsWith(suffixes[i], StringComparison.Ordinal)) continue;
                string trimmed = q.Substring(0, q.Length - suffixes[i].Length).Trim();
                AddCandidate(result, trimmed);
            }
            return result;
        }

        private static void AddCandidate(List<string> list, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase)) return;
            list.Add(value.Trim());
        }

        private static string StripHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            string text = html;
            text = Regex.Replace(text, @"<!--.*?-->", " ", RegexOptions.Singleline);
            text = Regex.Replace(text, @"<script\b[^>]*>.*?</script>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"<style\b[^>]*>.*?</style>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, @"<(nav|footer|aside)\b[^>]*>.*?</\1\s*>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            // wiki.gg/MediaWiki navigation boxes, category blocks, TOCs, sidebars and site footer
            // containers are presentation chrome, not evidence about relationships between entities.
            string chromePattern = @"<(?<tag>div|table|ul|ol|section)\b[^>]*(?:class|id)\s*=\s*['""][^'""]*(?:navbox|navigation|site-navigation|sidebar|toc|catlinks|categorylinks|mw-footer|site-footer|footer-links|mw-portlet)[^'""]*['""][^>]*>.*?</\k<tag>\s*>";
            for (int i = 0; i < 3; i++)
                text = Regex.Replace(text, chromePattern, " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            text = Regex.Replace(text, @"<br\s*/?>", " ; ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</(p|li|tr|h1|h2|h3|h4|h5|h6|div|section)\s*>", " ; ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</(td|th)\s*>", " | ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<[^>]+>", " ");
            text = WebUtility.HtmlDecode(text);
            text = text.Replace("[edit]", " ");
            return RemovePlaintextBoilerplate(CollapseWhitespace(text));
        }

        private static string CleanWikitext(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string text = value;
            text = Regex.Replace(text, @"<!--.*?-->", " ", RegexOptions.Singleline);
            text = Regex.Replace(text, @"\[\[Category:[^\]]+\]\]", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\{\{(?:Navbox|Navigation|Footer|ZoneNav)[\s\S]*?\}\}", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\[\[(?:[^\]|]+\|)?([^\]]+)\]\]", "$1");
            text = Regex.Replace(text, @"\[(?:https?://\S+)\s+([^\]]+)\]", "$1");
            text = Regex.Replace(text, @"'{2,5}", string.Empty);
            text = Regex.Replace(text, @"[{}|=]+", " ");
            return RemovePlaintextBoilerplate(CollapseWhitespace(text));
        }

        private static string RemovePlaintextBoilerplate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string[] parts = Regex.Split(CollapseWhitespace(value), @"\s*;\s*");
            StringBuilder kept = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0 || IsBoilerplateSegment(part)) continue;
                if (kept.Length > 0) kept.Append(" ; ");
                kept.Append(part);
            }
            return CollapseWhitespace(kept.ToString());
        }

        private static bool IsBoilerplateSegment(string segment)
        {
            string lower = segment.Trim().ToLowerInvariant();
            if (lower == "navigation" || lower == "site navigation" || lower == "categories" || lower == "category") return true;
            if (lower.StartsWith("category:", StringComparison.Ordinal) || lower.StartsWith("categories:", StringComparison.Ordinal)) return true;
            if ((lower.StartsWith("navigation:", StringComparison.Ordinal) || lower.StartsWith("zones:", StringComparison.Ordinal) ||
                 lower.StartsWith("zone navigation:", StringComparison.Ordinal)) && segment.Length > 70)
                return true;
            return false;
        }

        private static string SelectRelevantWindow(string text, string query, int maxChars, string title)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            string clean = RemovePlaintextBoilerplate(text);
            if (clean.Length == 0) return string.Empty;
            int budget = Math.Max(240, maxChars);
            List<string> tokens = BuildQueryTokens(query);

            int hit = FindBestTokenHit(clean, tokens);
            bool titleRelevant = ScoreTitleRelevance(title, query, tokens) >= 20;
            if (tokens.Count > 0 && hit < 0 && !titleRelevant) return string.Empty;
            if (clean.Length <= budget) return clean;
            if (hit < 0) hit = 0;

            int before = budget / 3;
            int start = Math.Max(0, hit - before);
            if (start + budget > clean.Length) start = Math.Max(0, clean.Length - budget);
            string window = clean.Substring(start, Math.Min(budget, clean.Length - start)).Trim();
            if (start > 0) window = "..." + window;
            if (start + budget < clean.Length) window += "...";
            return window;
        }

        private static int FindBestTokenHit(string text, IList<string> tokens)
        {
            if (string.IsNullOrWhiteSpace(text) || tokens == null || tokens.Count == 0) return -1;
            string lower = text.ToLowerInvariant();
            int best = -1;
            int bestWeight = -1;
            for (int i = 0; i < tokens.Count; i++)
            {
                int at = lower.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase);
                if (at < 0) continue;
                int weight = tokens[i].Length;
                if (weight > bestWeight) { best = at; bestWeight = weight; }
            }
            return best;
        }

        private static int ScorePageRelevance(string title, string text, string query)
        {
            List<string> tokens = BuildQueryTokens(query);
            string cleanTitle = NormalizeRelevanceText(title);
            string cleanQuery = NormalizeRelevanceText(query);
            string cleanText = NormalizeRelevanceText(RemovePlaintextBoilerplate(text));
            if (tokens.Count == 0) return string.IsNullOrWhiteSpace(cleanText) ? 0 : 20;

            int score = ScoreTitleRelevance(title, query, tokens);
            int bodyMatches = 0;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (cleanText.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 4;
                    bodyMatches++;
                }
            }

            // A generic navigation/index page that merely contains one query token is not useful
            // evidence. Exact/title-relevant entity pages remain eligible even when their lead is terse.
            bool titleMatched = score - (bodyMatches * 4) >= 20;
            if (!titleMatched && bodyMatches < Math.Min(2, tokens.Count)) return 0;
            if (cleanTitle.Length == 0 || cleanQuery.Length == 0) return score;
            return score;
        }

        private static int ScoreTitleRelevance(string title, string query, IList<string> tokens)
        {
            string cleanTitle = NormalizeRelevanceText(title);
            string cleanQuery = NormalizeRelevanceText(query);
            if (cleanTitle.Length == 0) return 0;
            int score = 0;
            if (cleanTitle == cleanQuery) score += 120;
            else if (cleanQuery.Length > 0 && (cleanQuery.Contains(cleanTitle) || cleanTitle.Contains(cleanQuery))) score += 60;
            for (int i = 0; tokens != null && i < tokens.Count; i++)
                if (cleanTitle.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0) score += 22;
            return score;
        }

        private static List<string> BuildQueryTokens(string query)
        {
            string[] rawTokens = Regex.Split(query == null ? string.Empty : query.ToLowerInvariant(), @"[^a-z0-9+]+", RegexOptions.IgnoreCase);
            List<string> tokens = new List<string>();
            for (int i = 0; i < rawTokens.Length; i++)
            {
                string token = rawTokens[i].Trim();
                if (token.Length < 3 || IsQueryStopWord(token)) continue;
                if (!tokens.Contains(token)) tokens.Add(token);
            }
            tokens.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
            return tokens;
        }

        private static string NormalizeRelevanceText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string clean = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9+]+", " ");
            return Regex.Replace(clean, @"\s+", " ").Trim();
        }

        private static bool IsQueryStopWord(string token)
        {
            switch (token)
            {
                case "where": case "what": case "when": case "which": case "with": case "from":
                case "have": case "this": case "that": case "does": case "about": case "there":
                case "could": case "would": case "should": case "search": case "lookup": case "wiki":
                case "main": case "near": case "around": case "find": case "tell": case "please":
                case "news": case "any": case "heard": case "route": case "way": case "get": case "reach":
                    return true;
                default: return false;
            }
        }

        private static string CollapseWhitespace(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string clean = value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
            clean = Regex.Replace(clean, @"\s+", " ");
            clean = Regex.Replace(clean, @"\s*;\s*;\s*", " ; ");
            clean = Regex.Replace(clean, @"\s*\|\s*\|\s*", " | ");
            return clean.Trim(' ', ';', '|');
        }

        // Pure parser/relevance hooks used only by the deterministic local fixture tests.
        internal static string StripBoilerplateForTests(string html) { return StripHtml(html); }
        internal static int RelevanceScoreForTests(string title, string text, string query) { return ScorePageRelevance(title, text, query); }
        internal static string RelevantWindowForTests(string title, string text, string query, int maxChars) { return SelectRelevantWindow(text, query, maxChars, title); }

        private void Cache(string key, WikiResult result)
        {
            lock (_cacheLock)
            {
                if (_cache.Count > 80) _cache.Clear();
                CachedWikiResult item = new CachedWikiResult();
                item.Utc = DateTime.UtcNow;
                item.Result = Clone(result);
                _cache[key] = item;
            }
        }

        private static WikiResult NotFound(string query)
        {
            WikiResult result = new WikiResult();
            result.Query = query;
            result.Title = string.Empty;
            result.Extract = string.Empty;
            result.Url = string.Empty;
            result.SourceLabel = "Erenshor community wiki";
            result.Found = false;
            return result;
        }

        private static WikiResult Clone(WikiResult source)
        {
            WikiResult copy = new WikiResult();
            if (source == null) return copy;
            copy.Query = source.Query;
            copy.Title = source.Title;
            copy.Extract = source.Extract;
            copy.Url = source.Url;
            copy.SourceLabel = source.SourceLabel;
            copy.Found = source.Found;
            return copy;
        }

        private static string CleanWikiText(string value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string clean = value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
            while (clean.Contains("  ")) clean = clean.Replace("  ", " ");
            if (clean.Length > maxChars) clean = clean.Substring(0, maxChars).TrimEnd() + "...";
            return clean;
        }

        private static int FindJsonProperty(string json, string propertyName, int startIndex)
        {
            string needle = "\"" + propertyName + "\"";
            int pos = Math.Max(0, startIndex);
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

        private static string ExtractJsonString(string json, string propertyName, int startIndex)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int key = FindJsonProperty(json, propertyName, startIndex);
            if (key < 0) return null;
            int colon = json.IndexOf(':', key + propertyName.Length + 2);
            if (colon < 0) return null;
            int pos = colon + 1;
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
            if (pos >= json.Length || json[pos] != '"') return null;
            pos++;

            StringBuilder sb = new StringBuilder();
            while (pos < json.Length)
            {
                char c = json[pos++];
                if (c == '"') return sb.ToString();
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
            return null;
        }

        private static string TrimForLog(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "<empty>";
            string clean = value.Replace("\r", " ").Replace("\n", " ");
            return clean.Length <= max ? clean : clean.Substring(0, max) + "...";
        }

        private class CachedWikiResult
        {
            public DateTime Utc;
            public WikiResult Result;
        }

        private class PageExtract
        {
            public string Title;
            public string Extract;
        }

        private class RankedPageExtract
        {
            public string Title;
            public string Extract;
            public int Score;
        }
    }

    internal static class KnowledgeQueryClassifier
    {
        internal static bool ShouldLookup(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;

            // Temporal/current real-world news intent belongs to ExternalNewsClient. Keep this guard
            // before the generic game-knowledge "news on X" form so routing is deterministic.
            if (ExternalNewsQueryClassifier.ShouldLookup(message)) return false;

            string normalized = Regex.Replace(message.Trim().ToLowerInvariant(), @"[^a-z0-9+\s']", " ");
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            string m = " " + normalized + " ";
            string[] strong = new string[]
            {
                " wiki ", " look up ", " lookup ", " search for ", " search the wiki ", " check the wiki ", " check wiki ",
                " where do i get ", " where can i get ", " where can i find ", " where is ", " where does ",
                " who drops ", " what drops ", " drops from ", " drop from ", " spawn ",
                " how do i get ", " how can i get ", " quest ", " quests ", " item ", " boss ", " npc ",
                " what level should ", " where should i level ", " where should we level ",
                " how do we get to ", " how do i get to ", " how can we get to ", " where do we go to ",
                " how do we reach ", " how do i reach ", " route to ", " way to the main ", " get to the main town ", " get to the main city ",
                " where should we head ", " where should we go ", " where do we head ",
                " how do i upgrade ", " how do we upgrade ", " upgrade my equipment ", " upgrade equipment ", " upgrade gear ",
                " forge ", " forging ", " item quality ", " merging vessel ", " make +1 ", " get +1 ", " +1 gear ", " +1 equipment ",
                " combine items ", " merge items ", " craft ", " any news on ", " any news about "
            };
            for (int i = 0; i < strong.Length; i++) if (m.Contains(strong[i])) return true;

            string[] classes = new string[] { "arcanist", "druid", "paladin", "reaver", "stormcaller", "windblade", "duelist" };
            bool mentionsClass = false;
            for (int i = 0; i < classes.Length; i++)
            {
                if (m.Contains(" " + classes[i] + " ")) { mentionsClass = true; break; }
            }
            if (mentionsClass)
            {
                string[] questionSignals = new string[]
                {
                    " what ", " how ", " tell me ", " role ", " build ", " tank ", " dps ",
                    " good ", " do ", " does ", " can ", " should ", " abilities ", " skills "
                };
                for (int i = 0; i < questionSignals.Length; i++) if (m.Contains(questionSignals[i])) return true;
            }
            return false;
        }

        internal static string ExtractSearchQuery(string message, string currentScene)
        {
            if (string.IsNullOrWhiteSpace(message)) return string.Empty;
            string original = message.Trim();
            string lowerOriginal = original.ToLowerInvariant();
            string scene = NormalizeScene(currentScene);

            Match gameNews = Regex.Match(original, @"^\s*any\s+news\s+(?:on|about)\s+(.+?)\s*[?!.]*\s*$", RegexOptions.IgnoreCase);
            if (gameNews.Success) return CleanQueryTail(gameNews.Groups[1].Value);

            string[] currentClasses = new string[] { "Arcanist", "Druid", "Paladin", "Reaver", "Stormcaller", "Windblade" };
            for (int i = 0; i < currentClasses.Length; i++)
            {
                if (lowerOriginal.Contains(currentClasses[i].ToLowerInvariant())) return currentClasses[i];
            }
            if (lowerOriginal.Contains("duelist")) return "Windblade";

            if (lowerOriginal.Contains("forge") || lowerOriginal.Contains("forging") || lowerOriginal.Contains("item quality") ||
                lowerOriginal.Contains("merging vessel") || lowerOriginal.Contains("upgrade equipment") || lowerOriginal.Contains("upgrade my equipment") ||
                lowerOriginal.Contains("upgrade gear") || lowerOriginal.Contains("make +1") || lowerOriginal.Contains("get +1") ||
                lowerOriginal.Contains("+1 gear") || lowerOriginal.Contains("+1 equipment") || lowerOriginal.Contains("combine items") || lowerOriginal.Contains("merge items"))
                return "Item Quality Merging Vessel";

            if (!string.IsNullOrWhiteSpace(scene) && (lowerOriginal.Contains("where should we head") || lowerOriginal.Contains("where should we go") || lowerOriginal.Contains("where do we head")))
                return scene;

            if ((lowerOriginal.Contains("quest") || lowerOriginal.Contains("quests")) && !string.IsNullOrWhiteSpace(scene))
            {
                if (lowerOriginal.Contains(" here") || lowerOriginal.Contains(" this zone") || lowerOriginal.Contains(" near ") ||
                    lowerOriginal.Contains(" around ") || lowerOriginal.Contains(scene.ToLowerInvariant()))
                    return scene + " quests";
            }

            // Route questions should query the destination entity itself. The current scene can still
            // be included in the LLM's world context, but prepending it here pollutes exact-title lookup.
            bool route = lowerOriginal.Contains("get to") || lowerOriginal.Contains("reach") || lowerOriginal.Contains("route to") ||
                         lowerOriginal.Contains("way to") || lowerOriginal.Contains("where do we go");
            if (route && !string.IsNullOrWhiteSpace(scene))
            {
                string destination = original;
                string[] routePrefixes = new string[]
                {
                    "where do we go to get to ", "how do we get to ", "how do i get to ", "how can we get to ",
                    "how do we reach ", "how do i reach ", "route to ", "way to "
                };
                string lower = destination.ToLowerInvariant();
                for (int i = 0; i < routePrefixes.Length; i++)
                {
                    int at = lower.IndexOf(routePrefixes[i], StringComparison.Ordinal);
                    if (at >= 0)
                    {
                        destination = destination.Substring(at + routePrefixes[i].Length).Trim();
                        break;
                    }
                }
                destination = CleanQueryTail(destination);
                return string.IsNullOrWhiteSpace(destination) ? scene : destination;
            }

            string q = original;
            string lowerQ = q.ToLowerInvariant();
            string[] polite = new string[] { "can you ", "could you ", "would you ", "please " };
            for (int i = 0; i < polite.Length; i++)
            {
                if (lowerQ.StartsWith(polite[i])) { q = q.Substring(polite[i].Length).Trim(); lowerQ = q.ToLowerInvariant(); break; }
            }

            string[] prefixes = new string[]
            {
                "search the wiki for ", "check the wiki for ", "check wiki for ",
                "wiki ", "look up ", "lookup ", "search for ",
                "where do i get ", "where can i get ", "where can i find ", "where is ", "where does ",
                "who drops ", "what drops ", "how do i get ", "how can i get "
            };
            for (int i = 0; i < prefixes.Length; i++)
            {
                if (!lowerQ.StartsWith(prefixes[i])) continue;
                q = q.Substring(prefixes[i].Length).Trim();
                break;
            }

            q = CleanQueryTail(q);
            if (q.StartsWith("the ", StringComparison.OrdinalIgnoreCase)) q = q.Substring(4).Trim();
            return q;
        }

        private static string CleanQueryTail(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return string.Empty;
            return q.Trim(' ', '\t', '\r', '\n', '?', '!', '.', ',', ':', ';', '"', '\'');
        }

        private static string NormalizeScene(string scene)
        {
            if (string.IsNullOrWhiteSpace(scene)) return string.Empty;
            string s = scene.Trim();
            if (string.Equals(s, "Vitheo", StringComparison.OrdinalIgnoreCase)) return "Vitheo's Watch";
            s = s.Replace("_", " ");
            s = Regex.Replace(s, "([a-z])([A-Z])", "$1 $2");
            return s.Trim();
        }
    }
}
