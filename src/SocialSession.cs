using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ErenshorDeepSims
{
    internal enum SemanticTurnType { DirectQuestion, Statement, Opinion, PersonalPreference, SocialQuestion, Reaction, Humor, Joke, Greeting, CommandLike, Other }
    internal enum KnowledgeNeed { None, GameWiki, ExternalNews, BothAmbiguous }
    internal enum SessionEventProvenance { VerifiedWorld, PlayerSaid, SimSaid, SoftPersona, ExternalKnowledge, InferenceOnly }

    // Reflection is allowed to preserve attributed dialogue, but must not turn a Sim question or
    // claim into a player belief. This is deliberately a narrow lexical provenance check rather
    // than a general semantic validator: strong player-attribution summaries need a topical
    // PlayerSaid event in the exact reflection delta.
    internal static class ReflectionProvenanceGuard
    {
        private static readonly string[] PlayerAttribution = new string[]
        {
            "player confirmed", "player prefers", "player wants", "player likes", "player dislikes",
            "player believes", "player intends", "player said", "player is "
        };

        internal static bool Allows(string summary, IList<SessionSocialEvent> evidence)
        {
            if (!HasStrongPlayerAttribution(summary)) return true;
            HashSet<string> summaryTokens = Tokens(summary);
            if (evidence == null) return false;
            for (int i = 0; i < evidence.Count; i++)
            {
                SessionSocialEvent evt = evidence[i];
                if (evt == null || evt.Provenance != SessionEventProvenance.PlayerSaid) continue;
                if (Overlap(summaryTokens, Tokens(evt.Text)) > 0) return true;
            }
            return false;
        }

        internal static bool HasStrongPlayerAttribution(string summary)
        {
            string lower = (summary ?? string.Empty).ToLowerInvariant();
            for (int i = 0; i < PlayerAttribution.Length; i++)
                if (lower.IndexOf(PlayerAttribution[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static HashSet<string> Tokens(string text)
        {
            string[] ignored = new string[] { "player", "confirmed", "prefers", "wants", "likes", "dislikes", "believes", "intends", "said", "still", "just", "that", "this", "with", "from", "have", "been", "more", "than", "about" };
            HashSet<string> skip = new HashSet<string>(ignored, StringComparer.OrdinalIgnoreCase);
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] pieces = Regex.Split((text ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9']+");
            for (int i = 0; i < pieces.Length; i++)
            {
                string token = pieces[i];
                if (token.Length >= 3 && !skip.Contains(token)) result.Add(token);
            }
            return result;
        }

        private static int Overlap(HashSet<string> a, HashSet<string> b)
        {
            int count = 0;
            foreach (string token in a) if (b.Contains(token)) count++;
            return count;
        }
    }

    internal sealed class SemanticTurnRoute
    {
        internal SemanticTurnType TurnType;
        internal KnowledgeNeed KnowledgeNeed;
        internal string Topic = "general";
        internal string Subject = string.Empty;
        internal string SearchQuery = string.Empty;
        internal double Confidence;
        internal bool DirectAnswerRequired;
        internal string SocialIntent = "respond";
    }

    internal static class SemanticTurnRouter
    {
        internal static List<ChatMessage> BuildClassificationPrompt(string message, string recentTopic)
        {
            List<ChatMessage> result = new List<ChatMessage>();
            result.Add(new ChatMessage("system", "Classify one player party-chat turn. Return exactly seven lines: TurnType=DirectQuestion|Statement|Opinion|PersonalPreference|SocialQuestion|Reaction|Humor|Joke|Greeting|CommandLike|Other; KnowledgeNeed=None|GameWiki|ExternalNews|BothAmbiguous; Topic=short topic; Subject=resolved subject; SearchQuery=useful query or blank; Confidence=0..1; DirectAnswerRequired=true|false. Opinions, preferences, feelings, humor, greetings, casual social questions, and questions about a Sim's own tastes use KnowledgeNeed=None unless a separate specific factual claim is required. Game mechanics/items/zones use GameWiki. Current real-world events use ExternalNews. Do not answer the player."));
            if (!string.IsNullOrWhiteSpace(recentTopic)) result.Add(new ChatMessage("system", "Recent thread topic: " + Bound(recentTopic, 80)));
            result.Add(new ChatMessage("user", Bound(message, 500)));
            return result;
        }

        // Diagnostic-only record of what the classifier model actually returned BEFORE the
        // deterministic corrections below rewrite it in place. Production callers use the
        // three-argument overload and are completely unaffected.
        internal sealed class SemanticRouteTrace
        {
            internal bool HasRawClassifier;
            internal SemanticTurnType RawTurnType;
            internal KnowledgeNeed RawKnowledgeNeed;
            internal string RawTopic = string.Empty;
            internal string RawSubject = string.Empty;
            internal string RawSearchQuery = string.Empty;
            internal double RawConfidence;
            internal bool RawDirectAnswerRequired;
            internal readonly List<string> Corrections = new List<string>();
        }

        internal static bool TryParse(string raw, string original, out SemanticTurnRoute route)
        {
            return TryParse(raw, original, out route, null);
        }

        internal static bool TryParse(string raw, string original, out SemanticTurnRoute route, SemanticRouteTrace trace)
        {
            route = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = raw.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int equals = lines[i].IndexOf('=');
                if (equals <= 0) continue;
                fields[lines[i].Substring(0, equals).Trim()] = lines[i].Substring(equals + 1).Trim();
            }
            string turnText, needText;
            SemanticTurnType turn;
            KnowledgeNeed need;
            if (!fields.TryGetValue("TurnType", out turnText) || !Enum.TryParse<SemanticTurnType>(turnText, true, out turn)) return false;
            if (!fields.TryGetValue("KnowledgeNeed", out needText) || !Enum.TryParse<KnowledgeNeed>(needText, true, out need)) return false;
            SemanticTurnRoute parsed = new SemanticTurnRoute();
            parsed.TurnType = turn;
            parsed.KnowledgeNeed = need;
            string value;
            if (fields.TryGetValue("Topic", out value)) parsed.Topic = Bound(value, 80);
            if (fields.TryGetValue("Subject", out value)) parsed.Subject = Bound(value, 100);
            if (fields.TryGetValue("SearchQuery", out value)) parsed.SearchQuery = NormalizeSearchQuery(value, need);
            double confidence;
            parsed.Confidence = fields.TryGetValue("Confidence", out value) && double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out confidence)
                ? Math.Max(0.0, Math.Min(1.0, confidence)) : 0.5;
            bool required;
            parsed.DirectAnswerRequired = fields.TryGetValue("DirectAnswerRequired", out value) && bool.TryParse(value, out required)
                ? required : turn != SemanticTurnType.CommandLike;
            parsed.SocialIntent = turn == SemanticTurnType.DirectQuestion ? "answer" : turn == SemanticTurnType.Statement || turn == SemanticTurnType.Opinion ? "acknowledge_and_react" : "respond";
            CaptureRawClassifier(trace, parsed);
            SemanticTurnType beforeMeaning = parsed.TurnType;
            KnowledgeNeed beforeMeaningNeed = parsed.KnowledgeNeed;
            ApplyMeaningOverride(parsed, original);
            NoteCorrection(trace, "ApplyMeaningOverride", beforeMeaning != parsed.TurnType || beforeMeaningNeed != parsed.KnowledgeNeed);
            KnowledgeNeed beforeNoRetrieval = parsed.KnowledgeNeed;
            ApplyNoRetrievalRule(parsed);
            NoteCorrection(trace, "ApplyNoRetrievalRule", beforeNoRetrieval != parsed.KnowledgeNeed);
            if (parsed.KnowledgeNeed != KnowledgeNeed.None && string.IsNullOrWhiteSpace(parsed.SearchQuery)) parsed.SearchQuery = BuildUsefulSearchQuery(original, parsed.KnowledgeNeed);
            route = parsed;
            return true;
        }

        private static void CaptureRawClassifier(SemanticRouteTrace trace, SemanticTurnRoute parsed)
        {
            if (trace == null || parsed == null) return;
            trace.HasRawClassifier = true;
            trace.RawTurnType = parsed.TurnType;
            trace.RawKnowledgeNeed = parsed.KnowledgeNeed;
            trace.RawTopic = parsed.Topic ?? string.Empty;
            trace.RawSubject = parsed.Subject ?? string.Empty;
            trace.RawSearchQuery = parsed.SearchQuery ?? string.Empty;
            trace.RawConfidence = parsed.Confidence;
            trace.RawDirectAnswerRequired = parsed.DirectAnswerRequired;
        }

        private static void NoteCorrection(SemanticRouteTrace trace, string name, bool changed)
        {
            if (trace == null || !changed) return;
            if (!trace.Corrections.Contains(name)) trace.Corrections.Add(name);
        }

        // A deterministic fail-safe, not the primary route. The live path asks the small model first.
        internal static SemanticTurnRoute Fallback(string message)
        {
            string text = (message ?? string.Empty).Trim();
            string lower = text.ToLowerInvariant();
            SemanticTurnRoute route = new SemanticTurnRoute();
            route.DirectAnswerRequired = text.Length > 0;
            route.TurnType = text.IndexOf('?') >= 0 || Regex.IsMatch(lower, @"^(what|where|who|when|why|how|did|does|do|is|are|can|could|would)\b")
                ? SemanticTurnType.DirectQuestion : SemanticTurnType.Statement;
            if (Regex.IsMatch(lower, @"\b(?:do you like|what do you like|do you think|being a|are .* fun|what are you reading|how do you feel|i (?:like|love|hate|prefer|think|feel)|imo|my favorite|my favourite)\b"))
                route.TurnType = lower.IndexOf("reading", StringComparison.OrdinalIgnoreCase) >= 0 ? SemanticTurnType.SocialQuestion : SemanticTurnType.PersonalPreference;
            if (Regex.IsMatch(lower, @"^(hi|hey|hello|yo|sup)\b")) route.TurnType = SemanticTurnType.Greeting;
            if (Regex.IsMatch(lower, @"\b(today|tonight|latest|current|recent|news)\b") && Regex.IsMatch(lower, @"\b(spacex|nasa|openai|world|news|ukraine|election|market|company)\b")) route.KnowledgeNeed = KnowledgeNeed.ExternalNews;
            else if (KnowledgeQueryClassifier.ShouldLookup(text) ||
                Regex.IsMatch(lower, @"\b(?:what abilities|what skills|what spells)\b") ||
                Regex.IsMatch(lower, @"\b(?:latest|newest|recent)\s+(?:erenshor\s+)?(?:patch|update)\b|patch notes\b")) route.KnowledgeNeed = KnowledgeNeed.GameWiki;
            else route.KnowledgeNeed = KnowledgeNeed.None;
            ApplyMeaningOverride(route, text);
            ApplyNoRetrievalRule(route);
            route.Topic = PromptBuilder.ClassifyThreadTopic(text);
            route.Subject = ExtractSubject(text);
            route.SearchQuery = route.KnowledgeNeed == KnowledgeNeed.None ? string.Empty : BuildUsefulSearchQuery(text, route.KnowledgeNeed);
            route.Confidence = 0.45;
            route.SocialIntent = route.TurnType == SemanticTurnType.DirectQuestion ? "answer" : "acknowledge_and_react";
            return route;
        }

        internal static string BuildUsefulSearchQuery(string message, KnowledgeNeed need)
        {
            string text = Regex.Replace(message ?? string.Empty, @"[/<>\r\n]", " ");
            text = Regex.Replace(text, @"\b(?:hey|guys|anyone|does anyone know|do you know|can you tell me|please|pls|in erenshor|erenshor)\b", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b(?:where does|where do|what does|what do|how do i|how can i|did anything happen with|what happened with|what's going on with|whats going on with)\b", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"[^A-Za-z0-9'\- ]", " ");
            text = Regex.Replace(text, @"\s+", " ").Trim();
            if (text.Length > 100) text = text.Substring(0, 100).TrimEnd();
            if (need == KnowledgeNeed.GameWiki)
            {
                text = Regex.Replace(text, @"\b(drop|drops|dropped|level|location|located|found|get|obtain)$", string.Empty, RegexOptions.IgnoreCase).Trim();
                if (text.Length == 0) text = "Erenshor game information";
                return TitleWords(text);
            }
            if (text.Length == 0) text = "current world news";
            if (!Regex.IsMatch(text, @"\b(today|latest|recent|current)\b", RegexOptions.IgnoreCase)) text += " latest news";
            return text;
        }

        internal static string LookupAcknowledgement(SimSnapshot speaker, SemanticTurnRoute route)
        {
            string seedText = (speaker == null ? string.Empty : speaker.Name) + "|" + (route == null ? string.Empty : route.Topic);
            int seed = StableHash(seedText);
            string[] lines = new string[] { "give me a sec, i'll check", "not sure, let me look", "hang on, i'll check", "let me look that up" };
            return lines[Math.Abs(seed == int.MinValue ? 0 : seed) % lines.Length];
        }

        // Deterministic meaning override applied after the small-model classifier as well as in the
        // fallback classifier. The model is allowed to resolve ambiguous turns, but it may not turn
        // an explicit personal-taste question into a wiki request merely because the subject happens
        // to be an Erenshor class/item noun. Conversely, current-event and mechanics wording remain
        // factual even when they contain conversational words such as "think".
        internal static void ApplyMeaningOverride(SemanticTurnRoute route, string original)
        {
            if (route == null || string.IsNullOrWhiteSpace(original)) return;
            string lower = original.Trim().ToLowerInvariant();

            bool currentExternalFact = Regex.IsMatch(lower, @"\b(today|latest|current|recent|news|happened|happening)\b") &&
                Regex.IsMatch(lower, @"\b(nasa|spacex|openai|world|ukraine|election|market|company|news)\b");
            bool explicitGameFact = Regex.IsMatch(lower, @"\b(?:where does|where do|how do i|how can i|what abilities|what skills|what spells|what drops|what does .* drop|what is the drop rate|where is|where are)\b") ||
                Regex.IsMatch(lower, @"\bwhat do you think (?:is|are|happened|caused|drops|drop)\b");
            if (currentExternalFact || explicitGameFact) return;

            bool personalIdentity = Regex.IsMatch(lower, @"\b(?:your background|your past|who are you|tell me about yourself|what did you study|what do you study|where did you grow up|what do you do for work|what(?:'s| is) your job|your career|how do you know me|how did we meet|were we|did we|do you remember|what do you remember about)\b");
            if (personalIdentity)
            {
                route.TurnType = SemanticTurnType.SocialQuestion;
                route.KnowledgeNeed = KnowledgeNeed.None;
                route.SearchQuery = string.Empty;
                route.DirectAnswerRequired = true;
                route.SocialIntent = "answer_personal_identity_or_memory";
                return;
            }

            bool personalTaste = Regex.IsMatch(lower, @"\b(?:do you (?:like|love|enjoy|prefer)|would you (?:rather|prefer)|what do you (?:like|prefer)|what(?:'s| is) your (?:favorite|favourite)|your opinion|how do you feel about|what do you think (?:about|of)|what are you (?:reading|watching|listening to|playing))\b") ||
                Regex.IsMatch(lower, @"\b(?:like|love|enjoy|prefer) being (?:a|an|the)?\s*\w+");
            if (!personalTaste) return;

            route.TurnType = lower.IndexOf("what are you reading", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("what are you watching", StringComparison.Ordinal) >= 0 ||
                lower.IndexOf("what are you listening", StringComparison.Ordinal) >= 0
                ? SemanticTurnType.SocialQuestion
                : SemanticTurnType.PersonalPreference;
            route.KnowledgeNeed = KnowledgeNeed.None;
            route.SearchQuery = string.Empty;
            route.DirectAnswerRequired = true;
            route.SocialIntent = "answer_personal_opinion";
        }

        // A class name or other topical noun must not reopen lookup after the semantic route has
        // recognized a normal social turn.
        internal static void ApplyNoRetrievalRule(SemanticTurnRoute route)
        {
            if (route == null) return;
            switch (route.TurnType)
            {
                case SemanticTurnType.Opinion:
                case SemanticTurnType.PersonalPreference:
                case SemanticTurnType.SocialQuestion:
                case SemanticTurnType.Reaction:
                case SemanticTurnType.Humor:
                case SemanticTurnType.Joke:
                case SemanticTurnType.Greeting:
                    route.KnowledgeNeed = KnowledgeNeed.None;
                    route.SearchQuery = string.Empty;
                    break;
            }
        }

        private static string NormalizeSearchQuery(string value, KnowledgeNeed need)
        {
            string clean = Bound(Regex.Replace(value ?? string.Empty, @"[\r\n<>]", " ").Trim(), 120);
            if (clean.Length < 3) return string.Empty;
            return need == KnowledgeNeed.GameWiki ? TitleWords(clean) : clean;
        }

        private static string ExtractSubject(string message)
        {
            string clean = Regex.Replace(message ?? string.Empty, @"[^A-Za-z0-9'\- ]", " ");
            clean = Regex.Replace(clean, @"\s+", " ").Trim();
            return Bound(clean, 100);
        }

        private static string TitleWords(string value)
        {
            string[] words = (value ?? string.Empty).Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++) if (words[i].Length > 0) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words);
        }

        private static string Bound(string value, int max) { string v = (value ?? string.Empty).Trim(); return v.Length <= max ? v : v.Substring(0, max).TrimEnd(); }
        private static int StableHash(string value) { unchecked { int h = 17; string v = value ?? string.Empty; for (int i = 0; i < v.Length; i++) h = h * 31 + v[i]; return h; } }
    }

    internal sealed class SessionSocialEvent
    {
        internal long Id;
        internal DateTime Utc;
        internal SessionEventProvenance Provenance;
        internal string Type;
        internal string Text;
        internal string Topic;
        internal List<string> Participants = new List<string>();
        internal List<string> Witnesses = new List<string>();
        internal string Zone = string.Empty;
        internal int Importance;
        internal double Novelty;
        internal long ThreadId;
    }

    internal sealed class SessionChatLine
    {
        internal string Speaker;
        internal string Text;
        internal DateTime Utc;
        internal SessionEventProvenance Provenance;
        internal long ThreadId;
    }

    internal sealed class SessionConversationSeed
    {
        internal string SeedId;
        internal string TopicKey;
        internal SessionEventProvenance Provenance;
        internal string Context;
        internal DateTime CreatedUtc;
        internal DateTime ExpiresUtc;
        internal int Importance;
        internal double Novelty;
        internal double Fatigue;
        internal double ConversationPotential;
        internal List<string> EligibleSpeakers = new List<string>();
    }

    internal sealed class SocialSessionState
    {
        private const int MaxEvents = 64;
        private const int MaxChat = 12;
        private const int MaxSummaryChars = 1200;
        internal const double AutonomousThreadHardCeilingSeconds = 60.0;
        internal const double PlayerThreadHardCeilingSeconds = 120.0;
        internal const double ThreadVisibleInactivitySeconds = 40.0;
        private readonly object _lock = new object();
        private readonly List<SessionSocialEvent> _events = new List<SessionSocialEvent>();
        private readonly List<SessionChatLine> _chat = new List<SessionChatLine>();
        private long _nextEventId = 1;
        private long _threadId;
        private string _threadTopic = string.Empty;
        private DateTime _threadStartedUtc = DateTime.MinValue;
        private DateTime _threadLastActivityUtc = DateTime.MinValue;
        private DateTime _threadLastVisibleUtc = DateTime.MinValue;
        private DateTime _threadPendingUntilUtc = DateTime.MinValue;
        private bool _threadInferencePending;
        private bool _threadPlayerOwned;
        private bool _threadOpen;
        private int _threadVisibleReplies;
        private int _threadReplyCap = 1;
        private string _pendingCloseDiagnostic = string.Empty;
        private long _lastReflectedEventId;
        private string _summary = string.Empty;
        private string _characterKey = string.Empty;

        internal void ResetForCharacter(string characterKey)
        {
            lock (_lock)
            {
                if (string.Equals(_characterKey, characterKey ?? string.Empty, StringComparison.Ordinal)) return;
                _characterKey = characterKey ?? string.Empty;
                _events.Clear(); _chat.Clear(); _summary = string.Empty; _threadId = 0; _threadTopic = string.Empty;
                _threadVisibleReplies = 0; _threadStartedUtc = DateTime.MinValue; _threadLastActivityUtc = DateTime.MinValue;
                _threadLastVisibleUtc = DateTime.MinValue; _threadPendingUntilUtc = DateTime.MinValue;
                _threadInferencePending = false; _threadPlayerOwned = false; _threadOpen = false;
                _threadReplyCap = 1; _pendingCloseDiagnostic = string.Empty; _lastReflectedEventId = 0; _nextEventId = 1;
            }
        }

        internal long BeginPlayerTurn(string player, string text, string topic, DateTime now)
        {
            lock (_lock)
            {
                PruneThreadLocked(now);
                bool continueThread = _threadOpen &&
                    (string.Equals(_threadTopic, topic, StringComparison.OrdinalIgnoreCase) || string.Equals(topic, "general party chat", StringComparison.OrdinalIgnoreCase));
                if (!continueThread)
                {
                    if (_threadOpen) CloseThreadLocked("topic_changed", now);
                    _threadId++;
                    _threadTopic = topic ?? "general";
                    _threadVisibleReplies = 0;
                }
                // Only an actual player-authored continuation may renew the player ceiling.
                _threadStartedUtc = now;
                _threadLastActivityUtc = now;
                _threadLastVisibleUtc = now;
                _threadPendingUntilUtc = DateTime.MinValue;
                _threadInferencePending = false;
                _threadPlayerOwned = true;
                _threadOpen = true;
                AddChatLocked(player, text, SessionEventProvenance.PlayerSaid, now, _threadId);
                AddEventLocked("player_message", text, _threadTopic, SessionEventProvenance.PlayerSaid, new string[] { player }, 35, 1.0, _threadId, now);
                return _threadId;
            }
        }

        internal void RecordVisibleSim(string speaker, string text, DateTime now)
        {
            lock (_lock)
            {
                PruneThreadLocked(now);
                if (!_threadOpen)
                {
                    _threadId++;
                    _threadTopic = "autonomous social";
                    _threadStartedUtc = now;
                    _threadPlayerOwned = false;
                    _threadVisibleReplies = 0;
                    _threadReplyCap = Math.Max(1, _threadReplyCap);
                    _threadOpen = true;
                }
                AddChatLocked(speaker, text, SessionEventProvenance.SimSaid, now, _threadId);
                AddEventLocked("sim_message", text, _threadTopic, SessionEventProvenance.SimSaid, new string[] { speaker }, 20, 0.6, _threadId, now);
                _threadVisibleReplies++;
                _threadLastVisibleUtc = now;
                _threadLastActivityUtc = now;
                _threadPendingUntilUtc = DateTime.MinValue;
            }
        }

        internal void NoteThreadPending(DateTime untilUtc, int replyCap, DateTime now)
        {
            lock (_lock)
            {
                if (!PruneThreadLocked(now)) return;
                DateTime ceiling = _threadStartedUtc.AddSeconds(_threadPlayerOwned ? PlayerThreadHardCeilingSeconds : AutonomousThreadHardCeilingSeconds);
                _threadPendingUntilUtc = untilUtc < ceiling ? untilUtc : ceiling;
                _threadReplyCap = Math.Max(1, replyCap);
                _threadLastActivityUtc = now;
            }
        }

        internal void SetThreadInferencePending(bool pending, DateTime now)
        {
            lock (_lock)
            {
                if (!PruneThreadLocked(now)) return;
                _threadInferencePending = pending;
                if (pending) _threadLastActivityUtc = now;
            }
        }

        internal void CloseThread(string reason, DateTime now)
        {
            lock (_lock) CloseThreadLocked(reason, now);
        }

        internal string ConsumeCloseDiagnostic()
        {
            lock (_lock)
            {
                string value = _pendingCloseDiagnostic;
                _pendingCloseDiagnostic = string.Empty;
                return value;
            }
        }

        internal void RecordEvent(string type, string text, string topic, SessionEventProvenance provenance, IList<string> participants, int importance, DateTime now)
        {
            lock (_lock) AddEventLocked(type, text, topic, provenance, participants, importance, 1.0, _threadId, now);
        }

        internal bool CanAddThreadReply(int hardCap) { lock (_lock) return _threadVisibleReplies < Math.Max(1, hardCap); }
        internal int PendingReflectionCount { get { lock (_lock) { int n = 0; for (int i = 0; i < _events.Count; i++) if (_events[i].Id > _lastReflectedEventId) n++; return n; } } }

        internal List<SessionSocialEvent> ReflectionDelta()
        {
            lock (_lock)
            {
                List<SessionSocialEvent> result = new List<SessionSocialEvent>();
                for (int i = 0; i < _events.Count; i++) if (_events[i].Id > _lastReflectedEventId) result.Add(_events[i]);
                return result;
            }
        }

        internal void ApplyReflection(string updatedSummary, long throughEventId)
        {
            if (string.IsNullOrWhiteSpace(updatedSummary)) return;
            lock (_lock)
            {
                _summary = Bound(updatedSummary, MaxSummaryChars);
                if (throughEventId > _lastReflectedEventId) _lastReflectedEventId = throughEventId;
            }
        }

        internal string Summary()
        {
            lock (_lock)
            {
                if (!string.IsNullOrWhiteSpace(_summary)) return _summary;
                StringBuilder sb = new StringBuilder();
                int start = Math.Max(0, _events.Count - 10);
                for (int i = start; i < _events.Count; i++)
                {
                    SessionSocialEvent evt = _events[i];
                    if (evt.Provenance == SessionEventProvenance.InferenceOnly) continue;
                    if (sb.Length > 0) sb.Append(" ");
                    sb.Append(evt.Type).Append(": ").Append(Bound(evt.Text, 120)).Append(".");
                    if (sb.Length >= MaxSummaryChars) break;
                }
                return Bound(sb.ToString(), MaxSummaryChars);
            }
        }

        internal List<SessionChatLine> RecentChat() { lock (_lock) return new List<SessionChatLine>(_chat); }

        internal List<SessionConversationSeed> BuildSeeds(DateTime now)
        {
            List<SessionConversationSeed> seeds = new List<SessionConversationSeed>();
            lock (_lock)
            {
                for (int i = _events.Count - 1; i >= 0 && seeds.Count < 8; i--)
                {
                    SessionSocialEvent evt = _events[i];
                    if (evt.Importance < 25 || now - evt.Utc > TimeSpan.FromMinutes(20)) continue;
                    SessionConversationSeed seed = new SessionConversationSeed();
                    seed.SeedId = "session:" + evt.Id;
                    seed.TopicKey = evt.Topic;
                    seed.Provenance = evt.Provenance;
                    seed.Context = evt.Text;
                    seed.CreatedUtc = evt.Utc;
                    seed.ExpiresUtc = evt.Utc.AddMinutes(evt.Provenance == SessionEventProvenance.VerifiedWorld ? 30 : 15);
                    seed.Importance = evt.Importance;
                    seed.Novelty = evt.Novelty;
                    seed.Fatigue = 0.0;
                    seed.ConversationPotential = Math.Min(1.0, (evt.Importance / 100.0) + (evt.Novelty * 0.5));
                    seed.EligibleSpeakers.AddRange(evt.Witnesses);
                    if (seed.ExpiresUtc >= now) seeds.Add(seed);
                }
            }
            return seeds;
        }

        internal static bool CanSpeakFirstPerson(SessionConversationSeed seed, string simName)
        {
            if (seed == null || string.IsNullOrWhiteSpace(simName)) return false;
            for (int i = 0; i < seed.EligibleSpeakers.Count; i++)
                if (string.Equals(seed.EligibleSpeakers[i], simName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal string DescribeThread()
        {
            DateTime now = DateTime.UtcNow;
            lock (_lock)
            {
                PruneThreadLocked(now);
                double age = _threadStartedUtc == DateTime.MinValue ? 0.0 : Math.Max(0.0, (now - _threadStartedUtc).TotalSeconds);
                double visibleAge = _threadLastVisibleUtc == DateTime.MinValue ? -1.0 : Math.Max(0.0, (now - _threadLastVisibleUtc).TotalSeconds);
                double hardRemaining = !_threadOpen ? 0.0 : Math.Max(0.0,
                    ((_threadStartedUtc.AddSeconds(_threadPlayerOwned ? PlayerThreadHardCeilingSeconds : AutonomousThreadHardCeilingSeconds)) - now).TotalSeconds);
                double pendingEta = _threadPendingUntilUtc > now ? (_threadPendingUntilUtc - now).TotalSeconds : 0.0;
                return "thread=" + _threadId + " state=" + (_threadOpen ? "active" : "closed") +
                    " topic=" + (_threadTopic.Length == 0 ? "none" : _threadTopic) +
                    " age=" + Math.Round(age) + "s lastVisibleAge=" + (visibleAge < 0 ? "none" : Math.Round(visibleAge) + "s") +
                    " replies=" + _threadVisibleReplies + "/" + _threadReplyCap +
                    " pendingFollowup=" + (_threadPendingUntilUtc > now ? "yes" : "no") +
                    " inferencePending=" + (_threadInferencePending ? "yes" : "no") +
                    " nextEta=" + Math.Round(pendingEta) + "s staleIn=" + Math.Round(hardRemaining) + "s";
            }
        }

        internal bool HasActiveThread(DateTime now)
        {
            lock (_lock) return PruneThreadLocked(now);
        }

        private bool PruneThreadLocked(DateTime now)
        {
            if (!_threadOpen || _threadId <= 0 || _threadStartedUtc == DateTime.MinValue) return false;
            double hard = _threadPlayerOwned ? PlayerThreadHardCeilingSeconds : AutonomousThreadHardCeilingSeconds;
            if ((now - _threadStartedUtc).TotalSeconds >= hard)
            {
                CloseThreadLocked("hard_ceiling", now);
                return false;
            }
            if (_threadInferencePending || _threadPendingUntilUtc > now) return true;
            DateTime recent = _threadLastVisibleUtc > _threadLastActivityUtc ? _threadLastVisibleUtc : _threadLastActivityUtc;
            if (recent != DateTime.MinValue && (now - recent).TotalSeconds <= ThreadVisibleInactivitySeconds) return true;
            CloseThreadLocked("inactivity", now);
            return false;
        }

        private void CloseThreadLocked(string reason, DateTime now)
        {
            if (!_threadOpen) return;
            double age = _threadStartedUtc == DateTime.MinValue ? 0.0 : Math.Max(0.0, (now - _threadStartedUtc).TotalSeconds);
            _pendingCloseDiagnostic = "[DeepSims][Thread] action=closed reason=" + (string.IsNullOrWhiteSpace(reason) ? "terminal" : reason) +
                " thread=" + _threadId + " age=" + Math.Round(age) + "s replies=" + _threadVisibleReplies + "/" + _threadReplyCap;
            _threadOpen = false;
            _threadInferencePending = false;
            _threadPendingUntilUtc = DateTime.MinValue;
        }

        private void AddChatLocked(string speaker, string text, SessionEventProvenance provenance, DateTime now, long thread)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _chat.Add(new SessionChatLine { Speaker = speaker ?? string.Empty, Text = Bound(text, 300), Provenance = provenance, Utc = now, ThreadId = thread });
            while (_chat.Count > MaxChat) _chat.RemoveAt(0);
        }

        private void AddEventLocked(string type, string text, string topic, SessionEventProvenance provenance, IList<string> participants, int importance, double novelty, long thread, DateTime now)
        {
            SessionSocialEvent evt = new SessionSocialEvent { Id = _nextEventId++, Utc = now, Type = type ?? "event", Text = Bound(text, 400), Topic = Bound(topic, 80), Provenance = provenance, Importance = Math.Max(0, Math.Min(100, importance)), Novelty = Math.Max(0.0, Math.Min(1.0, novelty)), ThreadId = thread };
            if (participants != null) for (int i = 0; i < participants.Count && i < 6; i++) if (!string.IsNullOrWhiteSpace(participants[i])) evt.Participants.Add(Bound(participants[i], 40));
            // Capture witnesses at event time. A Sim joining later cannot use the event as its own memory.
            evt.Witnesses.AddRange(evt.Participants);
            _events.Add(evt);
            while (_events.Count > MaxEvents) _events.RemoveAt(0);
        }

        private static string Bound(string value, int max) { string v = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim(); return v.Length <= max ? v : v.Substring(0, max).TrimEnd(); }
    }

    internal static class DirectPreferenceTopicPolicy
    {
        // Maps an explicitly subjective player turn onto one of the existing bounded SoftPersona
        // topic buckets. This never creates a world fact; it only allows an accepted, actually visible
        // first-person opinion to become future tone continuity after the output boundary confirms it.
        internal static string Resolve(string message, PartyReplyIntent intent)
        {
            if (!PartyReplyIntentClassifier.IsSubjective(intent) || string.IsNullOrWhiteSpace(message)) return string.Empty;
            string m = message.ToLowerInvariant();
            if (Regex.IsMatch(m, @"\b(?:class|tank|tanking|heal|healing|healer|dps|reroll|arcanist|druid|paladin|reaver|stormcaller|windblade|duelist)\b")) return "class_opinion";
            if (Regex.IsMatch(m, @"\b(?:zone|place|area|vibe|atmosphere|scenery)\b")) return "zone_preference";
            if (Regex.IsMatch(m, @"\b(?:pace|pull|pulls|fast|slow|careful)\b")) return "pace_preference";
            if (Regex.IsMatch(m, @"\b(?:gear|armor|armour|weapon|looks|style|fashion)\b")) return "gear_aesthetics";
            if (Regex.IsMatch(m, @"\b(?:enemy|enemies|mob|mobs|monster|monsters|encounter design)\b")) return "enemy_design";
            if (Regex.IsMatch(m, @"\b(?:dungeon|dungeons|grind|grinding|camp|explore|exploring|adventure)\b")) return "future_activity";
            if (Regex.IsMatch(m, @"\b(?:music|listen|listening|food|snack|weather|reading|book|watching)\b")) return "ordinary_downtime";
            return string.Empty;
        }

        // A direct topic label is only permission to remember a preference if the accepted visible
        // answer actually expresses one. Generic uncertainty/acknowledgement fallbacks remain useful
        // conversation history, but must not harden into SoftPersona.
        internal static bool CanEstablishFromVisible(string topicKey, string visibleText)
        {
            if (!PreferenceMemoryPolicy.IsEligible(topicKey, visibleText)) return false;
            string t = visibleText.Trim().ToLowerInvariant();
            if (Regex.IsMatch(t, @"\b(?:not sure|don't know|do not know|can't confirm|cannot confirm|couldn't confirm|no idea|i hear you|what part|can't say|cannot say)\b")) return false;
            return Regex.IsMatch(t, @"\b(?:like|love|enjoy|prefer|rather|favorite|favourite|fun|good|great|nice|boring|stressful|hate|into it|not for me|i'd|i would|i'm|i am)\b");
        }
    }

    internal static class ConnectedBanterThreadPolicy
    {
        // A -> B -> C -> D at most. Each turn past the first is still earned individually by
        // SimResponseDecision plus the momentum roll, so this is the ceiling, not the expected length.
        internal const int ManualTailReplies = SimResponseDecision.MaxResponsesPerLine;

        // The only legal seed for B is chat that has already become visible. The caller records A at
        // the final output boundary before invoking this method. If the exact shown opener is not the
        // newest visible line, the thread is stale or was preempted and must stop.
        internal static bool TryBuildFromVisible(IList<ConversationLine> visible, string openerSpeaker, string openerText, out List<ConversationLine> thread)
        {
            thread = new List<ConversationLine>();
            if (visible == null || visible.Count == 0 || string.IsNullOrWhiteSpace(openerSpeaker) || string.IsNullOrWhiteSpace(openerText)) return false;
            ConversationLine newest = visible[visible.Count - 1];
            if (newest == null || !string.Equals(newest.Speaker, openerSpeaker, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(newest.Text, openerText, StringComparison.Ordinal)) return false;
            int start = Math.Max(0, visible.Count - 5);
            for (int i = start; i < visible.Count; i++)
            {
                ConversationLine line = visible[i];
                if (line != null && !string.IsNullOrWhiteSpace(line.Text)) thread.Add(new ConversationLine(line.Speaker, line.Text));
            }
            return thread.Count > 0;
        }
    }

    internal static class DirectResponseFallback
    {
        internal static string ClassifyRejectionReason(string rejectionReason)
        {
            string reason = (rejectionReason ?? string.Empty).ToLowerInvariant();
            if (reason.IndexOf("topic mismatch", StringComparison.Ordinal) >= 0) return "topic_mismatch";
            if (reason.IndexOf("loot/acquisition", StringComparison.Ordinal) >= 0) return "loot_acquisition";
            if (reason.IndexOf("relationship/entities", StringComparison.Ordinal) >= 0 ||
                reason.IndexOf("retrieved game facts", StringComparison.Ordinal) >= 0) return "retrieved_relationship";
            if (reason.IndexOf("kill/clear", StringComparison.Ordinal) >= 0) return "kill_clear";
            return "other";
        }

        internal static string RenderAfterGroundingRejection(string message, string rejectionReason, SimSnapshot speaker)
        {
            string reason = (rejectionReason ?? string.Empty).ToLowerInvariant();
            if (reason.IndexOf("loot/acquisition", StringComparison.Ordinal) >= 0) return "i can't confirm that item claim";
            if (reason.IndexOf("kill/clear", StringComparison.Ordinal) >= 0) return "i can't confirm that happened";
            if (reason.IndexOf("relationship/entities", StringComparison.Ordinal) >= 0 || reason.IndexOf("retrieved game facts", StringComparison.Ordinal) >= 0) return "i couldn't confirm that cleanly from what i found";
            SemanticTurnRoute route = FallbackForRejectedDirect(message);
            return Render(message, route, speaker, false);
        }

        private static SemanticTurnRoute FallbackForRejectedDirect(string message)
        {
            SemanticTurnRoute route = SemanticTurnRouter.Fallback(message);
            if (route == null) route = new SemanticTurnRoute { TurnType = SemanticTurnType.Statement, KnowledgeNeed = KnowledgeNeed.None };
            return route;
        }

        internal static string Render(string message, SemanticTurnRoute route, SimSnapshot speaker, bool lookupFailed)
        {
            if (lookupFailed) return "couldn't find anything useful on that";
            string text = (message ?? string.Empty).Trim();
            string lower = text.ToLowerInvariant();
            if (Regex.IsMatch(lower, @"\b(i (?:like|love|prefer).{0,30}\btank|being the tank)\b")) return "yeah, tanking fits if you like setting the pace";
            if (Regex.IsMatch(lower, @"\b(i (?:like|love|prefer).{0,30}\bheal|healing)\b")) return "healing's stressful, but keeping everyone up feels good";
            if (route != null && route.TurnType == SemanticTurnType.Greeting) return "hey";
            if (route != null && route.TurnType == SemanticTurnType.DirectQuestion) return "not sure on that oneâ€”what part did you mean?";
            if (route != null && (route.TurnType == SemanticTurnType.Statement || route.TurnType == SemanticTurnType.Opinion))
            {
                string topic = string.IsNullOrWhiteSpace(route.Topic) ? "that" : route.Topic.Replace('_', ' ');
                return "yeah, i can see why you feel that way about " + topic;
            }
            return "yeah, i hear you";
        }
    }

    internal static class SocialOverhaulDeterministicTests
    {
        internal static List<string> Run()
        {
            List<string> lines = new List<string>();
            SemanticTurnRoute social = SemanticTurnRouter.Fallback("what are you guys reading tonight");
            lines.Add("[DeepSims Social] social question does not retrieve: " + Pass(social.KnowledgeNeed == KnowledgeNeed.None));
            SemanticTurnRoute windbladeOpinion = new SemanticTurnRoute { TurnType = SemanticTurnType.Opinion, KnowledgeNeed = KnowledgeNeed.GameWiki, SearchQuery = "Windblade" };
            SemanticTurnRouter.ApplyNoRetrievalRule(windbladeOpinion);
            lines.Add("[DeepSims Social] Windblade opinion overrides class lookup: " + Pass(windbladeOpinion.KnowledgeNeed == KnowledgeNeed.None && windbladeOpinion.SearchQuery.Length == 0));
            SemanticTurnRoute preference = SemanticTurnRouter.Fallback("dancer do you like being a windblade?");
            lines.Add("[DeepSims Social] personal preference does not retrieve: " + Pass(preference.TurnType == SemanticTurnType.PersonalPreference && preference.KnowledgeNeed == KnowledgeNeed.None));
            SemanticTurnRoute misroutedOpinion;
            bool misroutedParsed = SemanticTurnRouter.TryParse("TurnType=DirectQuestion\nKnowledgeNeed=GameWiki\nTopic=Windblade\nSubject=Dancer class\nSearchQuery=Windblade\nConfidence=0.91\nDirectAnswerRequired=true",
                "Dancer, do you like being a Windblade?", out misroutedOpinion);
            lines.Add("[DeepSims Social] model-misrouted class opinion is forced back to social: " + Pass(misroutedParsed && misroutedOpinion.KnowledgeNeed == KnowledgeNeed.None && misroutedOpinion.SearchQuery.Length == 0));
            SemanticTurnRoute enjoyTank = SemanticTurnRouter.Fallback("do you enjoy tanking?");
            lines.Add("[DeepSims Social] enjoy-role opinion stays social: " + Pass(enjoyTank.KnowledgeNeed == KnowledgeNeed.None));
            SemanticTurnRoute druidTaste = SemanticTurnRouter.Fallback("what do you think about being a Druid?");
            lines.Add("[DeepSims Social] class-identity opinion stays social: " + Pass(druidTaste.KnowledgeNeed == KnowledgeNeed.None));
            SemanticTurnRoute game = SemanticTurnRouter.Fallback("where does wolf meat drop?");
            lines.Add("[DeepSims Social] game question uses wiki: " + Pass(game.KnowledgeNeed == KnowledgeNeed.GameWiki));
            lines.Add("[DeepSims Social] useful exact entity query: " + Pass(game.SearchQuery.IndexOf("Wolf Meat", StringComparison.OrdinalIgnoreCase) >= 0 && game.SearchQuery.Length < 60));
            SemanticTurnRoute abilityFacts = SemanticTurnRouter.Fallback("what abilities do Windblades get?");
            lines.Add("[DeepSims Social] factual class ability contrast still uses wiki: " + Pass(abilityFacts.KnowledgeNeed == KnowledgeNeed.GameWiki));
            SemanticTurnRoute news = SemanticTurnRouter.Fallback("did anything happen with SpaceX today?");
            lines.Add("[DeepSims Social] current outside-world question uses news: " + Pass(news.KnowledgeNeed == KnowledgeNeed.ExternalNews));
            lines.Add("[DeepSims Social] lookup acknowledgement is visible: " + Pass(SemanticTurnRouter.LookupAcknowledgement(new SimSnapshot { Name = "Fiora" }, news).Length > 5));
            lines.Add("[DeepSims Social] lookup failure still replies: " + Pass(DirectResponseFallback.Render("where?", game, null, true).Length > 0));
            lines.Add("[DeepSims Social] direct opinion fallback stays relevant: " + Pass(DirectResponseFallback.Render("i like being the tank", social, null, false).IndexOf("tank", StringComparison.OrdinalIgnoreCase) >= 0));
            lines.Add("[DeepSims Social] topic-mismatch direct rejection has fallback: " + Pass(DirectResponseFallback.RenderAfterGroundingRejection("do you like being a Windblade?", "topic mismatch for selected class_opinion", null).Length > 0));
            lines.Add("[DeepSims Social] loot/acquisition direct rejection has fallback: " + Pass(DirectResponseFallback.RenderAfterGroundingRejection("did we get that item?", "unsupported loot/acquisition assertion", null).Length > 0));
            lines.Add("[DeepSims Social] retrieved-relationship direct rejection has fallback: " + Pass(DirectResponseFallback.RenderAfterGroundingRejection("where does it come from?", "answer relationship/entities are not supported by the retrieved game facts", null).Length > 0));
            lines.Add("[DeepSims Social] kill/clear direct rejection has fallback: " + Pass(DirectResponseFallback.RenderAfterGroundingRejection("did we clear that?", "unsupported kill/clear assertion", null).Length > 0));
            lines.Add("[DeepSims Social] rejection categories are privacy-safe and stable: " + Pass(
                DirectResponseFallback.ClassifyRejectionReason("topic mismatch for selected other_sim_preference") == "topic_mismatch" &&
                DirectResponseFallback.ClassifyRejectionReason("unsupported loot/acquisition assertion") == "loot_acquisition" &&
                DirectResponseFallback.ClassifyRejectionReason("answer relationship/entities are not supported by the retrieved game facts") == "retrieved_relationship" &&
                DirectResponseFallback.ClassifyRejectionReason("unsupported kill/clear assertion") == "kill_clear"));
            lines.Add("[DeepSims Social] direct class opinion gets SoftPersona topic: " + Pass(DirectPreferenceTopicPolicy.Resolve("Dancer, do you like being a Windblade?", PartyReplyIntent.Opinion) == "class_opinion"));
            lines.Add("[DeepSims Social] direct reading opinion gets downtime SoftPersona topic: " + Pass(DirectPreferenceTopicPolicy.Resolve("what are you reading tonight?", PartyReplyIntent.SocialBanter) == "ordinary_downtime"));
            lines.Add("[DeepSims Social] factual turn never becomes SoftPersona: " + Pass(DirectPreferenceTopicPolicy.Resolve("what abilities do Windblades get?", PartyReplyIntent.FactualGameQuestion).Length == 0));
            lines.Add("[DeepSims Social] actual visible opinion may establish SoftPersona: " + Pass(DirectPreferenceTopicPolicy.CanEstablishFromVisible("class_opinion", "i like being a windblade")));
            lines.Add("[DeepSims Social] generic direct fallback does not become SoftPersona: " + Pass(!DirectPreferenceTopicPolicy.CanEstablishFromVisible("class_opinion", "not sure on that one")));

            List<SimPreferenceMemory> rememberedPrefs = new List<SimPreferenceMemory>();
            rememberedPrefs.Add(new SimPreferenceMemory { TopicKey = "class_opinion", Statement = "i like being a windblade", TimesExpressed = 1 });
            lines.Add("[DeepSims Social] class-name question retrieves prior SoftPersona: " + Pass(PreferenceMemoryPolicy.Select(rememberedPrefs, "do you like being a Windblade?", 1).Count == 1));
            rememberedPrefs.Add(new SimPreferenceMemory { TopicKey = "ordinary_downtime", Statement = "i am reading an old travel journal", TimesExpressed = 1 });
            lines.Add("[DeepSims Social] reading question retrieves prior downtime SoftPersona: " + Pass(PreferenceMemoryPolicy.Select(rememberedPrefs, "what are you reading tonight?", 1).Count == 1));

            SocialSessionState state = new SocialSessionState();
            state.ResetForCharacter("slot-1-a");
            long thread = state.BeginPlayerTurn("Player", "i like being the tank", "class role preferences", DateTime.UtcNow);
            state.RecordVisibleSim("Fiora", "yeah, tanking fits if you like setting the pace", DateTime.UtcNow);
            state.RecordVisibleSim("Phanty", "healing has its own kind of stress too", DateTime.UtcNow);
            lines.Add("[DeepSims Social] autonomous Sim dialogue is not a durable factual seed: " + Pass(state.BuildSeeds(DateTime.UtcNow).FindAll(delegate(SessionConversationSeed x) { return x.Provenance == SessionEventProvenance.SimSaid; }).Count == 0));
            lines.Add("[DeepSims Social] subjective uncertainty with an opinion survives: " + Pass(!GroundingGuard.IsSubjectiveDeflection(PartyReplyIntent.Opinion, "i don't know, maybe i'd enjoy it")));
            lines.Add("[DeepSims Social] bare casual uncertainty is allowed: " + Pass(!GroundingGuard.IsSubjectiveDeflection(PartyReplyIntent.Opinion, "i don't know")));
            lines.Add("[DeepSims Social] gathering preference is classified subjective: " + Pass(SocialClaimClassifier.Classify("i like gathering more") == SocialClaimKind.SubjectiveSocial));
            lines.Add("[DeepSims Social] healing dislike is classified subjective: " + Pass(SocialClaimClassifier.Classify("healing sounds stressful lol") == SocialClaimKind.SubjectiveSocial));
            lines.Add("[DeepSims Social] all-caster hypothetical is classified subjective: " + Pass(SocialClaimClassifier.Classify("would you guys ever run an all-caster group?") == SocialClaimKind.SubjectiveSocial));
            lines.Add("[DeepSims Social] unsupported boss history remains factual: " + Pass(SocialClaimClassifier.Classify("we killed a boss yesterday") == SocialClaimKind.CheckableFactual));
            lines.Add("[DeepSims Social] unsupported item transfer remains factual: " + Pass(SocialClaimClassifier.Classify("Phanty gave me a sword") == SocialClaimKind.CheckableFactual));
            lines.Add("[DeepSims Social] unsupported current news remains factual: " + Pass(SocialClaimClassifier.Classify("NASA launched something today") == SocialClaimKind.CheckableFactual));
            lines.Add("[DeepSims Social] AI evidence boilerplate remains rejected: " + Pass(SocialClaimClassifier.Classify("Based on the available evidence, I cannot verify that") == SocialClaimKind.AiOrEngineeringLeak));
            SilenceFatigueTracker fatigue = new SilenceFatigueTracker();
            lines.Add("[DeepSims Social] first Lively silence keeps normal pressure: " + Pass(fatigue.NoteSilence(true) == SilenceFatiguePressure.Normal));
            lines.Add("[DeepSims Social] second Lively silence strongly favors a safe seed: " + Pass(fatigue.NoteSilence(true) == SilenceFatiguePressure.Strong && fatigue.SilenceAdjustment <= -20.0));
            lines.Add("[DeepSims Social] third Lively silence forces a safe seed attempt: " + Pass(fatigue.NoteSilence(true) == SilenceFatiguePressure.ForceSafeSeed));
            fatigue.Reset();
            lines.Add("[DeepSims Social] visible/player/combat reset contract is deterministic: " + Pass(fatigue.Consecutive == 0));
            List<AmbientSeedCandidate> freeSeeds = AmbientSeedProducers.BuildDowntimeCandidates(SocialContextMode.Normal, null, null, DateTime.UtcNow);
            int freeCount = freeSeeds.FindAll(delegate(AmbientSeedCandidate x) { return x != null && x.TopicKey.StartsWith("free_social_", StringComparison.Ordinal) && !x.HasFact; }).Count;
            lines.Add("[DeepSims Social] fact-free social seed pool is always available: " + Pass(freeCount == 3));
            DateTime threadStart = DateTime.UtcNow;
            SocialSessionState bounded = new SocialSessionState();
            bounded.ResetForCharacter("bounded-thread");
            bounded.RecordVisibleSim("Fiora", "maybe we should head out soon", threadStart);
            lines.Add("[DeepSims Social] visible opener creates active thread: " + Pass(bounded.HasActiveThread(threadStart.AddSeconds(1))));
            bounded.RecordVisibleSim("Phanty", "yeah, probably", threadStart.AddSeconds(35));
            lines.Add("[DeepSims Social] visible follow-up updates recent activity: " + Pass(bounded.HasActiveThread(threadStart.AddSeconds(50))));
            lines.Add("[DeepSims Social] Sim replies cannot renew autonomous hard ceiling: " + Pass(!bounded.HasActiveThread(threadStart.AddSeconds(61))));
            lines.Add("[DeepSims Social] 244-second zombie lock is impossible: " + Pass(!bounded.HasActiveThread(threadStart.AddSeconds(244))));
            SocialSessionState pending = new SocialSessionState();
            pending.ResetForCharacter("pending-thread");
            pending.BeginPlayerTurn("Player", "what do you think?", "opinion", threadStart);
            pending.NoteThreadPending(threadStart.AddSeconds(50), 3, threadStart);
            lines.Add("[DeepSims Social] legitimate pending follow-up remains active: " + Pass(pending.HasActiveThread(threadStart.AddSeconds(45))));
            pending.CloseThread("silence", threadStart.AddSeconds(46));
            lines.Add("[DeepSims Social] terminal silence releases thread immediately: " + Pass(!pending.HasActiveThread(threadStart.AddSeconds(46))));
            string[] terminalReasons = new string[] { "grounding_reject", "stale", "no_speaker", "inference_failure", "no_followup", "combat" };
            bool terminalsClose = true;
            for (int ti = 0; ti < terminalReasons.Length; ti++)
            {
                SocialSessionState terminal = new SocialSessionState(); terminal.ResetForCharacter("terminal-" + ti);
                terminal.RecordVisibleSim("Fiora", "maybe", threadStart);
                terminal.CloseThread(terminalReasons[ti], threadStart.AddSeconds(2));
                if (terminal.HasActiveThread(threadStart.AddSeconds(2))) terminalsClose = false;
            }
            lines.Add("[DeepSims Social] rejection/stale/no-speaker/failure/combat all close: " + Pass(terminalsClose));
            SocialSessionState staleThread = new SocialSessionState(); staleThread.ResetForCharacter("stale-threshold");
            staleThread.RecordVisibleSim("Fiora", "maybe", threadStart);
            lines.Add("[DeepSims Social] no visible or pending activity closes at stale threshold: " + Pass(!staleThread.HasActiveThread(threadStart.AddSeconds(41))));
            SocialSessionState playerExtension = new SocialSessionState(); playerExtension.ResetForCharacter("player-extension");
            long firstPlayerThread = playerExtension.BeginPlayerTurn("Player", "healing seems rough", "class opinion", threadStart);
            long continuedPlayerThread = playerExtension.BeginPlayerTurn("Player", "but maybe fun", "class opinion", threadStart.AddSeconds(30));
            lines.Add("[DeepSims Social] real player continuation extends relevant thread: " + Pass(firstPlayerThread == continuedPlayerThread && playerExtension.HasActiveThread(threadStart.AddSeconds(60))));
            long replacedPlayerThread = playerExtension.BeginPlayerTurn("Player", "what about weather?", "weather", threadStart.AddSeconds(61));
            lines.Add("[DeepSims Social] player topic change replaces old thread: " + Pass(replacedPlayerThread != continuedPlayerThread));
            string closeDiagnostic = playerExtension.ConsumeCloseDiagnostic();
            lines.Add("[DeepSims Social] thread close diagnostic contains no dialogue: " + Pass(closeDiagnostic.IndexOf("what about", StringComparison.OrdinalIgnoreCase) < 0 && closeDiagnostic.IndexOf("action=closed", StringComparison.OrdinalIgnoreCase) >= 0));
            SilenceFatigueTracker quietParty = new SilenceFatigueTracker();
            quietParty.NoteSilence(false); quietParty.NoteSilence(false); quietParty.NoteSilence(false);
            lines.Add("[DeepSims Social] ineligible one-Sim/Normal/Quiet contexts gain no silence pressure: " + Pass(quietParty.Consecutive == 0));
            List<SessionChatLine> recent = state.RecentChat();
            lines.Add("[DeepSims Social] Sim B receives Sim A visible history: " + Pass(recent.Count == 3 && recent[1].Speaker == "Fiora" && recent[2].Speaker == "Phanty"));
            List<ConversationLine> visibleBanter = new List<ConversationLine>();
            visibleBanter.Add(new ConversationLine("Astra", "windblade looks fun to me"));
            List<ConversationLine> banterThread;
            bool banterBuilt = ConnectedBanterThreadPolicy.TryBuildFromVisible(visibleBanter, "Astra", "windblade looks fun to me", out banterThread);
            lines.Add("[DeepSims Social] connected banter B receives exact accepted A text: " + Pass(banterBuilt && banterThread.Count == 1 && banterThread[0].Text == "windblade looks fun to me"));
            List<ConversationLine> rejectedCandidateNotVisible;
            bool rejectedSeeded = ConnectedBanterThreadPolicy.TryBuildFromVisible(visibleBanter, "Astra", "unshown rejected candidate", out rejectedCandidateNotVisible);
            lines.Add("[DeepSims Social] rejected unshown A cannot seed B: " + Pass(!rejectedSeeded));
            lines.Add("[DeepSims Social] manual connected banter never exceeds the shared response cap: " + Pass(ConnectedBanterThreadPolicy.ManualTailReplies == SimResponseDecision.MaxResponsesPerLine));
            lines.Add("[DeepSims Social] player generation preempts autonomous tail: " + Pass(ConversationTurnGuard.IsStale(4, 5)));
            long continued = state.BeginPlayerTurn("Player", "yeah, but healing looks fun too", "class role preferences", DateTime.UtcNow.AddSeconds(4));
            lines.Add("[DeepSims Social] player continues active thread: " + Pass(continued == thread));
            lines.Add("[DeepSims Social] conversation budget stops tails: " + Pass(!state.CanAddThreadReply(2)));
            for (int i = 0; i < 80; i++) state.RecordEvent("event", "event " + i, "event", SessionEventProvenance.VerifiedWorld, null, 30, DateTime.UtcNow);
            lines.Add("[DeepSims Social] event journal bounded: " + Pass(state.ReflectionDelta().Count <= 64));
            string prior = state.Summary();
            state.ApplyReflection(string.Empty, 999);
            lines.Add("[DeepSims Social] reflection failure preserves summary: " + Pass(state.Summary() == prior));
            state.ApplyReflection(new string('x', 1600), 999);
            lines.Add("[DeepSims Social] rolling summary bounded: " + Pass(state.Summary().Length <= 1200));
            lines.Add("[DeepSims Social] reflection consumes delta only: " + Pass(state.ReflectionDelta().Count == 0));
            lines.Add("[DeepSims Social] actual events produce seeds: " + Pass(state.BuildSeeds(DateTime.UtcNow).Count > 0));
            SocialSessionState witnessState = new SocialSessionState();
            witnessState.ResetForCharacter("slot-witness");
            witnessState.RecordEvent("expedition", "Reached Bonepits.", "bonepits", SessionEventProvenance.VerifiedWorld,
                new string[] { "Brinon", "Fiora", "Phanty", "Dancer" }, 60, DateTime.UtcNow);
            List<SessionConversationSeed> witnessedSeeds = witnessState.BuildSeeds(DateTime.UtcNow);
            lines.Add("[DeepSims Social] new Sim cannot claim unwitnessed session event: " + Pass(witnessedSeeds.Count == 1 && !SocialSessionState.CanSpeakFirstPerson(witnessedSeeds[0], "Astra")));
            lines.Add("[DeepSims Social] witness can use shared event seed: " + Pass(witnessedSeeds.Count == 1 && SocialSessionState.CanSpeakFirstPerson(witnessedSeeds[0], "Dancer")));
            lines.Add("[DeepSims Social] stale seeds expire: " + Pass(state.BuildSeeds(DateTime.UtcNow.AddHours(2)).Count == 0));
            state.ResetForCharacter("slot-2-b");
            lines.Add("[DeepSims Social] no cross-character session leakage: " + Pass(state.RecentChat().Count == 0 && state.ReflectionDelta().Count == 0));
            lines.Add("[DeepSims Social] SoftPersona distinct from VerifiedWorld: " + Pass(SessionEventProvenance.SoftPersona != SessionEventProvenance.VerifiedWorld));

            SemanticTurnRoute parsed;
            bool parsedOk = SemanticTurnRouter.TryParse("TurnType=DirectQuestion\nKnowledgeNeed=None\nTopic=books\nSubject=party reading\nSearchQuery=\nConfidence=0.91\nDirectAnswerRequired=true", "what are you reading?", out parsed);
            lines.Add("[DeepSims Social] structured semantic route parses: " + Pass(parsedOk && parsed.DirectAnswerRequired && parsed.KnowledgeNeed == KnowledgeNeed.None));
            List<ConversationLine> compactThread = new List<ConversationLine>();
            compactThread.Add(new ConversationLine { Speaker = "Fiora", Text = "i usually like the quieter zones" });
            compactThread.Add(new ConversationLine { Speaker = "Player", Text = "what do you like about them?" });
            List<ChatMessage> compact = PromptBuilder.BuildCompactDirectPartyReply(
                new SimSnapshot { Name = "Phanty", ClassName = "Druid", Personality = "thoughtful" }, null,
                new WorldSnapshot { Scene = "Hidden Hills" }, compactThread, null, social, new string('s', 1200));
            bool currentPreserved = false;
            for (int i = 0; i < compact.Count; i++)
                if (compact[i] != null && compact[i].content != null && compact[i].content.IndexOf("what do you like about them?", StringComparison.Ordinal) >= 0) currentPreserved = true;
            lines.Add("[DeepSims Social] compact direct prompt fits numCtx=2048: " + Pass(PromptBuilder.EstimateTokenCount(compact) < 1500));
            lines.Add("[DeepSims Social] compact prompt preserves newest player turn: " + Pass(currentPreserved));
            GroupMessageQueue queue = new GroupMessageQueue();
            ConnectedBanterPlan plan = new ConnectedBanterPlan { RemainingReplies = 1, TopicKey = "class_opinion", ManualThread = true };
            queue.Enqueue(DateTime.UtcNow.AddSeconds(-1), "Astra", "visible opener", true, 5, "manual_banter", 1, 1, "sim:astra", "manual_banter", DateTime.UtcNow, 2, "class_opinion", plan);
            List<ScheduledGroupMessage> scheduled = queue.TakeDue(DateTime.UtcNow);
            lines.Add("[DeepSims Social] queue preserves visible-only continuation metadata: " + Pass(scheduled.Count == 1 && scheduled[0].ConnectedBanter == plan && scheduled[0].SoftPreferenceTopicKey == "class_opinion"));
            return lines;
        }

        private static string Pass(bool value) { return value ? "PASS" : "FAIL"; }
    }
}
