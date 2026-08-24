using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    // Ephemeral "what was actually SAID recently" memory. This is deliberately separate from
    // MemoryStore: MemoryStore records verified gameplay facts that survive across sessions; a
    // ConversationMoment only records that some text was spoken in visible party chat, by whom, and
    // when. Saying "you mentioned tanks having a hard job" is safe because it only claims the line
    // was said - it never claims the underlying opinion, event, or plan is true. Nothing here is ever
    // persisted, and nothing here can be promoted into MemoryStore.
    internal enum ConversationMomentSource
    {
        PlayerSaid,
        SimSaid,
        ExternalNewsConversation,
        RelaxConversation
    }

    internal sealed class ConversationMoment
    {
        internal readonly string TopicKey;
        internal readonly string Speaker;
        internal readonly string TextSummary;
        internal readonly DateTime SaidUtc;
        internal readonly ConversationMomentSource SourceType;
        internal readonly long ConversationGeneration;
        internal readonly double InterestScore;
        internal DateTime ExpiresUtc;

        internal ConversationMoment(string topicKey, string speaker, string textSummary, DateTime saidUtc,
            ConversationMomentSource sourceType, long conversationGeneration, double interestScore, DateTime expiresUtc)
        {
            TopicKey = topicKey ?? string.Empty;
            Speaker = speaker ?? string.Empty;
            TextSummary = textSummary ?? string.Empty;
            SaidUtc = saidUtc;
            SourceType = sourceType;
            ConversationGeneration = conversationGeneration;
            InterestScore = interestScore;
            ExpiresUtc = expiresUtc;
        }

        internal double AgeSeconds(DateTime now) { return Math.Max(0.0, (now - SaidUtc).TotalSeconds); }
    }

    // Pure interest-scoring / TTL / safe-wording policy for callbacks. Kept independent of
    // ConversationTurnGuard's hook detection (which decides "should a reply happen right now") -
    // this instead decides "is this line worth remembering for a few minutes", which is a related
    // but distinct question with its own trivia list and its own callback-wording safety net.
    internal static class ConversationCallbackPolicy
    {
        private static readonly string[] TrivialExact = new string[]
        {
            "gg", "ok", "okay", "k", "lol", "lmao", "yeah", "yea", "yep", "nice", "sure", "cool",
            "thanks", "ty", "haha", "heh", "hi", "hey", "hello", "yo", "sup", "np", "rdy", "ready"
        };

        internal static bool IsTrivialLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (ConversationTurnGuard.IsNoiseLine(text)) return true;
            string m = text.Trim().ToLowerInvariant().Trim('.', '!', '?', ' ');
            for (int i = 0; i < TrivialExact.Length; i++) if (m == TrivialExact[i]) return true;
            return m.Length <= 2;
        }

        // Higher score = more worth remembering. 0 means "never a callback candidate" (trivial lines,
        // routine acknowledgements, WTB/WTS spam, system text, combat commands).
        internal static double InterestScore(string text)
        {
            if (IsTrivialLine(text)) return 0.0;
            string m = text.Trim().ToLowerInvariant();
            double score = 8.0;
            if (m.IndexOf('?') >= 0) score += 10.0; // unanswered question
            string[] opinionHooks = new string[]
            {
                "i think", "i don't think", "i dont think", "i feel like", "honestly", "actually",
                "prefer", "favorite", "hardest", "easiest", "worst", "best", "hate", "love", "disagree"
            };
            for (int i = 0; i < opinionHooks.Length; i++) if (m.Contains(opinionHooks[i])) score += 6.0;
            string[] subjectHooks = new string[]
            {
                "tank", "heal", "dps", "class", "dungeon", "camp", "gear", "loot", "zone", "quest", "boss", "playstyle"
            };
            for (int i = 0; i < subjectHooks.Length; i++) if (m.Contains(subjectHooks[i])) score += 3.0;
            if (m.Contains("no,") || m.Contains("nah") || m.Contains("nope")) score += 6.0; // disagreement
            if (m.Contains("lol") || m.Contains("lmao") || m.Contains("kidding") || m.Contains("teasing")) score += 3.0;
            if (text.Trim().Length >= 40) score += 4.0; // longer remarks tend to carry more substance
            return Math.Min(40.0, score);
        }

        internal const double CandidateThreshold = 10.0;

        internal static bool IsCallbackCandidate(string text) { return InterestScore(text) >= CandidateThreshold; }

        // Strong topics (opinion/question/disagreement) earn a 5-15 minute usable window; an ordinary
        // remark that barely clears the candidate threshold gets only 2-5 minutes.
        internal static double TtlSeconds(double interestScore)
        {
            double clamped = Math.Max(0.0, Math.Min(40.0, interestScore));
            bool strong = clamped >= 20.0;
            double minSeconds = strong ? 300.0 : 120.0;
            double maxSeconds = strong ? 900.0 : 300.0;
            double t = Math.Min(1.0, clamped / 40.0);
            return minSeconds + (t * (maxSeconds - minSeconds));
        }

        // A generated callback line is safe to display when it uses one of these "this was said"
        // phrasings. It is deliberately narrow: the caller decides what to do when neither this nor
        // InventsUnverifiedSharedHistory fires (that is simply not a callback-flavored line).
        private static readonly string[] SafePhrases = new string[]
        {
            "you were saying", "you said earlier", "you mentioned", "still think", "what you said about",
            "earlier you said", "you brought up", "what you were saying"
        };

        // Phrases that assert a SHARED EVENT happened ("we did X together") rather than merely that
        // something was said. These require actual verified MemoryStore support; a plain conversation
        // callback must never manufacture them.
        private static readonly string[] UnsafeSharedHistoryPhrases = new string[]
        {
            "remember when we", "remember that time", " again", "last time we", "last time this", "like we always do"
        };

        internal static bool UsesSafeCallbackWording(string generatedLine)
        {
            if (string.IsNullOrWhiteSpace(generatedLine)) return false;
            string m = " " + generatedLine.Trim().ToLowerInvariant() + " ";
            for (int i = 0; i < SafePhrases.Length; i++)
                if (m.IndexOf(SafePhrases[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        internal static bool InventsUnverifiedSharedHistory(string generatedLine, bool memoryStoreSupportsIt)
        {
            if (memoryStoreSupportsIt) return false;
            if (string.IsNullOrWhiteSpace(generatedLine)) return false;
            string m = " " + generatedLine.Trim().ToLowerInvariant() + " ";
            for (int i = 0; i < UnsafeSharedHistoryPhrases.Length; i++)
                if (m.IndexOf(UnsafeSharedHistoryPhrases[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }
    }

    // Small bounded store of recent ConversationMoments. TTL cleanup and a hard cap keep it from
    // growing unbounded; nothing here is ever written to MemoryStore. A genuinely new player subject
    // invalidates older callback candidates on a different topic so a delayed callback can never
    // resurrect a subject the player has already moved past (priority rule: current thread always wins).
    internal sealed class ConversationMomentStore
    {
        private const int MaxMoments = 10;
        private readonly object _lock = new object();
        private readonly List<ConversationMoment> _moments = new List<ConversationMoment>();

        internal bool Note(string topicKey, string speaker, string text, DateTime now,
            ConversationMomentSource source, long conversationGeneration)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            double interest = ConversationCallbackPolicy.InterestScore(text);
            if (interest < ConversationCallbackPolicy.CandidateThreshold) return false;
            string key = string.IsNullOrWhiteSpace(topicKey) ? PromptBuilder.ClassifyThreadTopic(text) : topicKey;
            string summary = text.Trim();
            if (summary.Length > 140) summary = summary.Substring(0, 140).TrimEnd() + "...";
            DateTime expires = now.AddSeconds(ConversationCallbackPolicy.TtlSeconds(interest));

            lock (_lock)
            {
                _moments.Add(new ConversationMoment(key, speaker, summary, now, source, conversationGeneration, interest, expires));
                PruneLocked(now);
                while (_moments.Count > MaxMoments) _moments.RemoveAt(0);
            }
            return true;
        }

        // Shortens (rather than deletes outright) any moment on a different topic once a genuinely new
        // player subject arrives, so a stale callback candidate quickly falls out of usable range
        // instead of being available to resurrect a subject the player has already left behind.
        // Returns the moments actually shortened, for diagnostics only (e.g. "callback=activity_preference
        // age=310s rejected=newer_active_topic").
        internal List<ConversationMoment> InvalidateConflicting(string newTopicKey, DateTime now)
        {
            List<ConversationMoment> shortened = new List<ConversationMoment>();
            if (string.IsNullOrWhiteSpace(newTopicKey)) return shortened;
            lock (_lock)
            {
                DateTime shortenTo = now.AddSeconds(20.0);
                for (int i = 0; i < _moments.Count; i++)
                {
                    ConversationMoment m = _moments[i];
                    if (m == null) continue;
                    if (string.Equals(m.TopicKey, newTopicKey, StringComparison.OrdinalIgnoreCase)) continue;
                    if (m.ExpiresUtc > shortenTo) { m.ExpiresUtc = shortenTo; shortened.Add(m); }
                }
                PruneLocked(now);
            }
            return shortened;
        }

        internal List<ConversationMoment> Snapshot(DateTime now)
        {
            lock (_lock) { PruneLocked(now); return new List<ConversationMoment>(_moments); }
        }

        // Ranks remaining candidates by interest score with a slow age decay so a very recent remark
        // usually wins over an older one of similar strength. excludeTopicKey lets the caller keep the
        // currently active thread's own subject out of the callback pool (priority: current thread
        // always wins over a callback about the same thing).
        internal bool TryPickCallback(DateTime now, string excludeTopicKey, out ConversationMoment chosen)
        {
            chosen = null;
            lock (_lock)
            {
                PruneLocked(now);
                double best = double.MinValue;
                for (int i = 0; i < _moments.Count; i++)
                {
                    ConversationMoment m = _moments[i];
                    if (m == null) continue;
                    if (!string.IsNullOrWhiteSpace(excludeTopicKey) &&
                        string.Equals(m.TopicKey, excludeTopicKey, StringComparison.OrdinalIgnoreCase)) continue;
                    double score = m.InterestScore - (m.AgeSeconds(now) / 30.0);
                    if (score > best) { best = score; chosen = m; }
                }
                return chosen != null;
            }
        }

        internal void Clear() { lock (_lock) _moments.Clear(); }

        internal int Count { get { lock (_lock) return _moments.Count; } }

        private void PruneLocked(DateTime now)
        {
            for (int i = _moments.Count - 1; i >= 0; i--)
                if (_moments[i] == null || now > _moments[i].ExpiresUtc) _moments.RemoveAt(i);
        }
    }

    // Variable, weighted-band ambient opportunity cadence. Replaces a single fixed cooldown with a
    // small set of weighted time bands per activity preset so ambient/autonomous conversation starts
    // feel organic (sometimes 1-2 min, sometimes 3, sometimes 4-5, occasionally longer) instead of
    // ticking on a metronome. A returned delay is only an OPPORTUNITY to decide - reaching it never
    // forces a message; silence remains a valid outcome every time (see AmbientSeedDecision.SilenceWon).
    internal static class AmbientCadence
    {
        private struct Band
        {
            internal double Weight;
            internal double Min;
            internal double Max;
            internal Band(double weight, double min, double max) { Weight = weight; Min = min; Max = max; }
        }

        private static readonly Band[] NormalBands = new Band[]
        {
            new Band(0.25, 60.0, 120.0),
            new Band(0.40, 120.0, 210.0),
            new Band(0.25, 210.0, 300.0),
            new Band(0.10, 300.0, 420.0)
        };

        private static readonly Band[] LivelyBands = new Band[]
        {
            new Band(0.35, 45.0, 90.0),
            new Band(0.45, 90.0, 180.0),
            new Band(0.20, 180.0, 240.0)
        };

        private static readonly Band[] QuietBands = new Band[]
        {
            new Band(0.50, 180.0, 300.0),
            new Band(0.35, 300.0, 480.0),
            new Band(0.15, 480.0, 660.0)
        };

        private static Band[] BandsFor(SocialActivityPreset preset)
        {
            if (preset == SocialActivityPreset.Lively) return LivelyBands;
            if (preset == SocialActivityPreset.Quiet) return QuietBands;
            return NormalBands;
        }

        // bandRoll/withinRoll are both expected in [0,1). Kept as explicit parameters, rather than
        // reaching for System.Random internally, so the deterministic test suite can drive an exact
        // band without depending on RNG sequencing.
        internal static double NextDelaySeconds(SocialActivityPreset preset, double bandRoll, double withinRoll)
        {
            Band[] bands = BandsFor(preset);
            double roll = Math.Max(0.0, Math.Min(0.999999, bandRoll));
            double cumulative = 0.0;
            Band chosen = bands[bands.Length - 1];
            for (int i = 0; i < bands.Length; i++)
            {
                cumulative += bands[i].Weight;
                if (roll < cumulative) { chosen = bands[i]; break; }
            }
            double within = Math.Max(0.0, Math.Min(1.0, withinRoll));
            return chosen.Min + (within * (chosen.Max - chosen.Min));
        }

        internal static double NextDelaySeconds(SocialActivityPreset preset, Random random)
        {
            if (random == null) random = new Random();
            return NextDelaySeconds(preset, random.NextDouble(), random.NextDouble());
        }

        // Lively remains one party-level clock. Party size changes that clock's bounded opportunity
        // range; it never creates one timer per Sim.
        internal static void LivelyPartyRange(int eligibleParty, out double min, out double max)
        {
            int count = Math.Max(1, Math.Min(5, eligibleParty));
            if (count == 1) { min = 180.0; max = 360.0; }
            else if (count == 2) { min = 120.0; max = 240.0; }
            else if (count == 3) { min = 90.0; max = 180.0; }
            else { min = 60.0; max = 150.0; }
        }

        internal static double NextDelaySeconds(SocialActivityPreset preset, int eligibleParty, Random random)
        {
            if (preset != SocialActivityPreset.Lively) return NextDelaySeconds(preset, random);
            if (random == null) random = new Random();
            double min, max;
            LivelyPartyRange(eligibleParty, out min, out max);
            return min + (random.NextDouble() * (max - min));
        }

        internal static double ThreadFollowUpSeconds(double roll)
        {
            return 8.0 + (Math.Max(0.0, Math.Min(1.0, roll)) * 22.0);
        }

        // Which weighted band (0-based) a given delay landed in, for diagnostics/tests only.
        internal static int BandIndexFor(SocialActivityPreset preset, double delaySeconds)
        {
            Band[] bands = BandsFor(preset);
            for (int i = 0; i < bands.Length; i++)
                if (delaySeconds >= bands[i].Min && delaySeconds <= bands[i].Max) return i;
            return bands.Length - 1;
        }

        internal static double ExpectedSeconds(SocialActivityPreset preset)
        {
            Band[] bands = BandsFor(preset);
            double sum = 0.0;
            for (int i = 0; i < bands.Length; i++) sum += bands[i].Weight * ((bands[i].Min + bands[i].Max) / 2.0);
            return sum;
        }

        // Momentum: after a thread actually starts, temporarily raise the chance of one more natural
        // reply, decaying per reply. replyIndex is 1-based counting the reply about to be attempted (2
        // = second reply in the thread). MaxAutonomousThreadReplies remains the hard cap elsewhere;
        // this only ever supplies a probability, never a guarantee, and returns 0 with no hook at all.
        internal static double ContinuationChance(int replyIndex, bool hasHook, SocialActivityPreset preset)
        {
            return ContinuationChance(replyIndex, hasHook ? SimReplyUrge.Normal : SimReplyUrge.None, preset);
        }

        // Graded form. A boolean hook could not distinguish "Kestrel, did you see that?" from "the
        // rain let up", so an answered thread died at one line far too often. The urge tiers come from
        // SimResponseDecision; the decay across replyIndex is unchanged in shape - only the starting
        // height now depends on how strongly the previous line actually invited an answer.
        internal static double ContinuationChance(int replyIndex, SimReplyUrge urge, SocialActivityPreset preset)
        {
            if (urge == SimReplyUrge.None) return 0.0;
            double presetDelta = preset == SocialActivityPreset.Lively ? 0.12
                : preset == SocialActivityPreset.Quiet ? -0.15 : 0.0;
            double baseChance;
            if (urge == SimReplyUrge.Strong) baseChance = replyIndex <= 2 ? 0.95 : replyIndex == 3 ? 0.72 : 0.45;
            else if (urge == SimReplyUrge.Normal) baseChance = replyIndex <= 2 ? 0.80 : replyIndex == 3 ? 0.50 : 0.25;
            else baseChance = replyIndex <= 2 ? 0.45 : replyIndex == 3 ? 0.22 : 0.10;
            return Math.Max(0.02, Math.Min(1.0, baseChance + presetDelta));
        }
    }
}
