using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ErenshorDeepSims
{
    // Ambient conversation seeding.
    //
    // Erenshor owns what is true.  Deep Sims decides what could be socially relevant.  This file
    // owns the next step only: given the current context, which *subject* (if any) is worth raising
    // right now, and by whom.  It answers "what is worth discussing?"  SocialBudget continues to
    // answer the separate question "may autonomous speech happen at all right now?".
    //
    // Nothing here calls a model, performs a network lookup, reads Unity state, or writes memory.
    // Selection is deterministic for a fixed (opportunity id, state, clock) triple so the same input
    // always produces the same ranking and the same diagnostics.
    internal enum SocialContextMode
    {
        Normal,
        Camp,
        // Deep Sims-only downtime inferred from verified sitting plus local stillness. It is not
        // forwarded to Campmaster and never claims a Hunt Camp or mana state.
        SoftDowntime,
        // Relax is driven by a verified Campmaster detector (CampmasterCompatibility.IsRelaxActive).
        // Expedition remains absent until it has one too.
        Relax
    }

    // Immutable ownership record for a single social utterance.  The director chooses this before
    // expression begins; the model may choose wording only.  Do not use dialogue or memory to
    // replace its subject during a retry.
    internal sealed class SocialIntent
    {
        internal readonly string Source;
        internal readonly string TopicKey;
        internal readonly long ConversationId;
        internal readonly int Generation;
        internal readonly string TriggerText;
        internal readonly string RelevantVerifiedContext;
        internal readonly string Speaker;

        internal SocialIntent(string source, string topicKey, long conversationId, int generation,
            string triggerText, string relevantVerifiedContext, string speaker)
        {
            Source = source ?? string.Empty;
            TopicKey = topicKey ?? string.Empty;
            ConversationId = conversationId;
            Generation = generation;
            TriggerText = triggerText ?? string.Empty;
            RelevantVerifiedContext = relevantVerifiedContext ?? string.Empty;
            Speaker = speaker ?? string.Empty;
        }
    }

    internal enum SilenceFatiguePressure { Normal, Reduced, Strong, ForceSafeSeed }

    internal sealed class SilenceFatigueTracker
    {
        private int _consecutive;
        internal int Consecutive { get { return _consecutive; } }

        internal SilenceFatiguePressure NoteSilence(bool eligible)
        {
            if (!eligible) { Reset(); return SilenceFatiguePressure.Normal; }
            _consecutive++;
            // Full-party Lively downtime should seek a subject on the assessment immediately
            // after one complete silence, while still leaving both selection and output fallible.
            if (_consecutive >= 3) return SilenceFatiguePressure.ForceSafeSeed;
            if (_consecutive == 2) return SilenceFatiguePressure.Strong;
            return SilenceFatiguePressure.Normal;
        }

        internal double SilenceAdjustment
        {
            get { return _consecutive >= 2 ? -20.0 : 0.0; }
        }

        internal void Reset() { _consecutive = 0; }
    }

    internal static class SocialIntentGuard
    {
        // Deliberately small semantic fence, not a general topic classifier.  It only protects
        // selected seed subjects whose expression otherwise tends to drift into whatever old fact
        // happens to be present in the broad world prompt.
        internal static bool Matches(SocialIntent intent, string text)
        {
            if (intent == null || string.IsNullOrWhiteSpace(intent.TopicKey) || string.IsNullOrWhiteSpace(text)) return true;
            string t = " " + text.ToLowerInvariant() + " ";
            string key = intent.TopicKey.ToLowerInvariant();
            if (key == "class_role_preferences" || key == "class_opinion" || key == "other_sim_preference")
                return Has(t, "class", "role", "healer", "healing", "tank", "dps", "reroll", "druid", "arcanist", "paladin", "reaver", "stormcaller", "windblade", "spell", "build");
            if (key == "gear_aesthetics") return Has(t, "gear", "look", "looks", "fashion", "shoulder", "weapon", "armor", "armour", "style");
            if (key == "zone_atmosphere" || key == "zone_preference") return Has(t, "zone", "place", "vibe", "atmosphere", "scenery", "area");
            if (key == "adventure_preferences" || key == "future_activity") return Has(t, "dungeon", "camp", "grind", "grinding", "exploring", "explore", "adventure");
            if (key == "pace_preferences" || key == "pace_preference") return Has(t, "pace", "careful", "fast", "slow", "pull", "pulls");
            if (key == "food_music" || key == "ordinary_downtime") return Has(t, "music", "food", "eat", "snack", "weather");
            if (key == "free_social_question" || key == "free_social_hypothetical" || key == "free_social_impulse")
                return !GroundingGuard.HasInstructionLeak(text);
            if (key == "enemy_design") return Has(t, "enemy", "enemies", "mob", "mobs", "fight", "design");
            if (key == "verified_outing" || key == "verified_history" || key == AmbientTopics.SessionObservation ||
                key.StartsWith("memory_", StringComparison.OrdinalIgnoreCase))
                return MatchesSuppliedFact(intent, t);
            return true; // fact/event/callback topics are fenced by their supplied context instead.
        }

        private static bool MatchesSuppliedFact(SocialIntent intent, string text)
        {
            string fact = (intent.RelevantVerifiedContext ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(fact)) return false;

            // A seed-bound memory/outing line may not borrow a different high-salience subject from
            // broad world context or recent chat. This specifically prevents a recent NASA lookup,
            // expedition, duel, or loot line from hijacking a selected outing observation.
            string[] fencedSubjects = new string[]
            {
                "news", "headline", "nasa", "expedition", "quest", "duel", "loot", "drop",
                "boss", "wipe", "death", "died", "killed", "cooldown"
            };
            for (int i = 0; i < fencedSubjects.Length; i++)
                if (Has(text, fencedSubjects[i]) && fact.IndexOf(fencedSubjects[i], StringComparison.OrdinalIgnoreCase) < 0)
                    return false;

            HashSet<string> anchors = FactAnchors(fact);
            foreach (string anchor in anchors)
                if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b" + System.Text.RegularExpressions.Regex.Escape(anchor) + @"\b")) return true;
            return false;
        }

        private static HashSet<string> FactAnchors(string fact)
        {
            HashSet<string> anchors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string normalized = System.Text.RegularExpressions.Regex.Replace(fact ?? string.Empty, @"[^a-z0-9'\s]", " ");
            string[] words = normalized.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string[] ignored = new string[]
            {
                "the", "and", "that", "this", "with", "from", "party", "current", "verified",
                "outing", "session", "observation", "completed", "active", "recorded", "recent"
            };
            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                if (word.Length < 4 || Array.IndexOf(ignored, word) >= 0) continue;
                anchors.Add(word);
            }
            return anchors;
        }

        private static bool Has(string text, params string[] words)
        {
            for (int i = 0; i < words.Length; i++)
                if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b" + System.Text.RegularExpressions.Regex.Escape(words[i]) + @"\b")) return true;
            return false;
        }
    }

    internal sealed class AmbientSeedDefinition
    {
        internal readonly string TopicKey;
        internal readonly string CooldownGroup;
        internal readonly string PromptHint;
        internal readonly double BaseScore;

        internal AmbientSeedDefinition(string topicKey, string cooldownGroup, string promptHint, double baseScore)
        {
            TopicKey = topicKey ?? string.Empty;
            CooldownGroup = string.IsNullOrWhiteSpace(cooldownGroup) ? TopicKey : cooldownGroup;
            PromptHint = promptHint ?? string.Empty;
            BaseScore = baseScore;
        }
    }

    // Canonical semantic topic keys for ambient downtime chatter.  The prompt hints are the previous
    // CampTopicSeeds wording, now attached to a stable TopicKey so repetition can be measured on the
    // subject rather than on the generated sentence.
    internal static class AmbientTopics
    {
        internal const string IdleWaiting = "idle_waiting";
        internal const string SessionObservation = "session_observation";

        // Base scores raised by a flat +6 relative to the original table. session_observation used to
        // be the only candidate that could realistically clear the silence threshold on its own (its
        // old base 30 + a flat +10 fact bonus reliably crossed 42, while these topics topped out
        // around base+personality+jitter = 40, just short). Without that structural gap, opinion/
        // preference subjects can now actually win a comparable share of ambient opportunities instead
        // of losing to silence by default.
        internal static readonly AmbientSeedDefinition[] Downtime = new AmbientSeedDefinition[]
        {
            new AmbientSeedDefinition("zone_preference", "preference",
                "open directly with a short zone-vibe preference such as 'best-looking zone?' or 'i like gloomy zones :D'; never acknowledge the quiet first", 34.0),
            new AmbientSeedDefinition("class_opinion", "preference",
                "open directly with a short class choice or opinion such as 'healing or tanking?' or 'windblade looks fun lol'", 32.0),
            new AmbientSeedDefinition("future_activity", "planning",
                "open with a general preference like 'dungeons or grinding?' without proposing an actual group plan", 32.0),
            new AmbientSeedDefinition("recovery", "state",
                "make a light observation about waiting on mana, cooldowns, or recovery", 28.0),
            new AmbientSeedDefinition("pace_preference", "preference",
                "ask whether the party prefers a careful or fast pace as a social preference only", 30.0),
            new AmbientSeedDefinition("gear_aesthetics", "gear",
                "open with a small looks-only gear opinion or question such as 'robes or plate for style?' without claiming ownership", 30.0),
            new AmbientSeedDefinition("enemy_design", "world",
                "ask which enemy type has the most interesting design, without inventing a fight", 30.0),
            new AmbientSeedDefinition("ordinary_downtime", "smalltalk",
                "start directly with one tiny off-topic question about food, weather, or music; casual 'lol', ':D', or ':)' is welcome when it fits", 24.0),
            new AmbientSeedDefinition("free_social_question", "smalltalk",
                "ask one harmless opinion question that needs no factual premise, such as a favorite kind of adventure or ideal way to spend a quiet hour", 31.0),
            new AmbientSeedDefinition("free_social_hypothetical", "smalltalk",
                "offer one playful what-if choice with no claim that anything happened", 31.0),
            new AmbientSeedDefinition("free_social_impulse", "smalltalk",
                "say one brief present-tense mood, preference, joke, or social impulse without inventing history", 31.0),
            new AmbientSeedDefinition("other_sim_preference", "social",
                "ask another Sim for a harmless class or zone preference", 34.0),
            new AmbientSeedDefinition("light_tease", "social",
                "react to the quiet moment with a short joke or teasing line", 28.0)
        };

        // Roleplay subject catalog. Deliberately a parallel table rather than a rewrite of Downtime:
        // an in-world adventurer has no opinion about rerolling, grinding, or gear stats, and the MMO
        // table must stay byte-identical for MMO perspective. Base scores mirror the MMO band so
        // perspective changes WHAT is chosen without changing how often a subject beats silence.
        // Faction subjects are NOT in this table; they are appended only when verified faction
        // exposure exists (see AmbientSeedSelector.BuildDowntimeCandidates).
        internal static readonly AmbientSeedDefinition[] RoleplayDowntime = new AmbientSeedDefinition[]
        {
            new AmbientSeedDefinition("rp_place", "world",
                "say something short about how this place feels, using only what is visibly here", 34.0),
            new AmbientSeedDefinition("rp_curiosity", "world",
                "wonder aloud about this place or ask a companion what they know; invent no answer", 32.0),
            new AmbientSeedDefinition("rp_danger", "state",
                "give one short cautious observation about what may lie ahead", 28.0),
            new AmbientSeedDefinition("rp_adventure", "planning",
                "express appetite for going further or turning back, as a preference only", 30.0),
            new AmbientSeedDefinition("rp_downtime", "smalltalk",
                "react briefly to the quiet, in-world, without inventing an event", 24.0),
            new AmbientSeedDefinition("rp_tease", "social",
                "lightly tease a companion about the present moment", 28.0),
            new AmbientSeedDefinition("rp_companions", "social",
                "say one short thing to a visible companion about right now", 34.0),
            new AmbientSeedDefinition("rp_belief", "preference",
                "offer a short personal opinion or doubt without asserting any fact", 30.0)
        };

        // Offered only when the speaker's class carries a verified cultural affinity. Weighted at the
        // low end of the RP band so a character's tradition occasionally colours conversation instead
        // of dominating it, and it flows through the same fatigue/duplicate/silence machinery as every
        // other subject -- no timer, no class cooldown, no guaranteed class talk.
        internal static readonly AmbientSeedDefinition RoleplayClassInterest = new AmbientSeedDefinition(
            "rp_class_interest", "world",
            "say one short thing that reflects what your training makes you notice here; claim no order, faith, or past", 22.0);

        // Only usable when verified faction exposure is supplied; never selected merely because
        // factions exist in the world.
        internal static readonly AmbientSeedDefinition RoleplayFactionOpinion = new AmbientSeedDefinition(
            "rp_faction_opinion", "world",
            "give one short attitude toward a faction the party has actually dealt with; assert no history, motive, or membership", 30.0);

        internal static readonly AmbientSeedDefinition RoleplayFactionUncertainty = new AmbientSeedDefinition(
            "rp_faction_uncertainty", "world",
            "admit you do not know enough about a faction the party has encountered; invent nothing", 26.0);

        // The party standing around is expected context, not a subject.  This exists so the idea has
        // one canonical key that fatigue and diagnostics can talk about, and it is scored so low that
        // it normally loses to silence.
        internal static readonly AmbientSeedDefinition Idle = new AmbientSeedDefinition(IdleWaiting, "idle",
            "acknowledge the quiet moment very briefly without inventing an event", -20.0);

        // Base score sits inside the same band as the opinion/preference topics above rather than
        // above all of them. A verified fact still gives it an edge (see FactBonus below), but it no
        // longer starts every comparison from a structurally higher floor than everything else, which
        // is what let "quiet here"/"nothing happening" observations dominate subject selection.
        internal static readonly AmbientSeedDefinition Observation = new AmbientSeedDefinition(
            SessionObservation, "session",
            "react to the supplied verified session observation; do not add facts it does not contain", 24.0);

        // Relax topics (Campmaster explicit social downtime). Kept in the same base-score band as
        // Downtime so the shared AmbientSeedSelector produces comparable subject variety there too.
        internal static readonly AmbientSeedDefinition[] Relax = new AmbientSeedDefinition[]
        {
            new AmbientSeedDefinition("class_role_preferences", "preference",
                "start directly with one short class/role preference such as 'healing or tanking?' or 'which class looks most fun?'", 32.0),
            new AmbientSeedDefinition("zone_atmosphere", "preference",
                "start directly with one short zone-atmosphere preference or question; do not acknowledge that everyone is relaxing first", 32.0),
            new AmbientSeedDefinition("adventure_preferences", "planning",
                "ask what kind of adventure, dungeon, grinding, or exploration people generally enjoy", 30.0),
            new AmbientSeedDefinition("pace_preferences", "preference",
                "ask whether people generally prefer careful pulls or fast pacing as a social preference only", 28.0),
            new AmbientSeedDefinition("gear_aesthetics", "gear",
                "talk about gear looks, weapon style, or loot aesthetics as opinions only", 30.0),
            new AmbientSeedDefinition("enemy_design", "world",
                "ask which kinds of enemies or encounter designs people find interesting or annoying", 28.0),
            new AmbientSeedDefinition("food_music", "smalltalk",
                "start directly with one tiny food, music, or weather question; an occasional 'lol', ':D', or ':)' is welcome", 24.0),
            new AmbientSeedDefinition("party_preferences", "social",
                "ask another visible party member a harmless preference or opinion question", 26.0),
            new AmbientSeedDefinition("verified_outing", "memory",
                "react to or ask about the supplied verified outing fact without adding unverified detail", 26.0),
            new AmbientSeedDefinition("verified_history", "memory",
                "briefly reference one verified shared memory if supplied, otherwise ask a generic preference", 24.0),
            new AmbientSeedDefinition("light_teasing", "social",
                "make a short friendly joke or light tease grounded only in current personality/relationship tone", 28.0)
        };

        internal static AmbientSeedDefinition Find(string topicKey)
        {
            if (string.IsNullOrWhiteSpace(topicKey)) return null;
            if (string.Equals(topicKey, IdleWaiting, StringComparison.OrdinalIgnoreCase)) return Idle;
            if (string.Equals(topicKey, SessionObservation, StringComparison.OrdinalIgnoreCase)) return Observation;
            for (int i = 0; i < Downtime.Length; i++)
                if (string.Equals(Downtime[i].TopicKey, topicKey, StringComparison.OrdinalIgnoreCase))
                    return Downtime[i];
            for (int i = 0; i < Relax.Length; i++)
                if (string.Equals(Relax[i].TopicKey, topicKey, StringComparison.OrdinalIgnoreCase))
                    return Relax[i];
            return null;
        }

        // Verified-fact scoring bonus, differentiated by category rather than one flat number. A
        // Sim's own personal memory/outing recollection is comparatively rare and worth a strong
        // bump; the shared session_observation fact is available on almost every ambient opportunity
        // (it just cycles through a handful of outing telemetry lines), so it gets a smaller nudge and
        // has to compete with the other subjects on jitter/personality/fatigue like everything else.
        internal static double FactBonus(string cooldownGroup)
        {
            if (string.Equals(cooldownGroup, "session", StringComparison.OrdinalIgnoreCase)) return 6.0;
            if (string.Equals(cooldownGroup, "memory", StringComparison.OrdinalIgnoreCase)) return 9.0;
            if (string.Equals(cooldownGroup, "recovery", StringComparison.OrdinalIgnoreCase)) return 9.0;
            return 7.0;
        }

        // Deliberately lexical and deliberately narrow: several different sentences that all mean
        // "we are standing here doing nothing" must collapse onto idle_waiting so fatigue treats them
        // as one subject.  This is not sentence-similarity detection and must not be used to guess
        // any other topic; it returns null when the line is about something.
        internal static string ClassifyIdleVariant(string text)
        {
            // Apostrophes are dropped rather than turned into a word break so "I'm"/"we're" collapse
            // onto the same tokens as "im"/"were".
            string collapsed = (text ?? string.Empty).Replace("'", string.Empty).Replace("’", string.Empty);
            string normalized = SocialBudget.NormalizeSemantic(collapsed);
            if (normalized.Length == 0) return null;
            string padded = " " + normalized + " ";
            string[] phrases = new string[]
            {
                " nothing is happening ", " nothing happening ", " nothing much happening ",
                " nothing going on ", " nothing much going on ", " not much going on ",
                " not much happening ", " not a lot going on ",
                " im waiting ", " i am waiting ", " im just waiting ", " i am just waiting ",
                " just waiting ", " still waiting ",
                " were just standing here ", " we are just standing here ", " just standing here ",
                " were standing around ", " we are standing around ", " standing around ",
                " were just sitting here ", " we are just sitting here ", " just sitting here ",
                " were sitting here ", " we are sitting here "
            };
            for (int i = 0; i < phrases.Length; i++)
                if (padded.IndexOf(phrases[i], StringComparison.Ordinal) >= 0) return IdleWaiting;
            return null;
        }
    }

    // One concrete subject that could be raised at this moment.  A candidate carrying a Fact must
    // also carry the provenance label for that fact; the selector excludes unsourced facts rather
    // than letting the model narrate them.
    internal sealed class AmbientSeedCandidate
    {
        internal readonly string TopicKey;
        internal readonly string CooldownGroup;
        internal readonly string PromptHint;
        internal readonly double BaseScore;
        internal readonly string Fact;
        internal readonly string FactSource;
        internal readonly int Importance;
        internal readonly double GroundingRisk;
        internal readonly DateTime CreatedUtc;
        internal readonly DateTime ExpiresUtc;
        internal readonly List<string> EligibleSpeakerNames;

        internal AmbientSeedCandidate(string topicKey, string cooldownGroup, string promptHint, double baseScore,
            string fact, string factSource, int importance, double groundingRisk,
            DateTime createdUtc, DateTime expiresUtc, IEnumerable<string> eligibleSpeakerNames)
        {
            TopicKey = topicKey ?? string.Empty;
            CooldownGroup = string.IsNullOrWhiteSpace(cooldownGroup) ? TopicKey : cooldownGroup;
            PromptHint = promptHint ?? string.Empty;
            BaseScore = baseScore;
            Fact = fact ?? string.Empty;
            FactSource = factSource ?? string.Empty;
            Importance = Math.Max(0, Math.Min(100, importance));
            GroundingRisk = Math.Max(0.0, groundingRisk);
            CreatedUtc = createdUtc;
            ExpiresUtc = expiresUtc;
            EligibleSpeakerNames = new List<string>();
            if (eligibleSpeakerNames != null)
                foreach (string name in eligibleSpeakerNames)
                    if (!string.IsNullOrWhiteSpace(name) && !EligibleSpeakerNames.Contains(name))
                        EligibleSpeakerNames.Add(name);
        }

        internal AmbientSeedCandidate(AmbientSeedDefinition definition, DateTime createdUtc)
            : this(definition == null ? string.Empty : definition.TopicKey,
                definition == null ? string.Empty : definition.CooldownGroup,
                definition == null ? string.Empty : definition.PromptHint,
                definition == null ? 0.0 : definition.BaseScore,
                null, null, 0, 0.0, createdUtc, DateTime.MaxValue, null)
        {
        }

        internal bool HasFact { get { return Fact.Length > 0; } }

        internal bool IsEligibleSpeaker(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (EligibleSpeakerNames.Count == 0) return true;
            for (int i = 0; i < EligibleSpeakerNames.Count; i++)
                if (string.Equals(EligibleSpeakerNames[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    internal sealed class TopicUsage
    {
        internal string TopicKey;
        internal string CooldownGroup;
        internal DateTime LastUsedUtc;
        internal int RecentUseCount;
        internal string LastSpeaker;
        internal long LastConversationId;
    }

    internal sealed class TopicRejectionUsage
    {
        internal string TopicKey;
        internal string Speaker;
        internal DateTime LastRejectedUtc;
        internal int RecentRejectCount;
        internal string LastReason;
    }

    // Transient, bounded record of which subjects were actually said recently.  Nothing here is
    // persisted: choosing a topic is not a memory, and a topic is only recorded once a line has
    // actually been emitted.
    internal sealed class TopicFatigueTracker
    {
        private const int MaxTrackedTopics = 32;
        private const int MaxTrackedGroups = 24;
        private const double VeryRecentSeconds = 90.0;
        private const double GroupRecentSeconds = 120.0;
        private const double SpeakerRepeatSeconds = 600.0;
        private const double RejectionWindowSeconds = 180.0;
        private const int MaxTrackedRejections = 32;

        private readonly object _lock = new object();
        private readonly Dictionary<string, TopicUsage> _usage =
            new Dictionary<string, TopicUsage>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _groups =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TopicRejectionUsage> _rejections =
            new Dictionary<string, TopicRejectionUsage>(StringComparer.OrdinalIgnoreCase);

        private readonly double _fatigueSeconds;
        private readonly double _recentWindowSeconds;

        internal TopicFatigueTracker() : this(300.0, 600.0) { }

        internal TopicFatigueTracker(double fatigueSeconds, double recentWindowSeconds)
        {
            _fatigueSeconds = Math.Max(30.0, fatigueSeconds);
            _recentWindowSeconds = Math.Max(_fatigueSeconds, recentWindowSeconds);
        }

        internal void NoteUsed(string topicKey, string cooldownGroup, string speaker, long conversationId, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(topicKey)) return;
            lock (_lock)
            {
                PruneLocked(now);
                TopicUsage usage;
                if (!_usage.TryGetValue(topicKey, out usage))
                {
                    usage = new TopicUsage { TopicKey = topicKey, RecentUseCount = 0 };
                    _usage[topicKey] = usage;
                }
                if (usage.LastUsedUtc != DateTime.MinValue &&
                    (now - usage.LastUsedUtc).TotalSeconds > _recentWindowSeconds) usage.RecentUseCount = 0;
                usage.CooldownGroup = string.IsNullOrWhiteSpace(cooldownGroup) ? topicKey : cooldownGroup;
                usage.LastUsedUtc = now;
                usage.RecentUseCount = Math.Min(16, usage.RecentUseCount + 1);
                usage.LastSpeaker = speaker ?? string.Empty;
                usage.LastConversationId = conversationId;
                _groups[usage.CooldownGroup] = now;
                PruneLocked(now);
            }
        }

        internal void NoteRejected(string topicKey, string speaker, string reason, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(topicKey)) return;
            string who = string.IsNullOrWhiteSpace(speaker) ? "*" : speaker.Trim();
            string key = topicKey.Trim() + "|" + who;
            lock (_lock)
            {
                PruneLocked(now);
                TopicRejectionUsage usage;
                if (!_rejections.TryGetValue(key, out usage))
                {
                    usage = new TopicRejectionUsage { TopicKey = topicKey.Trim(), Speaker = who };
                    _rejections[key] = usage;
                }
                if (usage.LastRejectedUtc != DateTime.MinValue &&
                    (now - usage.LastRejectedUtc).TotalSeconds > RejectionWindowSeconds) usage.RecentRejectCount = 0;
                usage.LastRejectedUtc = now;
                usage.RecentRejectCount = Math.Min(4, usage.RecentRejectCount + 1);
                usage.LastReason = string.IsNullOrWhiteSpace(reason) ? "rejected" : reason.Trim();
                PruneLocked(now);
            }
        }

        // Positive number to subtract from a candidate score.  double.MaxValue means "exclude".
        internal double Penalty(string topicKey, string cooldownGroup, string speaker,
            long activeConversationId, DateTime now, out string detail)
        {
            detail = string.Empty;
            if (string.IsNullOrWhiteSpace(topicKey)) return 0.0;
            lock (_lock)
            {
                PruneLocked(now);
                TopicUsage usage;
                bool known = _usage.TryGetValue(topicKey, out usage) && usage.LastUsedUtc != DateTime.MinValue;
                double penalty = 0.0;
                StringBuilder notes = new StringBuilder();

                if (known)
                {
                    if (activeConversationId != 0 && usage.LastConversationId == activeConversationId)
                    {
                        detail = "already covered in the active conversation";
                        return double.MaxValue;
                    }

                    double age = (now - usage.LastUsedUtc).TotalSeconds;
                    if (age < VeryRecentSeconds) { penalty += 55.0; notes.Append("used <" + (int)VeryRecentSeconds + "s"); }
                    else if (age < _fatigueSeconds) { penalty += 35.0; notes.Append("used <" + (int)_fatigueSeconds + "s"); }

                    if (!string.IsNullOrWhiteSpace(speaker) &&
                        string.Equals(usage.LastSpeaker, speaker, StringComparison.OrdinalIgnoreCase) &&
                        age < SpeakerRepeatSeconds)
                    {
                        penalty += 12.0;
                        if (notes.Length > 0) notes.Append(", ");
                        notes.Append("same speaker");
                    }

                    int extra = Math.Max(0, usage.RecentUseCount - 1);
                    double cumulative = Math.Min(24.0, extra * 8.0);
                    if (cumulative > 0.0)
                    {
                        penalty += cumulative;
                        if (notes.Length > 0) notes.Append(", ");
                        notes.Append(usage.RecentUseCount + " recent uses");
                    }
                }

                string rejectionKey = topicKey.Trim() + "|" + (string.IsNullOrWhiteSpace(speaker) ? "*" : speaker.Trim());
                TopicRejectionUsage rejected;
                if (_rejections.TryGetValue(rejectionKey, out rejected) && rejected.LastRejectedUtc != DateTime.MinValue)
                {
                    double rejectAge = (now - rejected.LastRejectedUtc).TotalSeconds;
                    if (rejectAge < RejectionWindowSeconds)
                    {
                        double rejectPenalty = rejectAge < 20.0 ? 24.0 : rejectAge < 60.0 ? 14.0 : 6.0;
                        rejectPenalty += Math.Min(18.0, Math.Max(0, rejected.RecentRejectCount - 1) * 6.0);
                        penalty += rejectPenalty;
                        if (notes.Length > 0) notes.Append(", ");
                        notes.Append("recent grounding rejection");
                    }
                }

                string group = string.IsNullOrWhiteSpace(cooldownGroup) ? topicKey : cooldownGroup;
                DateTime groupUsed;
                if (_groups.TryGetValue(group, out groupUsed) &&
                    (now - groupUsed).TotalSeconds < GroupRecentSeconds)
                {
                    bool sameTopicOnly = known && (now - usage.LastUsedUtc).TotalSeconds < GroupRecentSeconds &&
                        string.Equals(usage.CooldownGroup, group, StringComparison.OrdinalIgnoreCase) &&
                        usage.LastUsedUtc == groupUsed;
                    if (!sameTopicOnly)
                    {
                        penalty += 20.0;
                        if (notes.Length > 0) notes.Append(", ");
                        notes.Append("group '" + group + "' recent");
                    }
                }

                detail = notes.ToString();
                return penalty;
            }
        }

        internal string Describe(DateTime now)
        {
            lock (_lock)
            {
                PruneLocked(now);
                if (_usage.Count == 0) return "no topics used recently";
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, TopicUsage> pair in _usage)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(pair.Key).Append(" x").Append(pair.Value.RecentUseCount)
                        .Append(" (").Append((int)(now - pair.Value.LastUsedUtc).TotalSeconds).Append("s ago");
                    if (!string.IsNullOrWhiteSpace(pair.Value.LastSpeaker))
                        sb.Append(", ").Append(pair.Value.LastSpeaker);
                    sb.Append(")");
                }
                return sb.ToString();
            }
        }

        internal void Clear()
        {
            lock (_lock)
            {
                _usage.Clear();
                _groups.Clear();
                _rejections.Clear();
            }
        }

        // Callers must already hold _lock.
        private void PruneLocked(DateTime now)
        {
            if (_usage.Count > 4)
            {
                List<string> remove = null;
                foreach (KeyValuePair<string, TopicUsage> pair in _usage)
                    if ((now - pair.Value.LastUsedUtc).TotalSeconds > _recentWindowSeconds)
                        (remove ?? (remove = new List<string>())).Add(pair.Key);
                if (remove != null) for (int i = 0; i < remove.Count; i++) _usage.Remove(remove[i]);
            }
            while (_usage.Count > MaxTrackedTopics) RemoveOldestLocked(_usage);

            if (_groups.Count > 4)
            {
                List<string> remove = null;
                foreach (KeyValuePair<string, DateTime> pair in _groups)
                    if ((now - pair.Value).TotalSeconds > _recentWindowSeconds)
                        (remove ?? (remove = new List<string>())).Add(pair.Key);
                if (remove != null) for (int i = 0; i < remove.Count; i++) _groups.Remove(remove[i]);
            }
            while (_groups.Count > MaxTrackedGroups)
            {
                string oldest = null;
                DateTime oldestUtc = DateTime.MaxValue;
                foreach (KeyValuePair<string, DateTime> pair in _groups)
                    if (pair.Value < oldestUtc) { oldestUtc = pair.Value; oldest = pair.Key; }
                if (oldest == null) break;
                _groups.Remove(oldest);
            }

            if (_rejections.Count > 4)
            {
                List<string> rejectedRemove = null;
                foreach (KeyValuePair<string, TopicRejectionUsage> pair in _rejections)
                    if (pair.Value == null || pair.Value.LastRejectedUtc == DateTime.MinValue ||
                        (now - pair.Value.LastRejectedUtc).TotalSeconds > RejectionWindowSeconds)
                        (rejectedRemove ?? (rejectedRemove = new List<string>())).Add(pair.Key);
                if (rejectedRemove != null) for (int i = 0; i < rejectedRemove.Count; i++) _rejections.Remove(rejectedRemove[i]);
            }
            while (_rejections.Count > MaxTrackedRejections)
            {
                string oldestRejected = null;
                DateTime oldestRejectedUtc = DateTime.MaxValue;
                foreach (KeyValuePair<string, TopicRejectionUsage> pair in _rejections)
                    if (pair.Value != null && pair.Value.LastRejectedUtc < oldestRejectedUtc)
                    { oldestRejectedUtc = pair.Value.LastRejectedUtc; oldestRejected = pair.Key; }
                if (oldestRejected == null) break;
                _rejections.Remove(oldestRejected);
            }
        }

        private static void RemoveOldestLocked(Dictionary<string, TopicUsage> map)
        {
            string oldest = null;
            DateTime oldestUtc = DateTime.MaxValue;
            foreach (KeyValuePair<string, TopicUsage> pair in map)
                if (pair.Value.LastUsedUtc < oldestUtc) { oldestUtc = pair.Value.LastUsedUtc; oldest = pair.Key; }
            if (oldest != null) map.Remove(oldest);
        }
    }

    internal sealed class SeedScoreComponent
    {
        internal readonly string Name;
        internal readonly double Value;
        internal SeedScoreComponent(string name, double value) { Name = name; Value = value; }
    }

    internal sealed class AmbientSeedScore
    {
        internal string TopicKey;
        internal string Speaker;
        internal string Source;
        internal double Score;
        internal string ExcludedReason;
        internal List<SeedScoreComponent> Components;
        internal bool Excluded { get { return !string.IsNullOrEmpty(ExcludedReason); } }
    }

    // Silence is a first-class outcome, not a fabricated seed.  A decision where SelectedTopicKey is
    // empty means "nobody had anything worth saying"; no model request is made for it.
    internal sealed class AmbientSeedDecision
    {
        internal long OpportunityId;
        internal DateTime Utc;
        internal SocialContextMode Mode;
        internal double SilenceScore;
        internal string SelectedTopicKey = string.Empty;
        internal string SelectedCooldownGroup = string.Empty;
        internal string SelectedSpeaker = string.Empty;
        internal string SelectedPromptHint = string.Empty;
        internal string SelectedFact = string.Empty;
        internal string SelectedSource = string.Empty;
        internal double SelectedScore;
        internal string Reason = string.Empty;
        internal string Outcome = "pending";
        internal List<AmbientSeedScore> Candidates = new List<AmbientSeedScore>();
        internal List<string> SpeakerCandidates = new List<string>();

        internal bool SilenceWon { get { return string.IsNullOrEmpty(SelectedTopicKey); } }
    }

    internal static class AmbientSeedPrerequisitePolicy
    {
        // Reject semantic seeds whose missing participant is already knowable before generation.
        // This is intentionally tiny: all fact/memory provenance remains owned by the existing
        // candidate Fact/FactSource and per-speaker eligibility contracts.
        internal static bool IsSupported(AmbientSeedCandidate candidate, IList<SimSnapshot> speakers, out string reason)
        {
            reason = string.Empty;
            if (candidate == null) { reason = "missing candidate"; return false; }
            string key = (candidate.TopicKey ?? string.Empty).Trim().ToLowerInvariant();
            bool needsAnotherSim = key == "other_sim_preference" || key == "party_preferences" || key == "rp_companions";
            if (!needsAnotherSim) return true;
            int eligible = 0;
            for (int i = 0; speakers != null && i < speakers.Count; i++)
            {
                SimSnapshot sim = speakers[i];
                if (sim != null && !string.IsNullOrWhiteSpace(sim.Name) && candidate.IsEligibleSpeaker(sim.Name)) eligible++;
            }
            if (eligible >= 2) return true;
            reason = "requires another visible eligible Sim";
            return false;
        }
    }

    internal static class AmbientSeedSelector
    {
        internal const double DefaultSilenceNormal = 42.0;
        internal const double DefaultSilenceCamp = 38.0;
        internal const double DefaultSilenceRelax = 34.0;

        internal static double SilenceBase(SocialContextMode mode, double normal, double camp)
        {
            return mode == SocialContextMode.Camp ? camp : normal;
        }

        internal static double SilenceBase(SocialContextMode mode, double normal, double camp, double relax)
        {
            if (mode == SocialContextMode.Relax) return relax;
            if (mode == SocialContextMode.SoftDowntime) return Math.Max(1.0, relax + 2.0);
            return mode == SocialContextMode.Camp ? camp : normal;
        }

        // Ranks every (candidate, eligible speaker) pair against an explicit silence score.  The
        // party is small, so this stays a handful of cheap comparisons.  quietPressure (0..1) and
        // silenceAdjust let the caller fold in "how long has it been quiet" and the Quiet/Normal/
        // Lively preset without adding a second cadence system.
        internal static AmbientSeedDecision Select(long opportunityId, SocialContextMode mode,
            IList<AmbientSeedCandidate> candidates, IList<SimSnapshot> speakers,
            TopicFatigueTracker fatigue, long activeConversationId, DateTime now,
            double silenceNormal, double silenceCamp, double quietPressure, double silenceAdjust,
            bool forceSpeech, bool captureComponents)
        {
            return Select(opportunityId, mode, candidates, speakers, fatigue, activeConversationId, now,
                silenceNormal, silenceCamp, DefaultSilenceRelax, quietPressure, silenceAdjust,
                forceSpeech, captureComponents, null, 0.0);
        }

        // familiarityBySpeaker is an optional, bounded 0..1 tone nudge (Sim-to-player familiarity),
        // read from existing relationship state by the caller. It never creates a candidate on its
        // own and never affects grounding risk or fatigue.
        internal static AmbientSeedDecision Select(long opportunityId, SocialContextMode mode,
            IList<AmbientSeedCandidate> candidates, IList<SimSnapshot> speakers,
            TopicFatigueTracker fatigue, long activeConversationId, DateTime now,
            double silenceNormal, double silenceCamp, double quietPressure, double silenceAdjust,
            bool forceSpeech, bool captureComponents, IDictionary<string, double> familiarityBySpeaker)
        {
            return Select(opportunityId, mode, candidates, speakers, fatigue, activeConversationId, now,
                silenceNormal, silenceCamp, DefaultSilenceRelax, quietPressure, silenceAdjust,
                forceSpeech, captureComponents, familiarityBySpeaker, 0.0);
        }

        internal static AmbientSeedDecision Select(long opportunityId, SocialContextMode mode,
            IList<AmbientSeedCandidate> candidates, IList<SimSnapshot> speakers,
            TopicFatigueTracker fatigue, long activeConversationId, DateTime now,
            double silenceNormal, double silenceCamp, double silenceRelax, double quietPressure, double silenceAdjust,
            bool forceSpeech, bool captureComponents, IDictionary<string, double> familiarityBySpeaker,
            double contextualSourcePreference = 0.0)
        {
            AmbientSeedDecision decision = new AmbientSeedDecision();
            decision.OpportunityId = opportunityId;
            decision.Utc = now;
            decision.Mode = mode;

            double silence = SilenceBase(mode, silenceNormal, silenceCamp, silenceRelax);
            silence -= Math.Max(0.0, Math.Min(1.0, quietPressure)) * 6.0;
            silence += silenceAdjust;
            if (forceSpeech) silence = -1000.0;
            decision.SilenceScore = Math.Round(silence, 2);

            for (int i = 0; speakers != null && i < speakers.Count; i++)
                if (speakers[i] != null && !string.IsNullOrWhiteSpace(speakers[i].Name))
                    decision.SpeakerCandidates.Add(speakers[i].Name);

            if (decision.SpeakerCandidates.Count == 0)
            {
                decision.Outcome = "silence";
                decision.Reason = "no eligible Deep Sim speaker";
                return decision;
            }

            AmbientSeedScore best = null;
            for (int c = 0; candidates != null && c < candidates.Count; c++)
            {
                AmbientSeedCandidate candidate = candidates[c];
                if (candidate == null || candidate.TopicKey.Length == 0) continue;

                if (now > candidate.ExpiresUtc)
                {
                    decision.Candidates.Add(Excluded(candidate.TopicKey, candidate.FactSource, "expired"));
                    continue;
                }
                if (candidate.HasFact && candidate.FactSource.Length == 0)
                {
                    decision.Candidates.Add(Excluded(candidate.TopicKey, candidate.FactSource, "unsupported provenance"));
                    continue;
                }
                string prerequisiteReason;
                if (!AmbientSeedPrerequisitePolicy.IsSupported(candidate, speakers, out prerequisiteReason))
                {
                    decision.Candidates.Add(Excluded(candidate.TopicKey, candidate.FactSource, prerequisiteReason));
                    continue;
                }

                AmbientSeedScore bestForCandidate = null;
                string exclusion = null;
                for (int s = 0; s < speakers.Count; s++)
                {
                    SimSnapshot sim = speakers[s];
                    if (sim == null || string.IsNullOrWhiteSpace(sim.Name)) continue;
                    if (!candidate.IsEligibleSpeaker(sim.Name)) { exclusion = exclusion ?? "no eligible speaker"; continue; }

                    string fatigueDetail = string.Empty;
                    double penalty = 0.0;
                    if (fatigue != null)
                        penalty = fatigue.Penalty(candidate.TopicKey, candidate.CooldownGroup,
                            sim.Name, activeConversationId, now, out fatigueDetail);
                    if (penalty == double.MaxValue)
                    {
                        exclusion = fatigueDetail.Length > 0 ? fatigueDetail : "topic already covered";
                        continue;
                    }

                    List<SeedScoreComponent> components = captureComponents ? new List<SeedScoreComponent>() : null;
                    double score = candidate.BaseScore;
                    Note(components, "category", candidate.BaseScore);

                    double importance = candidate.Importance / 4.0;
                    if (importance != 0.0) { score += importance; Note(components, "importance", importance); }

                    if (candidate.HasFact)
                    {
                        double factBonus = AmbientTopics.FactBonus(candidate.CooldownGroup);
                        score += factBonus;
                        Note(components, "verified_fact", factBonus);
                    }

                    double familiarity = 0.0;
                    if (familiarityBySpeaker != null) familiarityBySpeaker.TryGetValue(sim.Name, out familiarity);
                    double personality = PersonalityAffinity(sim, candidate.TopicKey, familiarity);
                    if (personality != 0.0) { score += personality; Note(components, "personality", personality); }

                    // This is a small tie-break-style nudge, not a category scheduler. It runs only
                    // when the caller has established full-party Lively downtime and never creates,
                    // validates, or makes a candidate eligible by itself.
                    double contextual = ContextualSourceBonus(candidate, contextualSourcePreference);
                    if (contextual != 0.0) { score += contextual; Note(components, "contextual_source", contextual); }

                    // Deterministic per-opportunity variation.  This keeps a fixed input reproducible
                    // while stopping the highest static base score from always winning.
                    double jitter = Jitter(opportunityId, candidate.TopicKey, sim.Name);
                    score += jitter;
                    Note(components, "variation", jitter);

                    if (candidate.GroundingRisk != 0.0)
                    {
                        score -= candidate.GroundingRisk;
                        Note(components, "grounding_risk", -candidate.GroundingRisk);
                    }
                    if (penalty != 0.0)
                    {
                        score -= penalty;
                        Note(components, "topic_fatigue", -penalty);
                    }

                    AmbientSeedScore scored = new AmbientSeedScore
                    {
                        TopicKey = candidate.TopicKey,
                        Speaker = sim.Name,
                        Source = PrivacySafeSource(candidate),
                        Score = Math.Round(score, 2),
                        Components = components
                    };
                    if (bestForCandidate == null || Beats(scored, bestForCandidate)) bestForCandidate = scored;
                }

                if (bestForCandidate == null)
                {
                    decision.Candidates.Add(Excluded(candidate.TopicKey, candidate.FactSource, exclusion ?? "no eligible speaker"));
                    continue;
                }

                decision.Candidates.Add(bestForCandidate);
                if (best == null || Beats(bestForCandidate, best))
                {
                    best = bestForCandidate;
                    decision.SelectedPromptHint = candidate.PromptHint;
                    decision.SelectedFact = candidate.Fact;
                    decision.SelectedSource = PrivacySafeSource(candidate);
                    decision.SelectedCooldownGroup = candidate.CooldownGroup;
                }
            }

            decision.Candidates.Sort(delegate(AmbientSeedScore a, AmbientSeedScore b)
            {
                if (a.Excluded != b.Excluded) return a.Excluded ? 1 : -1;
                if (a.Excluded) return string.Compare(a.TopicKey, b.TopicKey, StringComparison.Ordinal);
                int byScore = b.Score.CompareTo(a.Score);
                return byScore != 0 ? byScore : string.Compare(a.TopicKey, b.TopicKey, StringComparison.Ordinal);
            });

            if (best == null)
            {
                decision.SelectedPromptHint = string.Empty;
                decision.SelectedFact = string.Empty;
                decision.SelectedSource = string.Empty;
                decision.SelectedCooldownGroup = string.Empty;
                decision.Outcome = "silence";
                decision.Reason = "no usable candidate subject";
                return decision;
            }

            if (best.Score <= decision.SilenceScore)
            {
                decision.SelectedPromptHint = string.Empty;
                decision.SelectedFact = string.Empty;
                decision.SelectedSource = string.Empty;
                decision.SelectedCooldownGroup = string.Empty;
                decision.SelectedScore = best.Score;
                decision.Outcome = "silence";
                decision.Reason = "best subject " + best.TopicKey + " scored " + best.Score.ToString("0.##") +
                    ", below silence " + decision.SilenceScore.ToString("0.##");
                return decision;
            }

            decision.SelectedTopicKey = best.TopicKey;
            decision.SelectedSpeaker = best.Speaker;
            decision.SelectedScore = best.Score;
            decision.Outcome = "selected";
            decision.Reason = best.TopicKey + " scored " + best.Score.ToString("0.##") +
                " over silence " + decision.SilenceScore.ToString("0.##");
            return decision;
        }

        private static double ContextualSourceBonus(AmbientSeedCandidate candidate, double preference)
        {
            if (candidate == null || preference <= 0.0) return 0.0;
            string topic = (candidate.TopicKey ?? string.Empty).ToLowerInvariant();
            string source = (candidate.FactSource ?? string.Empty).ToLowerInvariant();
            double weight = 0.0;
            if (topic.StartsWith("callback_")) weight = 4.0;
            else if (topic.StartsWith("identity:")) weight = 3.5;
            else if (topic.StartsWith("recent_life:")) weight = 3.0;
            else if (topic.StartsWith("camp_") || source.IndexOf("campmaster") >= 0) weight = 2.8;
            else if (topic.StartsWith("ambient_chat:")) weight = 2.2;
            else if (topic.StartsWith("free_social_")) weight = 1.4;
            return weight * Math.Min(1.0, preference);
        }

        private static bool Beats(AmbientSeedScore candidate, AmbientSeedScore incumbent)
        {
            if (candidate.Score != incumbent.Score) return candidate.Score > incumbent.Score;
            int byTopic = string.Compare(candidate.TopicKey, incumbent.TopicKey, StringComparison.Ordinal);
            if (byTopic != 0) return byTopic < 0;
            return string.Compare(candidate.Speaker, incumbent.Speaker, StringComparison.Ordinal) < 0;
        }

        private static AmbientSeedScore Excluded(string topicKey, string source, string reason)
        {
            return new AmbientSeedScore { TopicKey = topicKey, Source = PrivacySafeSource(source, topicKey), Speaker = string.Empty, ExcludedReason = reason };
        }

        private static string PrivacySafeSource(AmbientSeedCandidate candidate)
        {
            return candidate == null ? "ambient" : PrivacySafeSource(candidate.FactSource, candidate.TopicKey);
        }

        private static string PrivacySafeSource(string source, string topicKey)
        {
            if (string.IsNullOrWhiteSpace(source)) return "ambient";
            string lower = source.ToLowerInvariant();
            if (lower.IndexOf("pinned") >= 0) return "authored-pinned:" + SeedHash.Stable(topicKey ?? string.Empty).ToString("x8");
            if (lower.IndexOf("shared") >= 0) return "shared-memory:" + SeedHash.Stable(topicKey ?? string.Empty).ToString("x8");
            if (lower.IndexOf("identity") >= 0) return "authored-identity:" + SeedHash.Stable(topicKey ?? string.Empty).ToString("x8");
            if (lower.IndexOf("competitive") >= 0 || lower.IndexOf("pvp") >= 0 || lower.IndexOf("duel") >= 0 || lower.IndexOf("nemesis") >= 0)
                return "verified-competitive:" + SeedHash.Stable(topicKey ?? string.Empty).ToString("x8");
            if (lower.IndexOf("memory") >= 0 || lower.IndexOf("outing") >= 0) return "verified-memory:" + SeedHash.Stable(topicKey ?? string.Empty).ToString("x8");
            if (lower.IndexOf("session") >= 0 || lower.IndexOf("telemetry") >= 0) return "verified-session";
            return source.Length <= 64 ? source : source.Substring(0, 64);
        }

        private static void Note(List<SeedScoreComponent> components, string name, double value)
        {
            if (components != null) components.Add(new SeedScoreComponent(name, value));
        }

        // Personality may nudge which existing subject a Sim prefers.  It may never create a fact,
        // a participant, or a history.  Only fields Deep Sims already reads are used, and the
        // thresholds match the existing PromptBuilder cues.
        internal static double PersonalityAffinity(SimSnapshot sim, string topicKey)
        {
            return PersonalityAffinity(sim, topicKey, 0.0);
        }

        // familiarity is a bounded 0..1 Sim-to-player tone reading (existing MemoryStore data). It
        // only nudges willingness to initiate social/tease subjects; it can never create a fact, a
        // participant, or a shared history (see 8.4: relationship state is relevance/tone only).
        internal static double PersonalityAffinity(SimSnapshot sim, string topicKey, double familiarity)
        {
            if (sim == null || string.IsNullOrWhiteSpace(topicKey)) return 0.0;
            double affinity = 0.0;
            switch (topicKey.ToLowerInvariant())
            {
                case "light_tease":
                    if (sim.Rival) affinity += 3.0;
                    affinity += Math.Max(0.0, Math.Min(1.0, familiarity)) * 2.0;
                    break;
                case "other_sim_preference":
                    affinity += Math.Max(0.0, Math.Min(1.0, familiarity)) * 2.0;
                    break;
                case "class_opinion":
                    if (sim.Rival) affinity += 2.0;
                    break;
                case "gear_aesthetics":
                    if (sim.GearChase >= 60) affinity += 3.0;
                    if (sim.Greed >= 60) affinity += 2.0;
                    break;
                case "recovery":
                    if (sim.Patience >= 60) affinity += 2.0;
                    else if (sim.Patience > 0 && sim.Patience <= 35) affinity -= 2.0;
                    break;
                case "pace_preference":
                case "future_activity":
                    if (sim.Patience > 0 && sim.Patience <= 35) affinity += 2.0;
                    break;
            }
            return Math.Max(-4.0, Math.Min(6.0, affinity));
        }

        private static double Jitter(long opportunityId, string topicKey, string speaker)
        {
            int hash = SeedHash.Stable(opportunityId.ToString() + "|" + topicKey + "|" + speaker);
            return (hash % 13) - 6.0;
        }
    }

    // Player party chat is conversational knowledge (HEARD), never a verified fact. A recent topic
    // may still be worth picking back up, but only for Sims who were actually present when the
    // player said it, and it never carries a FactSource, so the selector cannot award it the
    // verified-fact bonus.
    internal static class PlayerTopicClassifier
    {
        internal const string Loot = "player_topic:loot";
        internal const string Zone = "player_topic:zone";
        internal const string Guild = "player_topic:guild";
        internal const string Duel = "player_topic:duel";
        internal const string FutureActivity = "player_topic:future_activity";
        internal const string StudyMagic = "player_topic:study_magic";
        internal const string FaithOrder = "player_topic:faith_order";
        internal const string WorkLife = "player_topic:work_life";

        // Deliberately narrow keyword lists in the same conservative style as the rest of the social
        // director (see SocialDirector.LooksTrivial/LooksLikeGreeting). Ambiguous or unmatched text
        // returns null rather than guessing a topic.
        internal static string Classify(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string padded = " " + SocialBudget.NormalizeSemantic(text) + " ";

            if (Contains(padded, " loot ", " gear ", " item ", " drop ", " dropped ", " equipment "))
                return Loot;
            if (Contains(padded, " zone ", " area ", " map ", " place we ", " next zone "))
                return Zone;
            if (Contains(padded, " guild "))
                return Guild;
            if (Contains(padded, " duel ", " spar ", " practice fight "))
                return Duel;
            if (Contains(padded, " what next ", " next adventure ", " where next ", " what should we do next "))
                return FutureActivity;
            if (Contains(padded, " spell ", " spells ", " spellcraft ", " mage academy ", " magic study ", " studying magic "))
                return StudyMagic;
            if (Contains(padded, " temple ", " faith ", " holy order ", " religious order ", " our order "))
                return FaithOrder;
            if (Contains(padded, " job ", " college ", " school after ", " graduation ", " graduate ", " work after ", " at work "))
                return WorkLife;
            return null;
        }

        private static bool Contains(string padded, params string[] phrases)
        {
            for (int i = 0; i < phrases.Length; i++)
                if (padded.IndexOf(phrases[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }
    }

    // Bounded, transient record of recent player party-chat topics. Nothing here is persisted or
    // treated as fact; a candidate built from it carries no FactSource and expires quickly.
    internal sealed class PlayerTopicTracker
    {
        private sealed class Entry
        {
            internal string TopicKey;
            internal string SourceText;
            internal List<string> EligibleSpeakerNames;
            internal DateTime ExpiresUtc;
        }

        private const int MaxEntries = 6;
        private readonly double _ttlSeconds;
        private readonly object _lock = new object();
        private readonly List<Entry> _entries = new List<Entry>();

        internal PlayerTopicTracker(double ttlSeconds) { _ttlSeconds = Math.Max(30.0, ttlSeconds); }

        // presentSpeakers is captured now, not re-resolved later: a Sim who joins afterward must not
        // become eligible to reference something said before they were present.
        internal void NotePartyMessage(string message, IEnumerable<string> presentSpeakers, DateTime now)
        {
            string topicKey = PlayerTopicClassifier.Classify(message);
            if (topicKey == null) return;
            List<string> present = new List<string>();
            if (presentSpeakers != null)
                foreach (string name in presentSpeakers)
                    if (!string.IsNullOrWhiteSpace(name) && !present.Contains(name)) present.Add(name);
            if (present.Count == 0) return;

            lock (_lock)
            {
                _entries.Add(new Entry
                {
                    TopicKey = topicKey,
                    SourceText = message.Trim(),
                    EligibleSpeakerNames = present,
                    ExpiresUtc = now.AddSeconds(_ttlSeconds)
                });
                while (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            }
        }

        internal List<AmbientSeedCandidate> BuildCandidates(DateTime now)
        {
            List<AmbientSeedCandidate> seeds = new List<AmbientSeedCandidate>();
            lock (_lock)
            {
                _entries.RemoveAll(delegate(Entry e) { return now > e.ExpiresUtc; });
                for (int i = 0; i < _entries.Count; i++)
                {
                    Entry e = _entries[i];
                    string truncated = e.SourceText.Length > 90 ? e.SourceText.Substring(0, 90).TrimEnd() + "..." : e.SourceText;
                    seeds.Add(new AmbientSeedCandidate(e.TopicKey, "player_topic",
                        "the player recently brought this up in party chat (unverified, HEARD only): \"" +
                        truncated + "\". You may pick the thread back up without treating it as a confirmed fact.",
                        20.0, null, null, 0, 0.0, now, e.ExpiresUtc, e.EligibleSpeakerNames));
                }
            }
            return seeds;
        }

        // Relevance-only context for authored identity/memory candidate admission. This is transient
        // HEARD material: it can make an already-authored fact topical, but it never becomes that
        // fact's provenance and is never persisted. Keep it tightly bounded and internal.
        internal string BuildRelevanceContext(DateTime now)
        {
            lock (_lock)
            {
                _entries.RemoveAll(delegate(Entry e) { return now > e.ExpiresUtc; });
                StringBuilder sb = new StringBuilder();
                int start = Math.Max(0, _entries.Count - 3);
                for (int i = start; i < _entries.Count; i++)
                {
                    Entry e = _entries[i];
                    if (e == null) continue;
                    if (!string.IsNullOrWhiteSpace(e.TopicKey)) sb.Append(e.TopicKey).Append(' ');
                    if (!string.IsNullOrWhiteSpace(e.SourceText))
                    {
                        string text = e.SourceText.Length > 120 ? e.SourceText.Substring(0, 120) : e.SourceText;
                        sb.Append(text).Append(' ');
                    }
                    if (sb.Length >= 360) break;
                }
                return sb.Length > 360 ? sb.ToString(0, 360).Trim() : sb.ToString().Trim();
            }
        }

        internal void Clear() { lock (_lock) _entries.Clear(); }
    }

    internal static class SeedHash
    {
        internal static int Stable(string value)
        {
            unchecked
            {
                int hash = 17;
                string text = value ?? string.Empty;
                for (int i = 0; i < text.Length; i++) hash = hash * 31 + text[i];
                return hash == int.MinValue ? 0 : Math.Abs(hash);
            }
        }
    }

    // An authoritative resource reading supplied by a caller that can prove where it came from.
    // Deep Sims does NOT currently have a verified mana source: SimSnapshot exposes HP only, and
    // guessing a reflected field would manufacture a fact.  The producer below therefore exists to
    // keep the selector honest and testable, and stays unwired until a real reader is established.
    internal sealed class AuthoritativeResourceReading
    {
        internal string SimName = string.Empty;
        internal string ResourceLabel = string.Empty;
        internal float Current;
        internal float Max;
        internal string Source = string.Empty;
        internal DateTime ObservedUtc;
    }

    // Optional sibling integrations may report the same underlying PvP/Nemesis episode through
    // two read-only event contracts. Collapse only terminal competitive copies; ordinary events and
    // distinct rematches remain independent. This tracker is process-local, bounded, and never writes
    // sibling state.
    internal static class CompetitiveEventCorrelation
    {
        private sealed class SeenEpisode { internal string Source; internal DateTime Utc; }
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, SeenEpisode> Seen = new Dictionary<string, SeenEpisode>(StringComparer.OrdinalIgnoreCase);
        private const int MaxSeen = 64;
        private const double CrossSourceWindowSeconds = 30.0;

        internal static bool TryAcceptTerminal(string source, string matchId, string opponent, string zone, DateTime now, out string correlationKey)
        {
            string idKey = string.IsNullOrWhiteSpace(matchId) ? string.Empty : "id:" + NormalizeCorrelation(matchId);
            string actorKey = "actor:" + NormalizeCorrelation(opponent) + "|zone:" + NormalizeCorrelation(zone);
            correlationKey = idKey.Length > 0 ? idKey : actorKey;
            lock (Gate)
            {
                Prune(now);
                SeenEpisode prior;
                if (idKey.Length > 0 && Seen.TryGetValue(idKey, out prior) && IsDuplicate(prior, source, now)) return false;
                if (Seen.TryGetValue(actorKey, out prior) && IsDuplicate(prior, source, now)) return false;
                SeenEpisode entry = new SeenEpisode { Source = source ?? string.Empty, Utc = now };
                if (idKey.Length > 0) Seen[idKey] = entry;
                Seen[actorKey] = entry;
                while (Seen.Count > MaxSeen) RemoveOldest();
                return true;
            }
        }

        internal static void ResetForTests() { lock (Gate) Seen.Clear(); }

        private static bool IsDuplicate(SeenEpisode prior, string source, DateTime now)
        {
            if (prior == null) return false;
            return !string.Equals(prior.Source, source ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs((now - prior.Utc).TotalSeconds) <= CrossSourceWindowSeconds;
        }

        private static void Prune(DateTime now)
        {
            List<string> remove = null;
            foreach (KeyValuePair<string, SeenEpisode> pair in Seen)
                if (pair.Value == null || Math.Abs((now - pair.Value.Utc).TotalSeconds) > 300.0)
                    (remove ?? (remove = new List<string>())).Add(pair.Key);
            if (remove != null) for (int i = 0; i < remove.Count; i++) Seen.Remove(remove[i]);
        }

        private static void RemoveOldest()
        {
            string key = null; DateTime oldest = DateTime.MaxValue;
            foreach (KeyValuePair<string, SeenEpisode> pair in Seen)
                if (pair.Value != null && pair.Value.Utc < oldest) { oldest = pair.Value.Utc; key = pair.Key; }
            if (key != null) Seen.Remove(key); else Seen.Clear();
        }

        private static string NormalizeCorrelation(string value)
        {
            return Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), @"[^a-z0-9_-]+", "_");
        }
    }

    internal static class AmbientSeedProducers
    {
        internal const double LowResourceRatio = 0.35;
        private const double LowResourceLifetimeSeconds = 45.0;

        internal static List<AmbientSeedCandidate> BuildDowntimeCandidates(SocialContextMode mode,
            string verifiedSessionFact, string sessionFactSource, DateTime now)
        {
            List<AmbientSeedCandidate> candidates = new List<AmbientSeedCandidate>();
            // Perspective selects WHICH subject table is offered. It does not add candidates, change
            // scores, or touch the silence threshold, so opportunity frequency is unaffected.
            AmbientSeedDefinition[] table = SocialPerspectiveState.RoleplayActive
                ? AmbientTopics.RoleplayDowntime
                : AmbientTopics.Downtime;
            for (int i = 0; i < table.Length; i++)
                candidates.Add(new AmbientSeedCandidate(table[i], now));

            // Faction subjects require verified exposure supplied by the caller. Without it they are
            // simply absent, so a faction is never discussed merely because it exists in the world.
            if (SocialPerspectiveState.RoleplayActive && RoleplayFactionContext.HasExposedFaction)
            {
                candidates.Add(new AmbientSeedCandidate(AmbientTopics.RoleplayFactionOpinion, now));
                candidates.Add(new AmbientSeedCandidate(AmbientTopics.RoleplayFactionUncertainty, now));
            }

            // Class cultural interest is one ordinary low-weight candidate among the RP subjects.
            if (SocialPerspectiveState.RoleplayActive && RoleplayClassContext.AnyAffinityPresent)
                candidates.Add(new AmbientSeedCandidate(AmbientTopics.RoleplayClassInterest, now));

            // Camp already means the party has stopped.  Saying so is not a subject there.
            if (mode != SocialContextMode.Camp)
                candidates.Add(new AmbientSeedCandidate(AmbientTopics.Idle, now));

            if (!string.IsNullOrWhiteSpace(verifiedSessionFact) && !string.IsNullOrWhiteSpace(sessionFactSource))
                candidates.Add(new AmbientSeedCandidate(AmbientTopics.Observation.TopicKey,
                    AmbientTopics.Observation.CooldownGroup, AmbientTopics.Observation.PromptHint,
                    AmbientTopics.Observation.BaseScore, verifiedSessionFact, sessionFactSource,
                    0, 0.0, now, DateTime.MaxValue, null));

            return candidates;
        }

        // Relax topic candidates. verifiedOutingFact/verifiedHistoryFact are optional and, when
        // supplied, are attributed exactly like session_observation/shared-memory facts: an unsourced
        // fact is excluded by the selector rather than narrated.
        internal static List<AmbientSeedCandidate> BuildRelaxCandidates(string verifiedOutingFact,
            string verifiedHistoryFact, DateTime now)
        {
            List<AmbientSeedCandidate> candidates = new List<AmbientSeedCandidate>();
            for (int i = 0; i < AmbientTopics.Relax.Length; i++)
            {
                AmbientSeedDefinition def = AmbientTopics.Relax[i];
                if (string.Equals(def.TopicKey, "verified_outing", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(verifiedOutingFact)) continue;
                    candidates.Add(new AmbientSeedCandidate(def.TopicKey, def.CooldownGroup, def.PromptHint,
                        def.BaseScore, verifiedOutingFact, "verified current-session outing telemetry",
                        0, 0.0, now, DateTime.MaxValue, null));
                    continue;
                }
                if (string.Equals(def.TopicKey, "verified_history", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(verifiedHistoryFact)) continue;
                    candidates.Add(new AmbientSeedCandidate(def.TopicKey, def.CooldownGroup, def.PromptHint,
                        def.BaseScore, verifiedHistoryFact, "verified shared-memory record",
                        0, 0.0, now, DateTime.MaxValue, null));
                    continue;
                }
                candidates.Add(new AmbientSeedCandidate(def, now));
            }
            return candidates;
        }

        // A Sim's own remembered outings/important-memory items are already compact verified text
        // (MemoryStore only writes them from confirmed telemetry). Provenance cannot be widened past
        // the owning Sim from a plain string, so eligibility is restricted to that one Sim rather than
        // being offered to the whole party.
        internal static List<AmbientSeedCandidate> BuildSharedMemoryCandidates(SimSnapshot sim, SimMemory memory,
            long opportunityId, DateTime now)
        {
            return BuildSharedMemoryCandidates(sim, memory, opportunityId, now, string.Empty);
        }

        internal static List<AmbientSeedCandidate> BuildSharedMemoryCandidates(SimSnapshot sim, SimMemory memory,
            long opportunityId, DateTime now, string contextText)
        {
            List<AmbientSeedCandidate> seeds = new List<AmbientSeedCandidate>();
            if (sim == null || string.IsNullOrWhiteSpace(sim.Name) || memory == null) return seeds;
            memory.Normalize();
            string ownerKey = sim.Name.Trim().ToLowerInvariant();
            string[] onlyOwner = new string[] { sim.Name };

            string importantMemory = PickBounded(memory.ImportantMemories, opportunityId, 4);
            if (!string.IsNullOrWhiteSpace(importantMemory))
                seeds.Add(new AmbientSeedCandidate(
                    "memory:" + ownerKey + ":" + SeedHash.Stable(importantMemory).ToString("x"), "memory",
                    "briefly bring up this specific remembered moment, exactly as stated, without adding new detail",
                    30.0, importantMemory, "verified important-memory record",
                    45, 0.0, now, DateTime.MaxValue, onlyOwner));

            string outingSummary = VerifiedOutingHistoryPolicy.SanitizeForPrompt(
                PickBounded(memory.OutingSummaries, opportunityId + 1, 4));
            if (!string.IsNullOrWhiteSpace(outingSummary))
                seeds.Add(new AmbientSeedCandidate(
                    "outing:" + ownerKey + ":" + SeedHash.Stable(outingSummary).ToString("x"), "memory",
                    "briefly recall this past outing summary without adding a new detail",
                    28.0, outingSummary, "verified outing-memory record",
                    40, 0.0, now, DateTime.MaxValue, onlyOwner));

            AddStructuredMemorySeeds(seeds, sim, memory.AuthoredIdentity == null ? null : memory.AuthoredIdentity.PinnedMemories,
                "pinned", 34.0, contextText, true, now);
            AddStructuredMemorySeeds(seeds, sim, memory.AuthoredIdentity == null ? null : memory.AuthoredIdentity.SharedHistory,
                "shared", 32.0, contextText, false, now);
            AddStructuredMemorySeeds(seeds, sim, memory.StructuredMemories,
                "learned", 29.0, contextText, false, now);
            AddRecentCompetitiveEventSeed(seeds, sim, memory, opportunityId, now);
            return seeds;
        }

        internal static List<AmbientSeedCandidate> BuildIdentityCandidates(SimSnapshot sim, SimMemory memory,
            string contextText, bool roleplay, DateTime now)
        {
            List<AmbientSeedCandidate> seeds = new List<AmbientSeedCandidate>();
            if (sim == null || string.IsNullOrWhiteSpace(sim.Name) || memory == null || memory.AuthoredIdentity == null) return seeds;
            memory.AuthoredIdentity.Normalize();
            AuthoredIdentityProfile authored = memory.AuthoredIdentity;
            string[] onlyOwner = new string[] { sim.Name };

            if (!string.IsNullOrWhiteSpace(authored.ErenshorPersona) && IdentityContextPolicy.IsRelevant(authored.ErenshorPersona, contextText))
                seeds.Add(new AmbientSeedCandidate("identity:persona:" + sim.Name.ToLowerInvariant(), "identity",
                    "use this relevant authored Erenshor identity as the subject; reveal only the part relevant now, not a biography dump",
                    35.0, IdentitySchema.Bound(authored.ErenshorPersona, 420), "authored identity erenshorPersona", 65, 0.0, now, DateTime.MaxValue, onlyOwner));

            // The personal/modern layer is only conversational material in MMO perspective.
            if (!roleplay && !string.IsNullOrWhiteSpace(authored.PersonalBackground) && IdentityContextPolicy.IsRelevant(authored.PersonalBackground, contextText))
                seeds.Add(new AmbientSeedCandidate("identity:background:" + sim.Name.ToLowerInvariant(), "identity",
                    "use only the relevant part of this authored personal background; do not dump biography",
                    34.0, IdentitySchema.Bound(authored.PersonalBackground, 360), "authored identity personalBackground", 60, 0.0, now, DateTime.MaxValue, onlyOwner));

            if (!string.IsNullOrWhiteSpace(authored.RelationshipToPlayer) && IdentityContextPolicy.IsRelevant(authored.RelationshipToPlayer, contextText))
                seeds.Add(new AmbientSeedCandidate("identity:relationship:" + sim.Name.ToLowerInvariant(), "identity",
                    "briefly express the relevant authored relationship without inventing shared events",
                    33.0, IdentitySchema.Bound(authored.RelationshipToPlayer, 260), "authored identity relationship", 60, 0.0, now, DateTime.MaxValue, onlyOwner));
            return seeds;
        }

        internal static AmbientSeedCandidate BuildRecentLifeCandidate(SimSnapshot sim, SimMemory memory, DateTime now)
        {
            if (sim == null || string.IsNullOrWhiteSpace(sim.Name) || memory == null || memory.StructuredMemories == null) return null;
            for (int i = memory.StructuredMemories.Count - 1; i >= 0; i--)
            {
                StructuredMemoryRecord record = memory.StructuredMemories[i];
                if (record == null || string.IsNullOrWhiteSpace(record.Text)) continue;
                if (!string.Equals(record.Source, RecentLifePolicy.Source, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(record.SourceSystem, RecentLifePolicy.SourceSystem, StringComparison.OrdinalIgnoreCase)) continue;
                DateTime observed;
                if (!DateTime.TryParse(record.Utc, out observed)) observed = now;
                return new AmbientSeedCandidate("recent_life:" + sim.Name.ToLowerInvariant() + ":" +
                    SeedHash.Stable(record.Id ?? record.Text).ToString("x8"), "recent_life",
                    "optionally mention this simulated recent-life detail in first person, casually and without adding facts; silence is fine",
                    27.0, IdentitySchema.Bound(record.Text, 420), "simulated recent life", 22, 0.0,
                    observed.ToUniversalTime(), DateTime.MaxValue, new string[] { sim.Name });
            }
            return null;
        }

        internal static AmbientSeedCandidate BuildAmbientChatCandidate(IList<RecentSocialLine> recent, DateTime now)
        {
            if (recent == null) return null;
            for (int i = recent.Count - 1; i >= 0; i--)
            {
                RecentSocialLine line = recent[i];
                if (line == null || !line.Public || string.IsNullOrWhiteSpace(line.Text)) continue;
                if (line.Channel != RecentChatChannel.Say && line.Channel != RecentChatChannel.Shout) continue;
                if ((now - line.Utc).TotalSeconds > 90.0) continue;
                string heard = (string.IsNullOrWhiteSpace(line.Speaker) ? "Someone" : line.Speaker) +
                    " was heard in " + line.Channel.ToString().ToLowerInvariant() + " chat saying: " + line.Text;
                return new AmbientSeedCandidate("ambient_chat:" + SeedHash.Stable(heard).ToString("x8"),
                    "ambient_chat", "privately discuss the HEARD public-chat line if it is interesting; never reply publicly and never treat its claims as verified world state",
                    29.0, IdentitySchema.Bound(heard, 360), "HEARD ambient chat (unverified content)", 20, 0.15,
                    line.Utc, line.Utc.AddSeconds(90.0), null);
            }
            return null;
        }

        private static void AddStructuredMemorySeeds(List<AmbientSeedCandidate> seeds, SimSnapshot owner,
            IList<StructuredMemoryRecord> records, string kind, double baseScore, string contextText, bool requireRelevance, DateTime now)
        {
            if (records == null || seeds == null || owner == null) return;
            int start = Math.Max(0, records.Count - 8);
            for (int i = records.Count - 1; i >= start; i--)
            {
                StructuredMemoryRecord record = records[i];
                if (record == null || !MemoryKnowledgePolicy.CanUse(record, owner)) continue;
                bool relevant = StructuredRecordRelevant(record, contextText);
                if ((requireRelevance || record.Pinned) && !relevant) continue;
                if (!relevant && record.Importance < 65) continue;

                List<string> known = EligibleKnownSpeakers(record, owner);
                if (known.Count == 0) continue;
                string id = !string.IsNullOrWhiteSpace(record.Id) ? record.Id : SeedHash.Stable(record.Text).ToString("x8");
                string source = record.Pinned || string.Equals(kind, "pinned", StringComparison.OrdinalIgnoreCase)
                    ? "authored pinned memory" : (record.Authored ? "authored shared memory" : "verified learned memory");
                seeds.Add(new AmbientSeedCandidate("memory:" + kind + ":" + id, "memory",
                    "briefly reference this specific known memory because it is relevant now; do not add facts or claim an emotion as verified",
                    baseScore, IdentitySchema.Bound(record.Text, 520), source, Math.Max(45, record.Importance), 0.0, now, DateTime.MaxValue, known));
            }
        }

        private static List<string> EligibleKnownSpeakers(StructuredMemoryRecord record, SimSnapshot owner)
        {
            List<string> result = new List<string>();
            if (record != null && record.KnownBy != null && record.KnownBy.Count > 0)
            {
                for (int i = 0; i < record.KnownBy.Count; i++)
                {
                    string value = record.KnownBy[i];
                    if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "player", StringComparison.OrdinalIgnoreCase)) continue;
                    bool exists = false;
                    for (int j = 0; j < result.Count; j++) if (string.Equals(result[j], value, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                    if (!exists) result.Add(value.Trim());
                }
            }
            else if (record != null && !record.Authored && owner != null && !string.IsNullOrWhiteSpace(owner.Name)) result.Add(owner.Name);
            return result;
        }

        private static bool StructuredRecordRelevant(StructuredMemoryRecord record, string contextText)
        {
            if (record == null || string.IsNullOrWhiteSpace(contextText)) return false;
            if (!string.IsNullOrWhiteSpace(record.Subject) && IdentityContextPolicy.IsRelevant(record.Subject, contextText)) return true;
            if (record.Topics != null)
                for (int i = 0; i < record.Topics.Count; i++)
                    if (!string.IsNullOrWhiteSpace(record.Topics[i]) && IdentityContextPolicy.IsRelevant(record.Topics[i], contextText)) return true;
            return IdentityContextPolicy.IsRelevant(record.Text, contextText);
        }

        private static void AddRecentCompetitiveEventSeed(List<AmbientSeedCandidate> seeds, SimSnapshot sim, SimMemory memory,
            long opportunityId, DateTime now)
        {
            if (memory == null || memory.RecentEvents == null || seeds == null) return;
            int start = Math.Max(0, memory.RecentEvents.Count - 10);
            for (int i = memory.RecentEvents.Count - 1; i >= start; i--)
            {
                MemoryEvent evt = memory.RecentEvents[i];
                if (evt == null || !IsCompetitiveEventType(evt.type) || string.IsNullOrWhiteSpace(evt.text)) continue;
                DateTime utc;
                if (!DateTime.TryParse(evt.utc, out utc)) continue;
                if (Math.Abs((now.ToUniversalTime() - utc.ToUniversalTime()).TotalHours) > 2.0) continue;
                string key = "competitive:" + SeedHash.Stable((evt.type ?? string.Empty) + "|" + evt.text).ToString("x8");
                seeds.Add(new AmbientSeedCandidate(key, "memory",
                    "react to this one verified competitive episode; the event is fact, but any emotion or interpretation is only your present opinion",
                    42.0, IdentitySchema.Bound(evt.text, 420), "verified competitive event:" + (evt.type ?? "event"), Math.Max(70, evt.importance), 0.0,
                    utc.ToUniversalTime(), now.AddMinutes(15), new string[] { sim.Name }));
                return; // one factual episode per memory/speaker per opportunity
            }
        }

        private static bool IsCompetitiveEventType(string type)
        {
            string t = (type ?? string.Empty).ToLowerInvariant();
            return t.IndexOf("duel") >= 0 || t.IndexOf("pvp") >= 0 || t.IndexOf("nemesis") >= 0;
        }

        private static string PickBounded(List<string> items, long opportunityId, int maxRecent)
        {
            if (items == null || items.Count == 0) return null;
            int count = Math.Min(maxRecent, items.Count);
            int start = items.Count - count;
            int index = start + (int)((opportunityId < 0 ? -opportunityId : opportunityId) % count);
            string value = items[index];
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        // Returns false whenever the reading is missing, unsourced, or already recovered, so a Sim
        // can never talk about someone being low on a resource that is no longer low.
        internal static bool TryBuildLowResourceSeed(AuthoritativeResourceReading reading, DateTime now,
            out AmbientSeedCandidate seed)
        {
            seed = null;
            if (reading == null) return false;
            if (string.IsNullOrWhiteSpace(reading.SimName)) return false;
            if (string.IsNullOrWhiteSpace(reading.ResourceLabel)) return false;
            if (string.IsNullOrWhiteSpace(reading.Source)) return false;
            if (reading.Max <= 0f) return false;
            double ratio = reading.Current / (double)reading.Max;
            if (ratio < 0.0 || ratio > LowResourceRatio) return false;

            string label = reading.ResourceLabel.Trim().ToLowerInvariant();
            string topicKey = label + ":" + reading.SimName.Trim().ToLowerInvariant();
            string fact = reading.SimName + " is currently at " + Math.Round(ratio * 100.0) + "% " + label + ".";
            DateTime observed = reading.ObservedUtc == DateTime.MinValue ? now : reading.ObservedUtc;
            seed = new AmbientSeedCandidate(topicKey, "recovery",
                "mention the supplied verified resource state as a reason to pause; do not invent a role, cause, or plan",
                26.0, fact, reading.Source, 45, 0.0, observed,
                observed.AddSeconds(LowResourceLifetimeSeconds), null);
            return true;
        }
    }

    // Bounded ring of recent ambient evaluations for /dsseeds.  Diagnostics only; never a fact source.
    internal sealed class AmbientSeedDiagnostics
    {
        private const int MaxRecent = 24;
        private readonly object _lock = new object();
        private readonly List<AmbientSeedDecision> _recent = new List<AmbientSeedDecision>();
        private long _opportunities;
        private long _silenced;
        private long _emitted;

        internal long NextOpportunityId()
        {
            lock (_lock) { return ++_opportunities; }
        }

        internal void Record(AmbientSeedDecision decision)
        {
            if (decision == null) return;
            lock (_lock)
            {
                _recent.Add(decision);
                while (_recent.Count > MaxRecent) _recent.RemoveAt(0);
                if (decision.SilenceWon) _silenced++;
            }
        }

        internal void NoteEmitted(long opportunityId, string speaker)
        {
            lock (_lock)
            {
                _emitted++;
                for (int i = _recent.Count - 1; i >= 0; i--)
                {
                    if (_recent[i].OpportunityId != opportunityId) continue;
                    _recent[i].Outcome = "emitted";
                    if (!string.IsNullOrWhiteSpace(speaker)) _recent[i].SelectedSpeaker = speaker;
                    return;
                }
            }
        }

        internal void NoteOutcome(long opportunityId, string outcome)
        {
            if (string.IsNullOrWhiteSpace(outcome)) return;
            lock (_lock)
            {
                for (int i = _recent.Count - 1; i >= 0; i--)
                    if (_recent[i].OpportunityId == opportunityId) { _recent[i].Outcome = outcome; return; }
            }
        }

        internal string DescribeStatus()
        {
            lock (_lock)
            {
                return "opportunities=" + _opportunities + ", silence=" + _silenced +
                    ", emitted=" + _emitted + ", recorded=" + _recent.Count + "/" + MaxRecent;
            }
        }

        internal string DescribeRecent(int count)
        {
            lock (_lock)
            {
                if (_recent.Count == 0) return "[DeepSims Seeds] No ambient opportunities evaluated yet.";
                StringBuilder sb = new StringBuilder();
                sb.Append("[DeepSims Seeds] Recent ambient evaluations (newest first):");
                int shown = 0;
                for (int i = _recent.Count - 1; i >= 0 && shown < Math.Max(1, count); i--, shown++)
                    sb.AppendLine().Append(Format(_recent[i]));
                return sb.ToString();
            }
        }

        internal static string Format(AmbientSeedDecision d)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("#").Append(d.OpportunityId).Append(" ")
                .Append(d.Utc.ToLocalTime().ToString("HH:mm:ss"))
                .Append(" opportunity=ambient")
                .Append(" mode/context=").Append(d.Mode)
                .Append(" silenceScore=").Append(d.SilenceScore.ToString("0.#"))
                .Append(" selected=").Append(d.SilenceWon ? "SILENCE" : (d.SelectedTopicKey + "/" + d.SelectedSpeaker))
                .Append(" selectedSource=").Append(string.IsNullOrWhiteSpace(d.SelectedSource) ? "none" : d.SelectedSource)
                .Append(" selectedScore=").Append(d.SelectedScore.ToString("0.#"))
                .Append(" [").Append(d.Outcome).Append("]");
            int listed = 0;
            for (int i = 0; i < d.Candidates.Count && listed < 5; i++, listed++)
            {
                AmbientSeedScore s = d.Candidates[i];
                sb.AppendLine().Append("    key=").Append(s.TopicKey)
                    .Append(" source=").Append(string.IsNullOrWhiteSpace(s.Source) ? "ambient" : s.Source)
                    .Append(" speaker=").Append(string.IsNullOrWhiteSpace(s.Speaker) ? "none" : s.Speaker);
                if (s.Excluded) sb.Append(" score=excluded exclusion=").Append(s.ExcludedReason);
                else sb.Append(" score=").Append(s.Score.ToString("0.#")).Append(" exclusion=none");
            }
            if (!string.IsNullOrEmpty(d.Reason)) sb.AppendLine().Append("    reason: ").Append(d.Reason);
            return sb.ToString();
        }
    }
}
