using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ErenshorDeepSims
{
    // Pure style fingerprint derived only from Erenshor's native dialogue examples. Keeping this
    // outside Unity/reflection code makes the personality contract deterministic and testable.
    internal static class NativeDialogueStyle
    {
        internal static List<string> ObservedTextExpressions(SimSnapshot sim)
        {
            List<string> result = new List<string>();
            if (sim == null || sim.DialogueExamples == null) return result;
            string[] candidates = new string[] { ":P", ":D", ":)", ";)", "XD", "o7", "lol", "lmao", "haha", "heh" };
            for (int c = 0; c < candidates.Length; c++)
            {
                for (int i = 0; i < sim.DialogueExamples.Count; i++)
                {
                    string line = sim.DialogueExamples[i] ?? string.Empty;
                    if (line.IndexOf(candidates[c], StringComparison.OrdinalIgnoreCase) < 0) continue;
                    result.Add(candidates[c]);
                    break;
                }
            }
            return result;
        }

        internal static bool UsesJoinedBangGreeting(SimSnapshot sim)
        {
            if (sim == null || sim.DialogueExamples == null) return false;
            for (int i = 0; i < sim.DialogueExamples.Count; i++)
                if (System.Text.RegularExpressions.Regex.IsMatch(sim.DialogueExamples[i] ?? string.Empty,
                    @"^\s*(?:hi|hey|hello)!\s*(?:NN|PLAYER)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
            return false;
        }

        internal static string ApplyGreetingShape(SimSnapshot sim, string text)
        {
            if (!UsesJoinedBangGreeting(sim) || string.IsNullOrWhiteSpace(text)) return text;
            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(text,
                @"^\s*(hi|hey|hello)[!,]?\s+(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success) return text;
            string greeting = match.Groups[1].Value;
            if (string.Equals(greeting, "hi", StringComparison.OrdinalIgnoreCase)) greeting = "Hi";
            else if (string.Equals(greeting, "hey", StringComparison.OrdinalIgnoreCase)) greeting = "Hey";
            else greeting = "Hello";
            return greeting + "!" + match.Groups[2].Value.TrimStart();
        }

        internal static string Describe(SimSnapshot sim)
        {
            if (sim == null) return "No native dialogue fingerprint was available.";
            List<string> traits = new List<string>();
            List<string> markers = ObservedTextExpressions(sim);
            if (markers.Count > 0) traits.Add("observed text expressions: " + string.Join(", ", markers.ToArray()) + " (use only these, and only sometimes)");
            if (UsesJoinedBangGreeting(sim)) traits.Add("observed greeting shape: Hi!PLAYER with no space after !; this applies only to an actual greeting");
            if (sim.TypesInAllCaps) traits.Add("authoritative casing: all caps");
            else if (sim.TypesInAllLowers) traits.Add("authoritative casing: lowercase");
            else traits.Add("preserve the mixed casing seen in the examples");
            return traits.Count == 0 ? "No strong native quirks were observed; use concise ordinary MMO chat." : string.Join("; ", traits.ToArray()) + ".";
        }

        // A small voice contract, not a fictional biography. Mapped native personality gets a
        // repeatable social shape; unmapped Sims remain coherent instead of collecting random slang.
        internal static string DescribeVoiceContract(SimSnapshot sim)
        {
            if (sim == null) return "Be concise, coherent, and ordinary; do not invent a character backstory.";
            if (sim.Rival) return "Dryly competitive and willing to tease, but never hostile without a present reason. Prefer a pointed opinion over empty hype.";
            switch (sim.PersonalityCode)
            {
                case 0:
                case 1: return "Warm and low-drama. Be welcoming, encouraging, or casually curious; avoid mockery, abrupt topic pivots, and performative slang.";
                case 2: return "Focused and lightly competitive. Prefer practical opinions or a small challenge; do not become bossy or issue gameplay commands.";
                case 3: return "Blunt and dry. Keep the edge playful and brief; do not become cruel, insulting, or invent complaints.";
                default: return "No mapped temperament is available. Keep a neutral, coherent MMO voice and use only observed typing quirks; do not manufacture catchphrases, random greetings, or a new personality.";
            }
        }
    }

    // Narrow final quality boundary: catch party-name near-misses and a greeting randomly stapled
    // onto the end of an otherwise complete thought. This is not general spell checking.
    internal static class ReplyVoiceGuard
    {
        internal static bool IsAcceptable(string reply, WorldSnapshot world, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(reply)) return true;
            string text = reply.Trim();
            Match greeting = Regex.Match(text, @"\b(?:hi|hey|heya|heyo|hello)\b\s*[!.?]*$", RegexOptions.IgnoreCase);
            if (greeting.Success && Regex.Matches(text.Substring(0, greeting.Index), @"[A-Za-z]{2,}").Count >= 2)
            {
                reason = "trailing_greeting_filler";
                return false;
            }
            List<string> names = new List<string>();
            if (world != null && world.Player != null && !string.IsNullOrWhiteSpace(world.Player.Name)) names.Add(world.Player.Name.Trim());
            if (world != null && world.Party != null)
                for (int i = 0; i < world.Party.Count; i++)
                    if (world.Party[i] != null && !string.IsNullOrWhiteSpace(world.Party[i].Name)) names.Add(world.Party[i].Name.Trim());
            MatchCollection words = Regex.Matches(text, @"\b[A-Za-z]{4,24}\b");
            for (int i = 0; i < words.Count; i++)
                for (int n = 0; n < names.Count; n++)
                {
                    string name = names[n];
                    if (name.Length < 4 || string.Equals(words[i].Value, name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (words[i].Value.StartsWith(name.Substring(0, 2), StringComparison.OrdinalIgnoreCase) && EditDistanceAtMostOne(words[i].Value, name))
                    {
                        reason = "near_miss_verified_name_" + name;
                        return false;
                    }
                }
            return true;
        }

        private static bool EditDistanceAtMostOne(string a, string b)
        {
            if (Math.Abs(a.Length - b.Length) > 1) return false;
            int i = 0, j = 0, edits = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[j])) { i++; j++; continue; }
                if (++edits > 1) return false;
                if (a.Length > b.Length) i++; else if (b.Length > a.Length) j++; else { i++; j++; }
            }
            return edits + (a.Length - i) + (b.Length - j) <= 1;
        }
    }

    internal enum SocialExpressionMode
    {
        Auto,
        Llm,
        Templates,
        Off
    }

    internal enum SocialActivityPreset
    {
        Quiet,
        Normal,
        Lively
    }

    internal enum SocialPriority
    {
        Low = 1,
        Medium = 2,
        High = 3
    }

    internal enum PartyReplyIntent
    {
        FactualGameQuestion,
        IdentityFact,
        VerifiedHistoryQuestion,
        Opinion,
        Preference,
        Hypothetical,
        SocialBanter
    }

    internal static class PartyReplyIntentClassifier
    {
        internal static PartyReplyIntent Classify(string message)
        {
            string m = (message ?? string.Empty).ToLowerInvariant();
            if (m.IndexOf("if i rerolled", StringComparison.Ordinal) >= 0 || m.IndexOf("if you rerolled", StringComparison.Ordinal) >= 0 ||
                m.IndexOf("would you try", StringComparison.Ordinal) >= 0 || m.IndexOf("would you play", StringComparison.Ordinal) >= 0)
                return PartyReplyIntent.Hypothetical;
            if (m.IndexOf("i think", StringComparison.Ordinal) >= 0 || m.IndexOf("imo", StringComparison.Ordinal) >= 0 ||
                m.IndexOf("better than", StringComparison.Ordinal) >= 0 || m.IndexOf("prefer", StringComparison.Ordinal) >= 0 ||
                m.IndexOf("more chill", StringComparison.Ordinal) >= 0)
                return PartyReplyIntent.Preference;
            if (m.IndexOf("last time", StringComparison.Ordinal) >= 0 || m.IndexOf("remember", StringComparison.Ordinal) >= 0 ||
                Regex.IsMatch(m, @"\b(?:did we|were we|how did we meet|how do you know me|our past|shared history)\b", RegexOptions.IgnoreCase))
                return PartyReplyIntent.VerifiedHistoryQuestion;
            if (Regex.IsMatch(m, @"\b(?:irl|your background|your past|who are you|tell me about yourself|where are you from|what do you do|what are you like|what did you do before this|what did you study|what do you study|where did you grow up|what do you do for work|what(?:'s| is) your job|your career|your order|your academy|your persona|how was work|how are things in real life|what are you doing this weekend|why are you online so late|what games do you like|are you in school|what do you do outside erenshor)\b", RegexOptions.IgnoreCase))
                return PartyReplyIntent.IdentityFact;
            // Subjective-opinion phrasing must win over the generic factual-lookup check below. A
            // question like "what do you think about being a windblade?" mentions a class name (which
            // trips KnowledgeQueryClassifier.ShouldLookup's class+question-word heuristic) but is asking
            // for an OPINION about a fact, not the fact itself; classifying it FactualGameQuestion routed
            // it into wiki-relationship grounding that an opinion can never satisfy, collapsing it into
            // the unknown-fact fallback even when the underlying identity fact was fully verified.
            if (m.IndexOf("what do you think", StringComparison.Ordinal) >= 0 ||
                m.IndexOf("do you like", StringComparison.Ordinal) >= 0 ||
                m.IndexOf("favorite", StringComparison.Ordinal) >= 0 ||
                m.IndexOf("favourite", StringComparison.Ordinal) >= 0 ||
                m.IndexOf("your opinion", StringComparison.Ordinal) >= 0)
                return PartyReplyIntent.Opinion;

            // Direct present-tense questions about the addressed Sim's own verified class are
            // identity facts, not wiki-definition questions. Keep this after explicit opinion
            // phrases so "what do you think about being a Windblade?" remains subjective.
            // The second alternative identifies "Is <SimName> a Windblade?"-style identity questions
            // about a party member. Without excluding articles/question words as the subject, "\w+"
            // also happily consumes "a" itself, so a plain definition question like "what is a
            // windblade?" ("is" + "a" + "windblade") was misclassified as IdentityFact and never
            // reached the factual/wiki branch below. Require the subject token to look like an actual
            // name/pronoun rather than an article or question word.
            if (Regex.IsMatch(m, @"\b(?:are|aren't|are not)\s+you\s+(?:really\s+|actually\s+)?(?:a|an|the)?\s*(?:arcanist|druid|paladin|reaver|stormcaller|windblade|duelist)\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(m, @"\b(?:is|isn't|is not)\s+(?!a\b|an\b|the\b|what\b|it\b|that\b|this\b)\w+\s+(?:really\s+|actually\s+)?(?:a|an|the)?\s*(?:arcanist|druid|paladin|reaver|stormcaller|windblade|duelist)\b", RegexOptions.IgnoreCase))
                return PartyReplyIntent.IdentityFact;

            if (KnowledgeQueryClassifier.ShouldLookup(message) || ExternalNewsQueryClassifier.ShouldLookup(message) ||
                m.IndexOf("latest patch", StringComparison.Ordinal) >= 0 ||
                m.IndexOf("newest patch", StringComparison.Ordinal) >= 0 ||
                m.IndexOf("patch notes", StringComparison.Ordinal) >= 0 ||
                m.IndexOf("latest update", StringComparison.Ordinal) >= 0 ||
                m.IndexOf("newest update", StringComparison.Ordinal) >= 0)
                return PartyReplyIntent.FactualGameQuestion;
            return PartyReplyIntent.SocialBanter;
        }

        internal static bool IsSubjective(PartyReplyIntent intent)
        {
            return intent == PartyReplyIntent.Opinion || intent == PartyReplyIntent.Preference ||
                intent == PartyReplyIntent.Hypothetical || intent == PartyReplyIntent.SocialBanter;
        }
    }

    // Explicit Campmaster Relax is a social context modifier, not a new scheduler.
    // The existing central SocialBudget remains authoritative; Relax only swaps in a
    // somewhat roomier profile so a short 1-3 line downtime thread does not consume
    // the entire ten-minute autonomous budget.
    internal static class SocialDowntimeContext
    {
        private static readonly object Gate = new object();
        private static bool _relaxActive;

        internal static bool RelaxActive { get { lock (Gate) { return _relaxActive; } } }
        internal static void SetRelaxActive(bool active) { lock (Gate) { _relaxActive = active; } }
    }

    internal sealed class SocialBudgetProfile
    {
        internal readonly double GlobalCooldownSeconds;
        internal readonly double SpeakerCooldownSeconds;
        internal readonly double EventTypeCooldownSeconds;
        internal readonly int MessagesPerTenMinutes;
        internal readonly double OpportunityMultiplier;

        internal SocialBudgetProfile(double global, double speaker, double eventType, int messages, double multiplier)
        {
            GlobalCooldownSeconds = global;
            SpeakerCooldownSeconds = speaker;
            EventTypeCooldownSeconds = eventType;
            MessagesPerTenMinutes = messages;
            OpportunityMultiplier = multiplier;
        }
    }

    internal sealed class SocialBudget
    {
        private const double PlayerQuietSeconds = 20.0;
        private const double ConversationThreadSeconds = 14.0;
        private const double ClaimedMomentSeconds = 4.0;
        private const double SemanticDuplicateSeconds = 300.0;
        private const double MessageDuplicateSeconds = 240.0;
        private const double IdeaDuplicateSeconds = 90.0;
        private readonly object _lock = new object();

        private readonly Dictionary<string, DateTime> _lastTypeUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _lastSpeakerUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _semanticUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _messageUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _ideaUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly List<DateTime> _messageTimes = new List<DateTime>();

        private DateTime _lastAutonomousUtc = DateTime.MinValue;
        private DateTime _lastPlayerSpeechUtc = DateTime.MinValue;
        private DateTime _conversationActiveUntilUtc = DateTime.MinValue;
        private DateTime _momentClaimedUntilUtc = DateTime.MinValue;
        private string _momentType = string.Empty;
        private SocialPriority _momentPriority = SocialPriority.Low;
        private SocialActivityPreset _preset = SocialActivityPreset.Normal;

        internal SocialActivityPreset Preset { get { lock (_lock) { return _preset; } } }
        internal void SetPreset(SocialActivityPreset preset) { lock (_lock) { _preset = preset; } }

        internal SocialBudgetProfile Profile
        {
            get
            {
                bool relax = SocialDowntimeContext.RelaxActive;
                switch (_preset)
                {
                    case SocialActivityPreset.Quiet:
                        return relax
                            ? new SocialBudgetProfile(65.0, 90.0, 110.0, 4, 0.70)
                            : new SocialBudgetProfile(55.0, 90.0, 150.0, 2, 0.55);
                    case SocialActivityPreset.Lively:
                        return relax
                            ? new SocialBudgetProfile(18.0, 28.0, 35.0, 20, 1.35)
                            : new SocialBudgetProfile(18.0, 30.0, 60.0, 5, 1.25);
                    default:
                        return relax
                            ? new SocialBudgetProfile(30.0, 45.0, 55.0, 12, 1.15)
                            : new SocialBudgetProfile(30.0, 45.0, 90.0, 3, 1.0);
                }
            }
        }

        internal void NotePlayerSpeech(DateTime now)
        {
            lock (_lock)
            {
                _lastPlayerSpeechUtc = now;
                _conversationActiveUntilUtc = now.AddSeconds(ConversationThreadSeconds);
            }
        }

        internal void NoteConversationActivity(DateTime now)
        {
            lock (_lock) { _conversationActiveUntilUtc = now.AddSeconds(ConversationThreadSeconds); }
        }

        internal bool IsSpeakerCoolingDown(string speaker, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(speaker)) return false;
            lock (_lock)
            {
                DateTime last;
                return _lastSpeakerUtc.TryGetValue(speaker, out last) &&
                    (now - last).TotalSeconds < Profile.SpeakerCooldownSeconds;
            }
        }

        internal double OpportunityMultiplier { get { return Profile.OpportunityMultiplier; } }

        internal bool CanAdmitOpportunity(string type, SocialPriority priority, string semanticKey,
            DateTime now, bool inOrRecentCombat, bool socialAuthority, out string reason)
        {
            lock (_lock)
            {
                reason = string.Empty;
                PruneLocked(now);
                if (!socialAuthority) { reason = "blocked because not social authority"; return false; }
                if (inOrRecentCombat && priority != SocialPriority.High) { reason = "combat/recent combat"; return false; }
                if (_lastPlayerSpeechUtc != DateTime.MinValue && (now - _lastPlayerSpeechUtc).TotalSeconds < PlayerQuietSeconds)
                { reason = "player recently spoke"; return false; }

                bool continuation = !string.IsNullOrWhiteSpace(type) && type.IndexOf("continuation", StringComparison.OrdinalIgnoreCase) >= 0;
                if (now < _conversationActiveUntilUtc && priority != SocialPriority.High && !continuation)
                { reason = "current conversation thread"; return false; }
                if (now < _momentClaimedUntilUtc)
                {
                    reason = priority > _momentPriority ? "social moment already emitted; higher priority arrived too late" : "another event already won this social moment";
                    return false;
                }

                SocialBudgetProfile profile = Profile;
                if (_lastAutonomousUtc != DateTime.MinValue && (now - _lastAutonomousUtc).TotalSeconds < profile.GlobalCooldownSeconds)
                { reason = "global cooldown"; return false; }

                string category = Normalize(type);
                DateTime lastType;
                if (category.Length > 0 && _lastTypeUtc.TryGetValue(category, out lastType) && (now - lastType).TotalSeconds < profile.EventTypeCooldownSeconds)
                { reason = "event-type cooldown"; return false; }
                if (_messageTimes.Count >= profile.MessagesPerTenMinutes) { reason = "rolling message budget"; return false; }

                string semantic = NormalizeSemantic(semanticKey);
                DateTime prior;
                if (semantic.Length > 0 && _semanticUtc.TryGetValue(semantic, out prior) && (now - prior).TotalSeconds < SemanticDuplicateSeconds)
                { reason = "recent semantic duplication"; return false; }
                return true;
            }
        }

        internal void CommitOpportunity(string type, SocialPriority priority, string semanticKey, DateTime now)
        {
            lock (_lock)
            {
                _lastAutonomousUtc = now;
                string category = Normalize(type);
                if (category.Length > 0) _lastTypeUtc[category] = now;
                string semantic = NormalizeSemantic(semanticKey);
                if (semantic.Length > 0) _semanticUtc[semantic] = now;
                _momentClaimedUntilUtc = now.AddSeconds(ClaimedMomentSeconds);
                _momentType = category;
                _momentPriority = priority;
                PruneLocked(now);
            }
        }

        internal bool CanEmitMessage(string speaker, string message, DateTime now, out string reason)
        {
            lock (_lock)
            {
                reason = string.Empty;
                PruneLocked(now);
                if (string.IsNullOrWhiteSpace(speaker) || string.IsNullOrWhiteSpace(message)) { reason = "missing speaker/message"; return false; }
                DateTime lastSpeak;
                if (_lastSpeakerUtc.TryGetValue(speaker, out lastSpeak) && (now - lastSpeak).TotalSeconds < Profile.SpeakerCooldownSeconds)
                { reason = "per-Sim cooldown"; return false; }
                string normalized = NormalizeMessage(message);
                DateTime prior;
                if (normalized.Length > 0 && _messageUtc.TryGetValue(normalized, out prior) && (now - prior).TotalSeconds < MessageDuplicateSeconds)
                { reason = "recent message duplication"; return false; }
                string idea = NormalizeIdea(message);
                if (idea.Length > 0 && _ideaUtc.TryGetValue(idea, out prior) && (now - prior).TotalSeconds < IdeaDuplicateSeconds)
                { reason = "recent idea duplication"; return false; }
                _lastSpeakerUtc[speaker] = now;
                if (normalized.Length > 0) _messageUtc[normalized] = now;
                if (idea.Length > 0) _ideaUtc[idea] = now;
                _messageTimes.Add(now);
                return true;
            }
        }

        internal string Describe(DateTime now)
        {
            lock (_lock)
            {
                PruneLocked(now);
                SocialBudgetProfile profile = Profile;
                double globalRemaining = _lastAutonomousUtc == DateTime.MinValue ? 0.0 : Math.Max(0.0, profile.GlobalCooldownSeconds - (now - _lastAutonomousUtc).TotalSeconds);
                return "preset=" + _preset + (SocialDowntimeContext.RelaxActive ? "/Relax" : string.Empty) +
                    ", global cooldown remaining=" + Math.Ceiling(globalRemaining) + "s" +
                    ", rolling messages=" + _messageTimes.Count + "/" + profile.MessagesPerTenMinutes +
                    (now < _momentClaimedUntilUtc ? ", moment=" + _momentType : string.Empty);
            }
        }

        private void PruneLocked(DateTime now)
        {
            for (int i = _messageTimes.Count - 1; i >= 0; i--)
                if ((now - _messageTimes[i]).TotalMinutes > 10.0) _messageTimes.RemoveAt(i);
            PruneMap(_semanticUtc, now, SemanticDuplicateSeconds);
            PruneMap(_messageUtc, now, MessageDuplicateSeconds);
            PruneMap(_ideaUtc, now, IdeaDuplicateSeconds);
        }

        private static void PruneMap(Dictionary<string, DateTime> map, DateTime now, double seconds)
        {
            if (map.Count <= 24) return;
            List<string> remove = new List<string>();
            foreach (KeyValuePair<string, DateTime> pair in map)
                if ((now - pair.Value).TotalSeconds > seconds) remove.Add(pair.Key);
            for (int i = 0; i < remove.Count; i++) map.Remove(remove[i]);
        }

        private static string Normalize(string value) { return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant(); }

        internal static string NormalizeSemantic(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string text = value.Trim().ToLowerInvariant();
            StringBuilder sb = new StringBuilder(text.Length);
            bool space = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsLetterOrDigit(c)) { sb.Append(c); space = false; }
                else if (!space) { sb.Append(' '); space = true; }
            }
            return sb.ToString().Trim();
        }

        internal static string NormalizeMessage(string value)
        {
            string normalized = NormalizeSemantic(value);
            if (normalized.Length == 0) return normalized;
            string[] tokens = normalized.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string t = tokens[i];
                if (t == "congrats" || t == "congratulations" || t == "gz") tokens[i] = "grats";
                else if (t == "goodgame") tokens[i] = "gg";
                else if (t == "welcome" || t == "welcome-back") tokens[i] = "wb";
                else if (t == "great" || t == "sweet") tokens[i] = "nice";
            }
            return string.Join(" ", tokens);
        }

        // Conservative concept buckets for a few high-frequency MMO-chat ideas. Returning empty is
        // intentional: ordinary agreement and distinct opinions must not be over-filtered.
        internal static string NormalizeIdea(string value)
        {
            string text = NormalizeSemantic(value);
            if (text.Length == 0) return string.Empty;
            if (Regex.IsMatch(text, @"\b(?:keep moving|keep going|move on|onward|lets go|let us go|press on)\b")) return "continue_moving";
            if (Regex.IsMatch(text, @"\b(?:ready when you are|ready to go|all good here|im ready|i am ready|rdy)\b")) return "readiness";
            if (Regex.IsMatch(text, @"\b(?:got a little close|too close|that was close|that got close|little too close)\b")) return "close_call_reaction";
            if (Regex.IsMatch(text, @"^(?:gg|nice one|good job|well done|clean enough|nice)$")) return "generic_praise";
            if (Regex.IsMatch(text, @"\b(?:nothing happening|nothing going on|not much happening|just waiting|standing around)\b")) return "idle_waiting";
            return string.Empty;
        }
    }

    internal static class SocialPolicy
    {
        internal static SocialExpressionMode ParseMode(string value)
        {
            if (string.Equals(value, "llm", StringComparison.OrdinalIgnoreCase)) return SocialExpressionMode.Llm;
            if (string.Equals(value, "templates", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "template", StringComparison.OrdinalIgnoreCase)) return SocialExpressionMode.Templates;
            if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase)) return SocialExpressionMode.Off;
            return SocialExpressionMode.Auto;
        }

        internal static SocialActivityPreset ParsePreset(string value)
        {
            if (string.Equals(value, "quiet", StringComparison.OrdinalIgnoreCase)) return SocialActivityPreset.Quiet;
            if (string.Equals(value, "lively", StringComparison.OrdinalIgnoreCase)) return SocialActivityPreset.Lively;
            return SocialActivityPreset.Normal;
        }

        internal static double ScaleAmbientSeconds(SocialActivityPreset preset, double configuredSeconds)
        {
            double baseline = Math.Max(1.0, configuredSeconds);
            if (preset == SocialActivityPreset.Lively) return baseline * 0.55;
            if (preset == SocialActivityPreset.Quiet) return baseline * 1.40;
            return baseline;
        }

        internal static SocialPriority PriorityOf(string type, int importance)
        {
            string t = string.IsNullOrWhiteSpace(type) ? string.Empty : type.Trim().ToLowerInvariant();
            if (t == "player_death" || t == "sim_death" || t == "player_level_up" || t == "sim_level_up" ||
                t == "friendly_duel" || t == "encounter_complete" || importance >= 75) return SocialPriority.High;
            if (t == "party_join" || t == "party_leave" || t == "quest_complete" || t == "travel_arrival" ||
                t == "ready_check" || t == "reunion" || t == "expedition_arrived" || t == "expedition_combat_interrupted" ||
                t == "expedition_failed" || importance >= 40) return SocialPriority.Medium;
            return SocialPriority.Low;
        }

        internal static bool IsTrivialRitualEvent(string type)
        {
            string t = string.IsNullOrWhiteSpace(type) ? string.Empty : type.Trim().ToLowerInvariant();
            return t == "player_level_up" || t == "sim_level_up" || t == "player_death" || t == "sim_death" ||
                t == "player_revive" || t == "party_join" || t == "party_leave" || t == "reunion" || t == "friendly_duel" ||
                t == "ready_check" || t == "expedition_arrived";
        }

        internal static bool IsRitualPlayerMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string m = text.Trim().ToLowerInvariant().Trim('.', '!', '?', ' ');
            string[] values = new string[] { "ding", "grats", "gz", "congrats", "gg", "inc", "incoming", "ready", "brb", "wb", "nice", "ouch", "rip", "lol", "lmao", "ty", "thanks" };
            for (int i = 0; i < values.Length; i++) if (m == values[i]) return true;
            return false;
        }

        internal static SocialExpressionMode ResolveAutonomousMode(string configuredMode, bool ollamaHealthy, string eventType)
        {
            SocialExpressionMode mode = ParseMode(configuredMode);
            if (mode == SocialExpressionMode.Off || mode == SocialExpressionMode.Templates || mode == SocialExpressionMode.Llm) return mode;
            if (!ollamaHealthy || IsTrivialRitualEvent(eventType)) return SocialExpressionMode.Templates;
            return SocialExpressionMode.Llm;
        }
    }

    internal static class PersonalitySpeechPolicy
    {
        // Desire to volunteer is separate from topic relevance and final speaker selection. Only
        // verified native personality/typing fields nudge this bounded probability; class never does.
        internal static double DesireProbability(SimSnapshot sim, SocialActivityPreset preset, bool meaningfulEvent)
        {
            if (sim == null) return 0.0;
            double chance = meaningfulEvent ? 0.88 : 0.72;
            if (preset == SocialActivityPreset.Lively) chance += 0.08;
            else if (preset == SocialActivityPreset.Quiet) chance -= 0.10;
            if (sim.Rival) chance += 0.05;
            if (sim.Abbreviates) chance += 0.03;
            if (sim.LovesEmojis) chance += 0.02;
            if (sim.Patience >= 70) chance -= 0.05;
            else if (sim.Patience > 0 && sim.Patience <= 30) chance += 0.04;
            if (sim.PersonalityCode >= 0) chance += ((sim.PersonalityCode % 5) - 2) * 0.015;
            return Math.Max(meaningfulEvent ? 0.75 : 0.52, Math.Min(0.98, chance));
        }
    }

    internal sealed class AdaptiveActivityDecision
    {
        internal SocialActivityPreset Preset;
        internal double Score;
        internal string Reason = string.Empty;
    }

    // Chooses a temporary party-level social mood. Personality and verified context contribute
    // points, while a small random term prevents the same roster from being permanently locked to
    // one cadence. This affects willingness/frequency only; it never creates dialogue facts.
    internal static class AdaptiveActivityPolicy
    {
        internal static AdaptiveActivityDecision Decide(IList<SimSnapshot> party, SocialContextMode context,
            bool knownTown, double randomRoll)
        {
            double personality = 0.0;
            int count = 0;
            for (int i = 0; party != null && i < party.Count; i++)
            {
                SimSnapshot sim = party[i];
                if (sim == null) continue;
                count++;
                if (sim.Patience > 0 && sim.Patience <= 30) personality += 2.0;
                else if (sim.Patience >= 70) personality -= 2.0;
                if (sim.Rival) personality += 1.0;
                if (sim.Abbreviates) personality += 0.75;
                if (sim.LovesEmojis) personality += 0.50;
                if (sim.PersonalityCode >= 0) personality += ((sim.PersonalityCode % 5) - 2) * 0.35;
            }

            // Average personality keeps a five-Sim party from becoming Lively solely because it is
            // larger, then party size adds a much smaller and intuitive "more people" nudge.
            if (count > 0) personality /= count;
            double partySize = Math.Max(0, count - 1) * 0.35;
            double contextPoints = 0.0;
            if (knownTown) contextPoints += 3.0;
            if (context == SocialContextMode.SoftDowntime) contextPoints += 2.5;
            else if (context == SocialContextMode.Camp) contextPoints += 3.5;
            else if (context == SocialContextMode.Relax) contextPoints += 4.5;

            double roll = Math.Max(0.0, Math.Min(1.0, randomRoll));
            double randomPoints = (roll - 0.5) * 4.0; // -2 through +2
            double score = personality + partySize + contextPoints + randomPoints;
            SocialActivityPreset preset = score >= 3.0 ? SocialActivityPreset.Lively :
                (score <= -1.25 ? SocialActivityPreset.Quiet : SocialActivityPreset.Normal);
            return new AdaptiveActivityDecision
            {
                Preset = preset,
                Score = score,
                Reason = "personality=" + Math.Round(personality, 1) +
                    ", party=" + Math.Round(partySize, 1) +
                    ", context=" + Math.Round(contextPoints, 1) +
                    ", random=" + Math.Round(randomPoints, 1) +
                    (knownTown ? ", town" : string.Empty)
            };
        }

        internal static bool IsAdaptive(string configured)
        {
            return string.Equals(configured == null ? string.Empty : configured.Trim(), "adaptive", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsConfiguredTown(string scene, string configuredCsv)
        {
            string current = NormalizeZone(scene);
            if (current.Length == 0) return false;
            string[] values = (configuredCsv ?? string.Empty).Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < values.Length; i++)
                if (string.Equals(current, NormalizeZone(values[i]), StringComparison.Ordinal)) return true;
            return false;
        }

        private static string NormalizeZone(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            StringBuilder sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++) if (char.IsLetterOrDigit(value[i])) sb.Append(char.ToLowerInvariant(value[i]));
            return sb.ToString();
        }
    }

    // A reconnect, slot reshuffle, or earlier chat exchange is not proof of a shared adventure.
    // Reunion speech is unlocked only by the completed-outing counter, which MemoryStore advances
    // from verified outing telemetry. The returning Sim is the sole eligible first speaker so the
    // event cannot produce the awkward "welcome back, me" perspective.
    internal static class ReunionPolicy
    {
        internal static bool TryBuildCandidate(SimSnapshot returning, SimMemory memory, DateTime now,
            out SocialEventCandidate candidate)
        {
            candidate = null;
            if (returning == null || string.IsNullOrWhiteSpace(returning.Name) || memory == null) return false;
            if (memory.CompletedOutings <= 0) return false;

            int outings = Math.Max(1, memory.CompletedOutings);
            string context = returning.Name + " just rejoined the current party. VERIFIED SHARED HISTORY: " +
                returning.Name + " and the player completed " + outings + " prior " +
                (outings == 1 ? "outing" : "outings") + " together. No specific event, elapsed time, or future plan is implied.";
            candidate = new SocialEventCandidate("reunion", now,
                new string[] { returning.Name }, new string[] { returning.Name },
                new string[] { returning.Name }, SocialEventTrust.Experienced,
                65, 1.0, "reunion", context, 1.0);
            return true;
        }
    }

    // Pure policy for Follow's structured Expedition lifecycle. Structural transitions are verified
    // facts but usually not social moments. Arrival is meaningful because the verified outing objective
    // completed; combat interruption may be meaningful but remains budget/combat gated.
    internal static class ExpeditionSocialPolicy
    {
        internal static bool IsExpeditionType(string type)
        {
            return Normalize(type).StartsWith("expedition_", StringComparison.Ordinal);
        }

        internal static bool ShouldCreateCandidate(string type)
        {
            string t = Normalize(type);
            return t == "expedition_arrived" || t == "expedition_combat_interrupted" || t == "expedition_failed";
        }

        internal static bool ShouldPersistSocialMemory(string type)
        {
            string t = Normalize(type);
            return t == "expedition_arrived" || t == "expedition_combat_interrupted" || t == "expedition_failed";
        }

        internal static int NormalizeImportance(string type, int incoming)
        {
            string t = Normalize(type);
            if (t == "expedition_arrived") return Math.Max(60, incoming);
            if (t == "expedition_combat_interrupted") return Math.Max(45, incoming);
            if (t == "expedition_failed") return Math.Max(45, incoming);
            return incoming;
        }

        // Arrival closes out a verified outing objective, so it is nudged toward a somewhat higher
        // chance of a single line; a structural failure gets only a bounded "possibly one reaction"
        // chance, and only ever reacts to the verified failure reason supplied by the caller - it must
        // never invent a cause.
        internal static double NormalizeChance(string type, double incoming)
        {
            string t = Normalize(type);
            if (t == "expedition_arrived") return Math.Max(0.65, Math.Min(0.85, incoming));
            if (t == "expedition_combat_interrupted") return Math.Max(0.20, Math.Min(0.45, incoming));
            if (t == "expedition_failed") return Math.Max(0.25, Math.Min(0.45, incoming));
            return incoming;
        }

        internal static double DuplicateWindowSeconds(string type)
        {
            switch (Normalize(type))
            {
                case "expedition_resumed": return 120.0;
                case "expedition_started":
                case "expedition_departed":
                case "expedition_returning": return 60.0;
                case "expedition_zone_entered": return 45.0;
                case "expedition_combat_interrupted": return 20.0;
                case "expedition_failed": return 30.0;
                case "expedition_arrived": return 12.0;
                default: return 0.0;
            }
        }

        internal static string SemanticFingerprint(string type, string description)
        {
            string t = Normalize(type);
            if (t == "expedition_resumed" || t == "expedition_started" || t == "expedition_departed" || t == "expedition_returning") return t;
            return t + "|" + SocialBudget.NormalizeSemantic(description ?? string.Empty);
        }

        private static string Normalize(string type) { return string.IsNullOrWhiteSpace(type) ? string.Empty : type.Trim().ToLowerInvariant(); }
    }

    // Small deterministic boundary deduper for external structured lifecycle events. It is not a
    // general social cooldown; only event kinds with an explicit policy window are deduplicated here.
    internal sealed class SemanticEventDeduplicator
    {
        private readonly Dictionary<string, DateTime> _lastUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        internal bool ShouldAccept(string type, string description, DateTime now)
        {
            double window = ExpeditionSocialPolicy.DuplicateWindowSeconds(type);
            if (window <= 0.0) return true;
            string key = ExpeditionSocialPolicy.SemanticFingerprint(type, description);
            DateTime prior;
            if (_lastUtc.TryGetValue(key, out prior) && (now - prior).TotalSeconds < window) return false;
            _lastUtc[key] = now;
            Prune(now);
            return true;
        }

        private void Prune(DateTime now)
        {
            if (_lastUtc.Count <= 24) return;
            List<string> remove = new List<string>();
            foreach (KeyValuePair<string, DateTime> pair in _lastUtc)
                if ((now - pair.Value).TotalMinutes > 5.0) remove.Add(pair.Key);
            for (int i = 0; i < remove.Count; i++) _lastUtc.Remove(remove[i]);
        }
    }

    internal static class CampSemanticAuthority
    {
        internal static bool ShouldEmitLegacyCampStart(bool campmasterHealthy, bool huntCampActive)
        {
            // Once the versioned API is healthy, Campmaster owns automatic Hunt Camp semantics.
            // huntCampActive remains part of the signature so tests can express the equivalent-start
            // case, but inactive Campmaster state is still authoritative (no legacy sitting camp).
            return !campmasterHealthy;
        }

        internal static string CanonicalCampStartType(bool campmasterHealthy)
        {
            return campmasterHealthy ? "hunt_camp_start" : "camp_start";
        }
    }

    internal static class SocialTemplates
    {
        // Fact-free renderings for ambient seeds. These preserve the selector's chosen subject when
        // Templates mode is active or Ollama is unavailable instead of collapsing every carefully
        // selected seed into "ready when you are". Memory, callback, player-topic, and verified-fact
        // seeds deliberately return false: paraphrasing those safely requires the grounded LLM path.
        internal static bool TryRenderAmbientSeed(string topicKey, string verifiedFact, long opportunityId,
            SimSnapshot speaker, out string message)
        {
            message = string.Empty;
            if (speaker == null || string.IsNullOrWhiteSpace(topicKey)) return false;
            string topic = topicKey.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(verifiedFact) || topic.StartsWith("memory:", StringComparison.Ordinal) ||
                topic.StartsWith("outing:", StringComparison.Ordinal) || topic.StartsWith("callback_", StringComparison.Ordinal) ||
                topic.StartsWith("player_topic:", StringComparison.Ordinal)) return false;

            int seed = StableHash(topic + "|" + speaker.Name + "|" + opportunityId);
            if (topic == "zone_preference" || topic == "zone_atmosphere")
                message = Pick(seed, new string[] { "gloomy zones have the best vibe", "i like the weirder-looking zones", "best zone vibe?" });
            else if (topic == "class_opinion" || topic == "class_role_preferences")
                message = Pick(seed, new string[] { "healing looks fun, honestly", "tanking or healing?", "what class would you reroll?" });
            else if (topic == "future_activity" || topic == "adventure_preferences")
                message = Pick(seed, new string[] { "dungeons over grinding for me", "exploring or a good camp?", "i'd pick a dungeon run" });
            else if (topic == "pace_preference" || topic == "pace_preferences")
                message = Pick(seed, new string[] { "quick pace or careful pulls?", "i like a steady pace", "chain pulls get chaotic fast lol" });
            else if (topic == "gear_aesthetics")
                message = Pick(seed, new string[] { "looks or stats?", "robes win on style", "some gear is all stats and no style" });
            else if (topic == "enemy_design")
                message = Pick(seed, new string[] { "what mobs have the best design?", "some mobs are way cooler than others", "favorite enemy design?" });
            else if (topic == "ordinary_downtime" || topic == "food_music")
                message = Pick(seed, new string[] { "what do you listen to while grinding?", "best grinding snack?", "rainy-day gaming is hard to beat" });
            else if (topic == "other_sim_preference" || topic == "party_preferences")
                message = Pick(seed, new string[] { "what class would you guys try next?", "you guys prefer dungeons or camps?", "any favorite zone vibes?" });
            else if (topic == "light_tease" || topic == "light_teasing")
                message = Pick(seed, new string[] { "look at us being responsible lol", "this is suspiciously peaceful", "nobody cause trouble for five seconds :D" });
            else return false;
            return true;
        }

        internal static bool TryRenderEvent(SocialEventCandidate candidate, SimSnapshot speaker, RelationshipTone tone, out string message)
        {
            message = string.Empty;
            if (candidate == null || speaker == null) return false;
            string type = candidate.Type == null ? string.Empty : candidate.Type.Trim().ToLowerInvariant();
            int seed = StableHash(type + "|" + speaker.Name + "|" + candidate.VerifiedContext);
            bool rivalish = speaker.Rival || (tone != null && tone.Rivalry >= 0.35f);

            if (type == "player_level_up" || type == "sim_level_up") { message = Pick(seed, rivalish ? new string[] { "grats", "nice", "ding" } : new string[] { "grats", "ding", "nice" }); return true; }
            if (type == "player_death" || type == "sim_death") { message = Pick(seed, new string[] { "rip", "ouch", "oof" }); return true; }
            if (type == "player_revive") { message = Pick(seed, new string[] { "wb", "back at it" }); return true; }
            if (type == "party_join") { message = Pick(seed, new string[] { "hey", "yo", "sup" }); return true; }
            if (type == "party_leave") { message = Pick(seed, new string[] { "later", "cya" }); return true; }
            if (type == "reunion")
            {
                bool familiar = tone != null && tone.Familiarity >= 0.32f;
                message = Pick(seed, familiar
                    ? new string[] { "hey, back at it?", "yo, good to see you", "ready for another adventure?" }
                    : new string[] { "hey, back at it?", "yo, good to see you", "hey again" });
                return true;
            }
            if (type == "quest_complete") { message = Pick(seed, new string[] { "nice", "gg" }); return true; }
            if (type == "friendly_duel") { message = Pick(seed, rivalish ? new string[] { "gg", "nice", "lol gg" } : new string[] { "gg", "nice" }); return true; }
            if (type == "ready_check") { message = Pick(seed, new string[] { "ready", "rdy" }); return true; }
            if (type == "encounter_complete")
            {
                message = EncounterReactionPolicy.Render(candidate.VerifiedContext, seed);
                return !string.IsNullOrWhiteSpace(message);
            }
            if (type == "expedition_arrived") { message = Pick(seed, new string[] { "made it", "nice", "there we go" }); return true; }
            if (type == "expedition_combat_interrupted") { message = Pick(seed, new string[] { "oof", "welp", "figures" }); return true; }
            if (type == "expedition_failed") { message = Pick(seed, new string[] { "welp", "that didn't go as planned", "rough" }); return true; }
            if (type.StartsWith("relax_topic_", StringComparison.Ordinal))
            {
                string topic = type.Substring("relax_topic_".Length);
                if (topic == "class_role_preferences") message = Pick(seed, new string[] { "what class do you guys actually enjoy playing most?", "any class you wouldn't mind trying?" });
                else if (topic == "zone_atmosphere") message = Pick(seed, new string[] { "what zone has the best vibe to you guys?", "you guys have a favorite place to hang out?" });
                else if (topic == "adventure_preferences") message = Pick(seed, new string[] { "dungeons or grinding a good camp?", "you guys prefer exploring or settling in somewhere?" });
                else if (topic == "pace_preferences") message = Pick(seed, new string[] { "you guys more into careful pulls or moving fast?", "slow and steady or chain pulls?" });
                else if (topic == "gear_aesthetics") message = Pick(seed, new string[] { "looks or stats if you had to pick?", "some gear is worth wearing just for the look" });
                else if (topic == "enemy_design") message = Pick(seed, new string[] { "what kind of mobs do you actually like fighting?", "some enemy designs are way better than others" });
                else if (topic == "food_music") message = Pick(seed, new string[] { "what do you guys listen to while grinding?", "anyone else always end up grabbing food during downtime?" });
                else if (topic == "light_teasing") message = Pick(seed, new string[] { "look at us being responsible and taking a break", "this is almost suspiciously peaceful" });
                else message = Pick(seed, new string[] { "what do you guys usually like doing when we're not fighting?", "anyone got a strong preference on what makes a good group night?" });
                return true;
            }
            if (type == "idle" || type == "camp_idle" || type == "camp_start") { message = Pick(seed, new string[] { "ready when you are", "all good here" }); return true; }
            return false;
        }

        internal static bool TryRenderPlayerRitual(string playerMessage, SimSnapshot speaker, out string message)
        {
            message = string.Empty;
            if (speaker == null || !SocialPolicy.IsRitualPlayerMessage(playerMessage)) return false;
            string m = playerMessage.Trim().ToLowerInvariant().Trim('.', '!', '?', ' ');
            int seed = StableHash(m + "|" + speaker.Name);
            if (m == "ding") message = Pick(seed, new string[] { "grats", "gz" });
            else if (m == "grats" || m == "gz" || m == "congrats") message = Pick(seed, new string[] { "ty", "thanks" });
            else if (m == "gg") message = "gg";
            else if (m == "inc" || m == "incoming") message = Pick(seed, new string[] { "ready", "got it" });
            else if (m == "ready") message = Pick(seed, new string[] { "ready", "rdy" });
            else if (m == "brb") message = Pick(seed, new string[] { "k", "np" });
            else if (m == "wb") message = Pick(seed, new string[] { "ty", "hey" });
            else if (m == "nice") message = Pick(seed, new string[] { "yeah", "nice" });
            else if (m == "ouch" || m == "rip") message = Pick(seed, new string[] { "oof", "yeah" });
            else if (m == "lol" || m == "lmao") message = Pick(seed, new string[] { "lol", "heh" });
            else if (m == "ty" || m == "thanks") message = Pick(seed, new string[] { "np", "sure" });
            return !string.IsNullOrWhiteSpace(message);
        }

        // Small deterministic continuation renderer for common social topics so the conversation
        // controller (who/when/what topic in SocialDirector/ConversationTurnGuard) can stay independent
        // of whether expression comes from the LLM or from Templates mode. Only a bounded set of common
        // party-chat exchanges are covered; anything else returns false so the caller stops the thread
        // rather than fabricating a reply.
        internal static bool TryRenderThreadReply(string latestText, SimSnapshot speaker, out string message)
        {
            message = string.Empty;
            if (speaker == null || string.IsNullOrWhiteSpace(latestText)) return false;
            string m = latestText.Trim().ToLowerInvariant();
            int seed = StableHash(m + "|" + speaker.Name);
            string cls = string.IsNullOrWhiteSpace(speaker.ClassName) ? string.Empty : speaker.ClassName.ToLowerInvariant();

            if (m.Contains("tank") && (m.Contains("hard") || m.Contains("job") || m.Contains("harder")))
            {
                message = cls == "druid" || cls == "paladin"
                    ? Pick(seed, new string[] { "depends who has to keep them alive", "someone has to keep the heals up too" })
                    : Pick(seed, new string[] { "yeah until the tank pulls half the room", "that's when it gets fun" });
                return true;
            }
            if (m.Contains("heal") && (m.Contains("hard") || m.Contains("job")))
            {
                message = Pick(seed, new string[] { "healing has its own kind of stress", "keeping everyone up isn't easy either" });
                return true;
            }
            if (m.Contains("favorite") && m.Contains("class"))
            {
                message = Pick(seed, new string[] { "druid looks fun to me", "probably stormcaller", "windblade has the best vibe", "i'd stick with " + (string.IsNullOrWhiteSpace(cls) ? "whatever feels fun" : cls) });
                return true;
            }
            if (m.EndsWith("?", StringComparison.Ordinal) && (m.Contains("agree") || m.Contains("right")))
            {
                message = Pick(seed, new string[] { "pretty much", "yeah, agreed" });
                return true;
            }
            return false;
        }

        // Last-resort expression for directly addressed subjective turns. This is intentionally
        // personality-shaped and fact-free, so a model NO_MESSAGE cannot turn a harmless opinion
        // into the generic factual uncertainty fallback.
        internal static bool TryRenderSubjectiveReply(string playerMessage, SimSnapshot speaker, PartyReplyIntent intent, out string message)
        {
            message = string.Empty;
            if (speaker == null || string.IsNullOrWhiteSpace(playerMessage) || !PartyReplyIntentClassifier.IsSubjective(intent)) return false;
            string m = playerMessage.ToLowerInvariant();
            int seed = StableHash((speaker.Name ?? string.Empty) + "|" + m + "|" + intent.ToString());
            bool playful = speaker.Rival || speaker.Patience > 0 && speaker.Patience < 35;
            if ((m.Contains("favorite") || m.Contains("favourite") || m.Contains("prefer")) &&
                (m.Contains("camp") || m.Contains("place") || m.Contains("spot")))
            {
                message = playful
                    ? Pick(seed, new string[] { "open camps, less surprise aggro lol", "somewhere with quick respawns :D" })
                    : Pick(seed, new string[] { "somewhere quiet with quick respawns :)", "a cozy camp by the water" });
                return true;
            }
            if (m.Contains("camp") && m.Contains("dungeon"))
            {
                message = playful ? Pick(seed, new string[] { "dungeons for me, less boring XD", "depends how good the camp is lol" })
                    : Pick(seed, new string[] { "camping is more chill imo", "depends how good the camp is" });
                return true;
            }
            if (intent == PartyReplyIntent.Hypothetical && (m.Contains("class") || m.Contains("reroll")))
            {
                string[] choices = new string[] { "probably druid", "maybe arcanist", "stormcaller looks fun", "honestly i'd stay " + (string.IsNullOrWhiteSpace(speaker.ClassName) ? "with this class" : speaker.ClassName.ToLowerInvariant()) };
                message = Pick(seed, choices) + (playful ? " lol" : string.Empty);
                return true;
            }
            if (m.Contains("music") || m.Contains("listen to") || m.Contains("listening to"))
            {
                message = playful
                    ? Pick(seed, new string[] { "anything loud while grinding lol", "probably metal for dungeon runs" })
                    : Pick(seed, new string[] { "usually chill instrumentals", "the game soundtrack honestly" });
                return true;
            }
            if ((m.Contains("favorite") || m.Contains("favourite")) && m.Contains("zone"))
            {
                message = playful ? Pick(seed, new string[] { "somewhere dangerous enough to stay awake lol", "give me a gloomy dungeon" })
                    : Pick(seed, new string[] { "somewhere quiet with a good view", "probably a rainy zone" });
                return true;
            }
            if ((m.Contains("sucked into") || m.Contains("stuck in") || m.Contains("live in")) &&
                (m.Contains("game") || m.Contains("erenshor")))
            {
                message = playful ? Pick(seed, new string[] { "honestly i'd never log out lol", "fun until the first corpse run :P" })
                    : Pick(seed, new string[] { "i'd probably stay honestly", "kinda terrifying but i'd try it" });
                return true;
            }
            if (intent == PartyReplyIntent.Preference || intent == PartyReplyIntent.Opinion)
            {
                message = Pick(seed, playful ? new string[] { "yeah i can see that lol", "nah, not really my thing" } : new string[] { "yeah, i can see that", "i'd lean yes honestly" });
                return true;
            }
            return false;
        }

        internal static string RenderUnknownFactReply(string playerMessage, SimSnapshot speaker)
        {
            string m = (playerMessage ?? string.Empty).ToLowerInvariant();
            int seed = StableHash((speaker == null ? string.Empty : speaker.Name ?? string.Empty) + "|unknown|" + m);
            if (m.Contains("news") || m.Contains("patch") || m.Contains("update"))
                return Pick(seed, new string[] { "haven't heard anything solid", "not sure what's new yet" });
            if (m.Contains("where") || m.Contains("location") || m.Contains("how do i get") || m.Contains("how do we get"))
                return Pick(seed, new string[] { "can't place that one", "no idea where that is" });
            if (m.Contains("drop") || m.Contains("loot") || m.Contains("item"))
                return Pick(seed, new string[] { "no clue what drops that", "i don't know that item" });
            return Pick(seed, new string[] { "beats me on that one", "i don't know that one", "not sure on that specifically" });
        }

        internal static string ApplyOccasionalMmoTexture(SimSnapshot speaker, string text, bool profileUsesTextEmotes)
        {
            if (speaker == null || string.IsNullOrWhiteSpace(text)) return text;
            string line = text.Trim();
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"(?:^|\s)(?:lol|lmao|haha|heh|o7)(?:\s|$)|[:;]-?[dDpP)]", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return line;
            if (System.Text.RegularExpressions.Regex.IsMatch(line,
                @"\b(?:news|nasa|headline|died|death|killed|failed|sorry|hurt|loot|boss|fight|combat|remember|wipe)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return line;

            List<string> observed = NativeDialogueStyle.ObservedTextExpressions(speaker);
            bool genericSlangOnly = observed.Count == 0 && !speaker.LovesEmojis;
            string[] choices = observed.Count > 0 ? observed.ToArray() :
                (speaker.LovesEmojis ? new string[] { ":D", ":P", ":)" } : new string[] { "lol" });
            int roll = Math.Abs(StableHash((speaker.Name ?? string.Empty) + "|texture|" + line) % 100);
            int chance = genericSlangOnly ? 10 : (observed.Count > 0 || profileUsesTextEmotes ? 45 : 35);
            if (roll >= chance) return line;
            string suffix = Pick(StableHash(line + "|suffix"), choices);
            return line.TrimEnd('.', ',', '!', '?', ';', ' ') + " " + suffix;
        }

        private static string Pick(int seed, string[] values)
        {
            if (values == null || values.Length == 0) return string.Empty;
            int index = seed == int.MinValue ? 0 : Math.Abs(seed) % values.Length;
            return values[index];
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                int hash = 17;
                string text = value ?? string.Empty;
                for (int i = 0; i < text.Length; i++) hash = hash * 31 + text[i];
                return hash;
            }
        }

        internal static List<string> RunSelfTests()
        {
            List<string> results = new List<string>();
            DateTime t = new DateTime(2026, 8, 9, 20, 0, 0, DateTimeKind.Utc);
            results.Add("mode/Templates never selects LLM: " + (SocialPolicy.ResolveAutonomousMode("Templates", true, "encounter_complete") == SocialExpressionMode.Templates ? "PASS" : "FAIL"));
            results.Add("mode/Auto falls back without Ollama: " + (SocialPolicy.ResolveAutonomousMode("Auto", false, "encounter_complete") == SocialExpressionMode.Templates ? "PASS" : "FAIL"));
            results.Add("mode/Auto uses templates for ritual event: " + (SocialPolicy.ResolveAutonomousMode("Auto", true, "friendly_duel") == SocialExpressionMode.Templates ? "PASS" : "FAIL"));
            results.Add("mode/Auto uses templates for reunion ritual: " + (SocialPolicy.ResolveAutonomousMode("Auto", true, "reunion") == SocialExpressionMode.Templates ? "PASS" : "FAIL"));
            results.Add("mode/Off remains off: " + (SocialPolicy.ResolveAutonomousMode("Off", true, "friendly_duel") == SocialExpressionMode.Off ? "PASS" : "FAIL"));

            SocialBudget quiet = new SocialBudget(); quiet.SetPreset(SocialActivityPreset.Quiet);
            SocialBudget normal = new SocialBudget(); normal.SetPreset(SocialActivityPreset.Normal);
            SocialBudget lively = new SocialBudget(); lively.SetPreset(SocialActivityPreset.Lively);
            bool ordered = quiet.Profile.MessagesPerTenMinutes < normal.Profile.MessagesPerTenMinutes && normal.Profile.MessagesPerTenMinutes < lively.Profile.MessagesPerTenMinutes && quiet.Profile.OpportunityMultiplier < normal.Profile.OpportunityMultiplier && normal.Profile.OpportunityMultiplier < lively.Profile.OpportunityMultiplier;
            results.Add("budget/Quiet < Normal < Lively: " + (ordered ? "PASS" : "FAIL"));

            string reason;
            bool first = normal.CanAdmitOpportunity("player_level_up", SocialPriority.High, "level|12", t, false, true, out reason);
            if (first) normal.CommitOpportunity("player_level_up", SocialPriority.High, "level|12", t);
            bool second = normal.CanAdmitOpportunity("party_join", SocialPriority.Medium, "join|A", t.AddSeconds(1), false, true, out reason);
            results.Add("budget/one social moment wins: " + (first && !second ? "PASS" : "FAIL"));

            SocialBudget speakerBudget = new SocialBudget(); speakerBudget.SetPreset(SocialActivityPreset.Normal);
            bool msg1 = speakerBudget.CanEmitMessage("Phanty", "gg", t, out reason);
            bool msg2 = speakerBudget.CanEmitMessage("Phanty", "nice", t.AddSeconds(1), out reason);
            results.Add("budget/per-Sim cooldown: " + (msg1 && !msg2 && reason == "per-Sim cooldown" ? "PASS" : "FAIL"));

            SocialBudget dupBudget = new SocialBudget();
            bool dup1 = dupBudget.CanEmitMessage("A", "congrats!", t, out reason);
            bool dup2 = dupBudget.CanEmitMessage("B", "grats", t.AddSeconds(1), out reason);
            results.Add("budget/recent duplicate templates suppressed: " + (dup1 && !dup2 ? "PASS" : "FAIL"));

            SocialBudget ideaBudget = new SocialBudget();
            bool idea1 = ideaBudget.CanEmitMessage("A", "let's keep moving", t, out reason);
            bool idea2 = ideaBudget.CanEmitMessage("B", "yeah, onward", t.AddSeconds(1), out reason);
            bool agreement = ideaBudget.CanEmitMessage("C", "yeah, i agree", t.AddSeconds(2), out reason);
            results.Add("budget/idea-level duplicate suppressed without filtering agreement: " +
                (idea1 && !idea2 && agreement ? "PASS" : "FAIL"));

            SocialBudget authorityBudget = new SocialBudget();
            bool authority = authorityBudget.CanAdmitOpportunity("idle", SocialPriority.Low, "idle", t, false, false, out reason);
            results.Add("budget/non-authority cannot autonomously emit: " + (!authority && reason == "blocked because not social authority" ? "PASS" : "FAIL"));

            SocialEventCandidate duel = new SocialEventCandidate("friendly_duel", t,
                new string[] { "Player", "Duelist" }, new string[] { "Spectator" }, new string[] { "Player", "Duelist" },
                SocialEventTrust.Experienced, 80, 1.0, "duel", "Verified friendly duel completed: Player defeated Duelist.", 0.55);
            string line;
            bool rendered = SocialTemplates.TryRenderEvent(duel, new SimSnapshot { Name = "Spectator", Rival = true }, RelationshipModel.Describe(0.5f, 0.3f, 0.6f), out line);
            bool noInventedResult = rendered && line.IndexOf("defeat", StringComparison.OrdinalIgnoreCase) < 0 && line.IndexOf("won", StringComparison.OrdinalIgnoreCase) < 0 && line.IndexOf("closer", StringComparison.OrdinalIgnoreCase) < 0 && line.IndexOf("10g", StringComparison.OrdinalIgnoreCase) < 0;
            results.Add("duel/template cannot invent result/comparison/bet: " + (noInventedResult ? "PASS" : "FAIL"));
            results.Add("duel/absent Sim cannot spectate: " + (!EventConversationDirector.IsEligibleSpeaker(duel, "Absent") ? "PASS" : "FAIL"));

            string neutralLine; string rivalLine;
            SocialTemplates.TryRenderEvent(duel, new SimSnapshot { Name = "Spectator", Rival = false }, RelationshipModel.Describe(0f, 0f, 0f), out neutralLine);
            SocialTemplates.TryRenderEvent(duel, new SimSnapshot { Name = "Spectator", Rival = true }, RelationshipModel.Describe(0f, 0f, 0.8f), out rivalLine);
            bool toneSafe = !string.IsNullOrWhiteSpace(neutralLine) && !string.IsNullOrWhiteSpace(rivalLine);
            results.Add("duel/relationship-personality changes selection only, not facts: " + (toneSafe ? "PASS" : "FAIL"));

            SimMemory reconnectOnly = new SimMemory { GroupSessions = 9, CompletedOutings = 0 };
            reconnectOnly.Normalize();
            SocialEventCandidate reunion;
            bool falseReunion = ReunionPolicy.TryBuildCandidate(new SimSnapshot { Name = "Fiora" }, reconnectOnly, t, out reunion);
            SimMemory sharedHistory = new SimMemory { GroupSessions = 2, CompletedOutings = 2 };
            sharedHistory.Normalize();
            bool realReunion = ReunionPolicy.TryBuildCandidate(new SimSnapshot { Name = "Fiora" }, sharedHistory, t, out reunion);
            results.Add("reunion/reconnects alone do not establish history: " + (!falseReunion ? "PASS" : "FAIL"));
            results.Add("reunion/completed outing unlocks returning-Sim greeting: " +
                (realReunion && reunion != null && EventConversationDirector.IsEligibleSpeaker(reunion, "Fiora") &&
                 !EventConversationDirector.IsEligibleSpeaker(reunion, "Phanty") &&
                 reunion.VerifiedContext.IndexOf("2 prior outings", StringComparison.OrdinalIgnoreCase) >= 0 ? "PASS" : "FAIL"));
            string reunionLine = string.Empty;
            bool reunionRendered = realReunion && SocialTemplates.TryRenderEvent(reunion,
                new SimSnapshot { Name = "Fiora" }, RelationshipModel.Describe(0.6f, 0.3f, 0.0f), out reunionLine);
            results.Add("reunion/template stays brief and avoids invented event detail: " +
                (reunionRendered && !string.IsNullOrWhiteSpace(reunionLine) && reunionLine.Length <= 32 &&
                 reunionLine.IndexOf("last time", StringComparison.OrdinalIgnoreCase) < 0 ? "PASS" : "FAIL"));

            string zoneLine;
            bool zoneRendered = SocialTemplates.TryRenderAmbientSeed("zone_preference", string.Empty, 17,
                new SimSnapshot { Name = "Fiora" }, out zoneLine);
            results.Add("ambient/template preserves selected fact-free subject: " +
                (zoneRendered && !string.IsNullOrWhiteSpace(zoneLine) &&
                 (zoneLine.IndexOf("zone", StringComparison.OrdinalIgnoreCase) >= 0 || zoneLine.IndexOf("vibe", StringComparison.OrdinalIgnoreCase) >= 0) ? "PASS" : "FAIL"));
            string unsafeMemoryLine;
            results.Add("ambient/template refuses to paraphrase verified memory: " +
                (!SocialTemplates.TryRenderAmbientSeed("memory:fiora:abc", "Fiora and the player fought a dragon.", 18,
                    new SimSnapshot { Name = "Fiora" }, out unsafeMemoryLine) ? "PASS" : "FAIL"));
            string closeReaction = EncounterReactionPolicy.Render("Completed fight: 2 kills; 1 close call.", 1);
            string crowdReaction = EncounterReactionPolicy.Render("Completed fight: 7 kills; no recorded deaths or close calls.", 2);
            string deathReaction = EncounterReactionPolicy.Render("Completed fight: 1 kill; 2 party deaths.", 3);
            results.Add("encounter/template distinguishes close call: " +
                (closeReaction.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0 ? "PASS" : "FAIL"));
            results.Add("encounter/template distinguishes many enemies without comparison: " +
                (!string.IsNullOrWhiteSpace(crowdReaction) && crowdReaction.IndexOf("smoother", StringComparison.OrdinalIgnoreCase) < 0 ? "PASS" : "FAIL"));
            results.Add("encounter/template distinguishes death from ordinary gg: " +
                (!string.IsNullOrWhiteSpace(deathReaction) && deathReaction != "gg" && deathReaction != "nice" ? "PASS" : "FAIL"));

            SimSnapshot quietSim = new SimSnapshot { Name = "Quiet", Patience = 90, PersonalityCode = 0 };
            SimSnapshot chattySim = new SimSnapshot { Name = "Chatty", Patience = 15, PersonalityCode = 4, Rival = true, Abbreviates = true };
            double quietDesire = PersonalitySpeechPolicy.DesireProbability(quietSim, SocialActivityPreset.Normal, false);
            double chattyDesire = PersonalitySpeechPolicy.DesireProbability(chattySim, SocialActivityPreset.Normal, false);
            results.Add("personality/talk desire is bounded and class-independent: " +
                (chattyDesire > quietDesire && quietDesire >= 0.52 && chattyDesire <= 0.98 ? "PASS" : "FAIL"));

            AdaptiveActivityDecision quietMood = AdaptiveActivityPolicy.Decide(
                new List<SimSnapshot> { quietSim }, SocialContextMode.Normal, false, 0.5);
            AdaptiveActivityDecision chattyMood = AdaptiveActivityPolicy.Decide(
                new List<SimSnapshot> { chattySim }, SocialContextMode.Normal, false, 0.75);
            AdaptiveActivityDecision townMood = AdaptiveActivityPolicy.Decide(
                new List<SimSnapshot> { quietSim }, SocialContextMode.Normal, true, 0.5);
            AdaptiveActivityDecision downtimeMood = AdaptiveActivityPolicy.Decide(
                new List<SimSnapshot> { quietSim }, SocialContextMode.SoftDowntime, false, 0.5);
            results.Add("activity/adaptive personality points can choose Quiet or Lively: " +
                (quietMood.Preset == SocialActivityPreset.Quiet && chattyMood.Preset == SocialActivityPreset.Lively ? "PASS" : "FAIL"));
            results.Add("activity/verified town and downtime raise the same party's score: " +
                (townMood.Score > quietMood.Score && downtimeMood.Score > quietMood.Score ? "PASS" : "FAIL"));
            results.Add("activity/town matching is exact after harmless scene normalization: " +
                (AdaptiveActivityPolicy.IsConfiguredTown("PortAzure", "Port Azure, Some Town") &&
                 !AdaptiveActivityPolicy.IsConfiguredTown("Azure Depths", "Port Azure, Some Town") ? "PASS" : "FAIL"));
            results.Add("activity/adaptive mode remains distinct from manual presets: " +
                (AdaptiveActivityPolicy.IsAdaptive("adaptive") && !AdaptiveActivityPolicy.IsAdaptive("lively") ? "PASS" : "FAIL"));

            SimMemory retrievalMemory = new SimMemory();
            retrievalMemory.Normalize();
            retrievalMemory.ImportantMemories.Add("Found Aetheria after fighting a Lost Sea Giant.");
            retrievalMemory.ImportantMemories.Add("Visited Brakke with the party.");
            retrievalMemory.RecentEvents.Add(new MemoryEvent { type = "kill", text = "Killed a young wolf.", importance = 40 });
            List<RelevantMemory> retrieved = MemoryRelevance.Select(retrievalMemory, "where did we find Aetheria?", 3);
            results.Add("memory/retrieval prefers lexical relevance over newest unrelated event: " +
                (retrieved.Count > 0 && retrieved[0].Text.IndexOf("Aetheria", StringComparison.OrdinalIgnoreCase) >= 0 ? "PASS" : "FAIL"));
            results.Add("preference/questions are not persisted as opinions: " +
                (!PreferenceMemoryPolicy.IsEligible("zone_preference", "best zone vibe?") &&
                 PreferenceMemoryPolicy.IsEligible("zone_preference", "gloomy zones have the best vibe") ? "PASS" : "FAIL"));

            results.Add("expedition/arrival eligible: " + (ExpeditionSocialPolicy.ShouldCreateCandidate("expedition_arrived") ? "PASS" : "FAIL"));
            results.Add("expedition/resume silent: " + (!ExpeditionSocialPolicy.ShouldCreateCandidate("expedition_resumed") ? "PASS" : "FAIL"));
            results.Add("expedition/failure may react but stays bounded: " + (ExpeditionSocialPolicy.ShouldCreateCandidate("expedition_failed") &&
                ExpeditionSocialPolicy.NormalizeChance("expedition_failed", 0.99) <= 0.45 ? "PASS" : "FAIL"));
            SemanticEventDeduplicator eventDedupe = new SemanticEventDeduplicator();
            bool resume1 = eventDedupe.ShouldAccept("expedition_resumed", "resumed toward Vitheo", t);
            bool resume2 = eventDedupe.ShouldAccept("expedition_resumed", "resumed toward Duskenlight", t.AddSeconds(8));
            results.Add("expedition/repeated resume deduplicated: " + (resume1 && !resume2 ? "PASS" : "FAIL"));
            bool campOneSemantic = !CampSemanticAuthority.ShouldEmitLegacyCampStart(true, true) && CampSemanticAuthority.CanonicalCampStartType(true) == "hunt_camp_start";
            results.Add("camp/Campmaster authority suppresses equivalent legacy start: " + (campOneSemantic ? "PASS" : "FAIL"));
            return results;
        }
    }

    internal static class EncounterReactionPolicy
    {
        // Reads only the structured summary produced by SessionTelemetry. It does not infer pull
        // quality, damage, recovery, loot value, or comparisons with an earlier fight.
        internal static string Render(string verifiedContext, int seed)
        {
            string text = verifiedContext ?? string.Empty;
            if (Regex.IsMatch(text, @"\b[1-9]\d*\s+party deaths?\b", RegexOptions.IgnoreCase))
                return Pick(seed, new string[] { "rough one", "oof, that hurt", "that got ugly" });
            if (Regex.IsMatch(text, @"\b[1-9]\d*\s+close calls?\b", RegexOptions.IgnoreCase))
                return Pick(seed, new string[] { "that got a little close", "little too close lol", "okay, that was close" });
            Match kills = Regex.Match(text, @"\b(?<count>[1-9]\d*)\s+kills?\b", RegexOptions.IgnoreCase);
            int count;
            if (kills.Success && int.TryParse(kills.Groups["count"].Value, out count) && count >= 5)
                return Pick(seed, new string[] { "that was a crowd", "busy fight lol", "lot of mobs in that one" });
            if (kills.Success)
                return Pick(seed, new string[] { "clean enough", "nice one", "gg" });
            return string.Empty;
        }

        private static string Pick(int seed, string[] values)
        {
            if (values == null || values.Length == 0) return string.Empty;
            int index = seed == int.MinValue ? 0 : Math.Abs(seed) % values.Length;
            return values[index];
        }
    }
}
