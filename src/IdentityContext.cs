using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ErenshorDeepSims
{
    [Serializable]
    public class StructuredMemoryRecord
    {
        public string Id;
        public string MemoryType;
        public string Text;
        public string Subject;
        public int Importance;
        public List<string> Participants;
        public List<string> KnownBy;
        public List<string> Topics;
        public List<string> EmotionalTags;
        public string Utc;
        public string Source;
        // Social memory keeps verified evidence and per-Sim interpretation deliberately separate.
        // Text remains the factual/attributed evidence summary so legacy grounding code can never
        // certify an interpretation as a world fact.
        public string EpisodeId;
        public string SourceSystem;
        public string SourceCorrelationId;
        public string FactSummary;
        public string InterpretationSummary;
        public string CharacterScope;
        public string MemoryTier;
        public List<string> EvidenceIds;
        public string CallbackConcept;
        public bool InsideJoke;
        public int RecurrenceCount;
        public float HumorScore;
        public float ConflictScore;
        public string LastRecalledUtc;
        public string OwnerSimKey;
        public string OwnerSimName;
        public bool ExplicitPublic;
        public bool Authored;
        public bool Pinned;

        public void Normalize()
        {
            if (Id == null) Id = string.Empty;
            if (MemoryType == null) MemoryType = string.Empty;
            if (Text == null) Text = string.Empty;
            if (Subject == null) Subject = string.Empty;
            if (Participants == null) Participants = new List<string>();
            if (KnownBy == null) KnownBy = new List<string>();
            if (Topics == null) Topics = new List<string>();
            if (EmotionalTags == null) EmotionalTags = new List<string>();
            if (Utc == null) Utc = string.Empty;
            if (Source == null) Source = string.Empty;
            if (EpisodeId == null) EpisodeId = string.Empty;
            if (SourceSystem == null) SourceSystem = string.Empty;
            if (SourceCorrelationId == null) SourceCorrelationId = string.Empty;
            if (FactSummary == null) FactSummary = string.Empty;
            if (InterpretationSummary == null) InterpretationSummary = string.Empty;
            if (CharacterScope == null) CharacterScope = string.Empty;
            if (MemoryTier == null) MemoryTier = string.Empty;
            if (EvidenceIds == null) EvidenceIds = new List<string>();
            if (CallbackConcept == null) CallbackConcept = string.Empty;
            if (LastRecalledUtc == null) LastRecalledUtc = string.Empty;
            if (OwnerSimKey == null) OwnerSimKey = string.Empty;
            if (OwnerSimName == null) OwnerSimName = string.Empty;
            RecurrenceCount = Math.Max(0, Math.Min(1000, RecurrenceCount));
            HumorScore = Math.Max(0f, Math.Min(1f, HumorScore));
            ConflictScore = Math.Max(0f, Math.Min(1f, ConflictScore));
            Importance = Math.Max(0, Math.Min(100, Importance));
        }
    }

    [Serializable]
    public class AuthoredIdentityProfile
    {
        public string CorePersonality;
        public string PersonalBackground;
        public string ErenshorPersona;
        public string RelationshipToPlayer;
        public string LongTermWants;
        public string CaresAbout;
        public List<StructuredMemoryRecord> SharedHistory;
        public List<StructuredMemoryRecord> PinnedMemories;

        public void Normalize()
        {
            if (CorePersonality == null) CorePersonality = string.Empty;
            if (PersonalBackground == null) PersonalBackground = string.Empty;
            if (ErenshorPersona == null) ErenshorPersona = string.Empty;
            if (RelationshipToPlayer == null) RelationshipToPlayer = string.Empty;
            if (LongTermWants == null) LongTermWants = string.Empty;
            if (CaresAbout == null) CaresAbout = string.Empty;
            if (SharedHistory == null) SharedHistory = new List<StructuredMemoryRecord>();
            if (PinnedMemories == null) PinnedMemories = new List<StructuredMemoryRecord>();
            NormalizeRecords(SharedHistory);
            NormalizeRecords(PinnedMemories);
        }

        private static void NormalizeRecords(List<StructuredMemoryRecord> records)
        {
            for (int i = records.Count - 1; i >= 0; i--)
            {
                StructuredMemoryRecord record = records[i];
                if (record == null || string.IsNullOrWhiteSpace(record.Text)) records.RemoveAt(i);
                else record.Normalize();
            }
            // Authored records are explicit player data. Never silently prune them during migration/normalization;
            // prompt retrieval is bounded separately. The editor rejects new records at a clear hard limit.
        }
    }

    internal static class IdentitySchema
    {
        internal const int CurrentVersion = 3;

        internal static void Normalize(SimMemory memory)
        {
            if (memory == null) return;
            if (memory.AuthoredIdentity == null) memory.AuthoredIdentity = new AuthoredIdentityProfile();
            memory.AuthoredIdentity.Normalize();
            if (memory.StructuredMemories == null) memory.StructuredMemories = new List<StructuredMemoryRecord>();
            for (int i = memory.StructuredMemories.Count - 1; i >= 0; i--)
            {
                StructuredMemoryRecord record = memory.StructuredMemories[i];
                if (record == null || string.IsNullOrWhiteSpace(record.Text)) memory.StructuredMemories.RemoveAt(i);
                else
                {
                    record.Normalize();
                    // Legacy learned records with empty KnownBy stay private to their owning Sim file.
                    // Migration records ownership explicitly rather than interpreting empty KnownBy as public.
                    if (string.IsNullOrWhiteSpace(record.OwnerSimKey)) record.OwnerSimKey = memory.SimKey ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(record.OwnerSimName)) record.OwnerSimName = memory.Name ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(record.FactSummary)) record.FactSummary = record.Text ?? string.Empty;
                }
            }
            if (memory.StructuredMemories.Count > 48)
                memory.StructuredMemories.RemoveRange(0, memory.StructuredMemories.Count - 48);
            if (memory.IdentityDataVersion < CurrentVersion) memory.IdentityDataVersion = CurrentVersion;
        }

        internal static StructuredMemoryRecord Create(string type, string text, int importance, bool authored, bool pinned,
            string simKey, string simName, string source)
        {
            StructuredMemoryRecord record = new StructuredMemoryRecord();
            record.Id = StableId(type, text, DateTime.UtcNow.Ticks.ToString());
            record.MemoryType = string.IsNullOrWhiteSpace(type) ? "episodic" : type.Trim();
            record.Text = Bound(text, 700);
            record.Subject = string.Empty;
            record.Importance = Math.Max(0, Math.Min(100, importance));
            record.Participants = new List<string>();
            record.KnownBy = new List<string>();
            record.Topics = MemoryTopicPolicy.ExtractTopics(text);
            record.EmotionalTags = MemoryTopicPolicy.ExtractEmotionalTags(text);
            record.Utc = DateTime.UtcNow.ToString("o");
            record.Source = source ?? string.Empty;
            record.FactSummary = record.Text;
            record.OwnerSimKey = simKey ?? string.Empty;
            record.OwnerSimName = simName ?? string.Empty;
            record.MemoryTier = pinned ? SocialMemoryPolicy.TierPinned : importance >= 70 ? SocialMemoryPolicy.TierSignificant : SocialMemoryPolicy.TierRecent;
            record.Authored = authored;
            record.Pinned = pinned;
            AddIdentity(record.Participants, "player");
            AddIdentity(record.KnownBy, "player");
            AddIdentity(record.Participants, simKey);
            AddIdentity(record.Participants, simName);
            AddIdentity(record.KnownBy, simKey);
            AddIdentity(record.KnownBy, simName);
            return record;
        }

        internal static AuthoredIdentityProfile CloneProfile(AuthoredIdentityProfile source)
        {
            AuthoredIdentityProfile copy = new AuthoredIdentityProfile();
            if (source == null) { copy.Normalize(); return copy; }
            copy.CorePersonality = source.CorePersonality;
            copy.PersonalBackground = source.PersonalBackground;
            copy.ErenshorPersona = source.ErenshorPersona;
            copy.RelationshipToPlayer = source.RelationshipToPlayer;
            copy.LongTermWants = source.LongTermWants;
            copy.CaresAbout = source.CaresAbout;
            copy.SharedHistory = CloneRecords(source.SharedHistory);
            copy.PinnedMemories = CloneRecords(source.PinnedMemories);
            copy.Normalize();
            return copy;
        }

        internal static List<StructuredMemoryRecord> CloneRecords(List<StructuredMemoryRecord> source)
        {
            List<StructuredMemoryRecord> result = new List<StructuredMemoryRecord>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                StructuredMemoryRecord r = source[i];
                if (r == null) continue;
                r.Normalize();
                result.Add(new StructuredMemoryRecord
                {
                    Id = r.Id, MemoryType = r.MemoryType, Text = r.Text, Subject = r.Subject, Importance = r.Importance,
                    Participants = new List<string>(r.Participants), KnownBy = new List<string>(r.KnownBy),
                    Topics = new List<string>(r.Topics), EmotionalTags = new List<string>(r.EmotionalTags),
                    Utc = r.Utc, Source = r.Source, EpisodeId = r.EpisodeId, SourceSystem = r.SourceSystem,
                    SourceCorrelationId = r.SourceCorrelationId, FactSummary = r.FactSummary, InterpretationSummary = r.InterpretationSummary,
                    CharacterScope = r.CharacterScope, MemoryTier = r.MemoryTier, EvidenceIds = new List<string>(r.EvidenceIds),
                    CallbackConcept = r.CallbackConcept, InsideJoke = r.InsideJoke, RecurrenceCount = r.RecurrenceCount, HumorScore = r.HumorScore,
                    ConflictScore = r.ConflictScore, LastRecalledUtc = r.LastRecalledUtc, OwnerSimKey = r.OwnerSimKey, OwnerSimName = r.OwnerSimName,
                    ExplicitPublic = r.ExplicitPublic, Authored = r.Authored, Pinned = r.Pinned
                });
            }
            return result;
        }

        internal static string Bound(string value, int max)
        {
            string clean = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (clean.Length > max) clean = clean.Substring(0, max).TrimEnd();
            return clean;
        }

        private static string StableId(string a, string b, string c)
        {
            unchecked
            {
                uint h = 2166136261;
                string value = (a ?? string.Empty) + "|" + (b ?? string.Empty) + "|" + (c ?? string.Empty);
                for (int i = 0; i < value.Length; i++) { h ^= value[i]; h *= 16777619; }
                return h.ToString("x8");
            }
        }

        private static void AddIdentity(List<string> list, string value)
        {
            if (list == null || string.IsNullOrWhiteSpace(value)) return;
            for (int i = 0; i < list.Count; i++) if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase)) return;
            list.Add(value.Trim());
        }
    }

    internal static class MemoryTopicPolicy
    {
        internal static List<string> ExtractTopics(string text)
        {
            List<string> topics = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] words = Regex.Split((text ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9+']+");
            HashSet<string> stop = new HashSet<string>(new string[] { "the", "and", "that", "this", "with", "from", "have", "were", "been", "they", "their", "player", "sim", "about", "when", "where", "into", "once", "same" }, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < words.Length && topics.Count < 8; i++)
            {
                string w = words[i].Trim();
                if (w.Length < 3 || stop.Contains(w) || !seen.Add(w)) continue;
                topics.Add(w);
            }
            return topics;
        }

        internal static List<string> ExtractEmotionalTags(string text)
        {
            string lower = (text ?? string.Empty).ToLowerInvariant();
            List<string> tags = new List<string>();
            AddTag(tags, lower, "trust", @"\b(?:trust|trusted|promise|promised|loyal|friend)\w*\b");
            AddTag(tags, lower, "rivalry", @"\b(?:rival|competitive|competition|outdo)\w*\b");
            AddTag(tags, lower, "conflict", @"\b(?:argue|argued|angry|anger|fight over|betray)\w*\b");
            AddTag(tags, lower, "worry", @"\b(?:worry|worried|anxious|uncertain|afraid|fear)\w*\b");
            AddTag(tags, lower, "pride", @"\b(?:proud|pride|victory|won|triumphed)\w*\b");
            AddTag(tags, lower, "loss", @"\b(?:died|death|lost|loss|grief|wipe)\w*\b");
            if (tags.Count > 4) tags.RemoveRange(4, tags.Count - 4);
            return tags;
        }

        private static void AddTag(List<string> tags, string text, string tag, string pattern)
        {
            if (tags == null || tags.Count >= 4 || string.IsNullOrWhiteSpace(text)) return;
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase) && !tags.Contains(tag)) tags.Add(tag);
        }
    }

    internal static class MemoryKnowledgePolicy
    {
        internal static bool CanUse(StructuredMemoryRecord record, SimSnapshot owner)
        {
            if (record == null || owner == null || string.IsNullOrWhiteSpace(record.Text)) return false;
            record.Normalize();
            if (record.ExplicitPublic) return true;
            if (record.KnownBy == null || record.KnownBy.Count == 0)
            {
                // Empty knowledge is never public. Legacy per-Sim records are usable only when the
                // explicit owner written during schema normalization matches the current Sim.
                return (!string.IsNullOrWhiteSpace(record.OwnerSimKey) && !string.IsNullOrWhiteSpace(owner.Key) &&
                        string.Equals(record.OwnerSimKey, owner.Key, StringComparison.OrdinalIgnoreCase)) ||
                       (!string.IsNullOrWhiteSpace(record.OwnerSimName) && !string.IsNullOrWhiteSpace(owner.Name) &&
                        string.Equals(record.OwnerSimName, owner.Name, StringComparison.OrdinalIgnoreCase));
            }
            for (int i = 0; i < record.KnownBy.Count; i++)
            {
                string known = record.KnownBy[i] ?? string.Empty;
                if ((!string.IsNullOrWhiteSpace(owner.Key) && string.Equals(known, owner.Key, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(owner.Name) && string.Equals(known, owner.Name, StringComparison.OrdinalIgnoreCase))) return true;
            }
            return false;
        }
    }

    internal sealed class IdentityPromptContext
    {
        internal string CorePersonality = string.Empty;
        internal string PersonalBackground = string.Empty;
        internal string ErenshorPersona = string.Empty;
        internal string RelationshipToPlayer = string.Empty;
        internal string LongTermWants = string.Empty;
        internal string CaresAbout = string.Empty;
        internal string DefaultSummary = string.Empty;
        internal IdentityValueSource CorePersonalitySource = IdentityValueSource.None;
        internal IdentityValueSource PersonalBackgroundSource = IdentityValueSource.None;
        internal IdentityValueSource ErenshorPersonaSource = IdentityValueSource.None;
        internal IdentityValueSource RelationshipToPlayerSource = IdentityValueSource.None;
        internal IdentityValueSource LongTermWantsSource = IdentityValueSource.None;
        internal IdentityValueSource CaresAboutSource = IdentityValueSource.None;
        internal GeneratedPersonalityDimensions Dimensions = new GeneratedPersonalityDimensions();

        internal bool HasDefaultValues
        {
            get
            {
                return CorePersonalitySource == IdentityValueSource.DefaultTemplate ||
                    PersonalBackgroundSource == IdentityValueSource.DefaultTemplate ||
                    PersonalBackgroundSource == IdentityValueSource.PersistedGeneratedDefault ||
                    ErenshorPersonaSource == IdentityValueSource.DefaultTemplate ||
                    RelationshipToPlayerSource == IdentityValueSource.DefaultTemplate;
            }
        }
    }

    internal static class IdentityContextPolicy
    {
        internal static IdentityPromptContext Select(SimSnapshot sim, SimMemory memory, string topicText, bool roleplay)
        {
            IdentityPromptContext result = new IdentityPromptContext();
            DefaultIdentityProfile defaults = DefaultIdentityPolicy.Build(sim);
            result.DefaultSummary = defaults.Summary;
            result.Dimensions = defaults.Dimensions;

            AuthoredIdentityProfile authored = memory == null ? null : memory.AuthoredIdentity;
            if (authored != null) authored.Normalize();

            string authoredCore = authored == null ? string.Empty : IdentitySchema.Bound(authored.CorePersonality, 320);
            string authoredRelationship = authored == null ? string.Empty : IdentitySchema.Bound(authored.RelationshipToPlayer, 260);
            string authoredPersona = authored == null ? string.Empty : IdentitySchema.Bound(authored.ErenshorPersona, 420);
            // PersonalBackground is retained as the serialized authored field for backward
            // compatibility, but its 0.8.2 semantic meaning is the fictional SIMULATED PLAYER
            // BACKGROUND used in MMO perspective. Generated defaults are stored separately.
            string authoredBackground = authored == null ? string.Empty : IdentitySchema.Bound(authored.PersonalBackground, 420);
            string generatedBackground = memory == null ? string.Empty : IdentitySchema.Bound(memory.GeneratedSimulatedPlayerBackground, 420);
            string authoredWants = authored == null ? string.Empty : IdentitySchema.Bound(authored.LongTermWants, 320);
            string authoredCares = authored == null ? string.Empty : IdentitySchema.Bound(authored.CaresAbout, 320);
            string generatedWants = memory == null ? string.Empty : IdentitySchema.Bound(memory.GeneratedLongTermWants, 320);
            string generatedCares = memory == null ? string.Empty : IdentitySchema.Bound(memory.GeneratedCaresAbout, 320);
            result.LongTermWants = !string.IsNullOrWhiteSpace(authoredWants) ? authoredWants : generatedWants;
            result.LongTermWantsSource = !string.IsNullOrWhiteSpace(authoredWants) ? IdentityValueSource.AuthoredOverride :
                (!string.IsNullOrWhiteSpace(generatedWants) ? IdentityValueSource.PersistedGeneratedDefault : IdentityValueSource.None);
            result.CaresAbout = !string.IsNullOrWhiteSpace(authoredCares) ? authoredCares : generatedCares;
            result.CaresAboutSource = !string.IsNullOrWhiteSpace(authoredCares) ? IdentityValueSource.AuthoredOverride :
                (!string.IsNullOrWhiteSpace(generatedCares) ? IdentityValueSource.PersistedGeneratedDefault : IdentityValueSource.None);

            result.CorePersonality = !string.IsNullOrWhiteSpace(authoredCore) ? authoredCore : IdentitySchema.Bound(defaults.CorePersonality, 320);
            result.CorePersonalitySource = !string.IsNullOrWhiteSpace(authoredCore) ? IdentityValueSource.AuthoredOverride : IdentityValueSource.DefaultTemplate;
            result.RelationshipToPlayer = !string.IsNullOrWhiteSpace(authoredRelationship) ? authoredRelationship : IdentitySchema.Bound(defaults.RelationshipToPlayer, 260);
            result.RelationshipToPlayerSource = !string.IsNullOrWhiteSpace(authoredRelationship) ? IdentityValueSource.AuthoredOverride : IdentityValueSource.DefaultTemplate;

            string effectivePersona = !string.IsNullOrWhiteSpace(authoredPersona) ? authoredPersona : defaults.ErenshorPersona;
            IdentityValueSource personaSource = !string.IsNullOrWhiteSpace(authoredPersona) ? IdentityValueSource.AuthoredOverride : IdentityValueSource.DefaultTemplate;
            if (roleplay)
            {
                result.ErenshorPersona = IdentitySchema.Bound(effectivePersona, 420);
                result.ErenshorPersonaSource = personaSource;
            }
            else if (IsRelevant(effectivePersona, topicText) || Regex.IsMatch(topicText ?? string.Empty, @"\b(?:erenshor|persona|roleplay|order|academy|temple|mercenary|scholar|hunter)\b", RegexOptions.IgnoreCase))
            {
                result.ErenshorPersona = IdentitySchema.Bound(effectivePersona, 320);
                result.ErenshorPersonaSource = personaSource;
            }

            bool explicitBackground = SimulatedPlayerBackgroundPolicy.IsBackgroundQuestion(topicText);
            string effectiveBackground = !string.IsNullOrWhiteSpace(authoredBackground) ? authoredBackground : generatedBackground;
            IdentityValueSource backgroundSource = !string.IsNullOrWhiteSpace(authoredBackground)
                ? IdentityValueSource.AuthoredOverride
                : (!string.IsNullOrWhiteSpace(generatedBackground) ? IdentityValueSource.PersistedGeneratedDefault : IdentityValueSource.None);
            // Layer 3 is explicitly out-of-character MMO-player identity. Roleplay perspective fails
            // closed and never exposes it merely because a prompt says work/school/IRL. MMO mode may
            // use the stable persisted background when the player actually asks about that layer.
            if (!roleplay && !string.IsNullOrWhiteSpace(effectiveBackground) &&
                (explicitBackground || IsRelevant(effectiveBackground, topicText)))
            {
                result.PersonalBackground = IdentitySchema.Bound(effectiveBackground, 420);
                result.PersonalBackgroundSource = backgroundSource;
            }
            return result;
        }

        internal static bool IsRelevant(string source, string topicText)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(topicText)) return false;
            HashSet<string> sourceTokens = Tokens(source);
            HashSet<string> query = Tokens(topicText);
            int overlap = 0;
            foreach (string token in query) if (sourceTokens.Contains(token)) overlap++;
            return overlap >= 1;
        }

        private static HashSet<string> Tokens(string text)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] parts = Regex.Split((text ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9+']+");
            for (int i = 0; i < parts.Length; i++) if (parts[i].Length >= 4) set.Add(parts[i]);
            return set;
        }
    }

    internal static class PromptIdentityRenderer
    {
        internal static string Render(SimSnapshot sim, IdentityPromptContext identity, string playerName, bool roleplay, bool includePlayerRelationship = true)
        {
            if (identity == null) return string.Empty;
            string name = sim == null || string.IsNullOrWhiteSpace(sim.Name) ? "THIS CHARACTER" : sim.Name.Trim().ToUpperInvariant();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("WHO YOU ARE");
            sb.AppendLine("YOU ARE " + name + ".");
            if (identity.CorePersonalitySource == IdentityValueSource.AuthoredOverride && !string.IsNullOrWhiteSpace(identity.CorePersonality))
                sb.AppendLine("Your authored personality: " + IdentitySchema.Bound(identity.CorePersonality, 300));
            else AppendDimensions(sb, identity.Dimensions);

            if (!string.IsNullOrWhiteSpace(identity.ErenshorPersona))
            {
                if (identity.ErenshorPersonaSource == IdentityValueSource.AuthoredOverride)
                    sb.AppendLine("As an Erenshor adventurer: " + IdentitySchema.Bound(identity.ErenshorPersona, 320));
                else if (sim != null)
                    sb.AppendLine("As an Erenshor adventurer, you approach situations through your " +
                        SimContextReader.NormalizeClassName(sim.ClassName) + " perspective and your own preferences.");
            }
            if (includePlayerRelationship && !string.IsNullOrWhiteSpace(identity.RelationshipToPlayer))
            {
                string who = string.IsNullOrWhiteSpace(playerName) ? "the player" : playerName.Trim();
                sb.AppendLine("With " + who + ": " + (identity.RelationshipToPlayerSource == IdentityValueSource.AuthoredOverride
                    ? IdentitySchema.Bound(identity.RelationshipToPlayer, 240)
                    : RelationshipVoice(identity.Dimensions)));
            }
            if (!string.IsNullOrWhiteSpace(identity.CaresAbout))
                sb.AppendLine("You care about: " + IdentitySchema.Bound(identity.CaresAbout, 260));
            if (!string.IsNullOrWhiteSpace(identity.LongTermWants))
                sb.AppendLine("Over the long term: " + IdentitySchema.Bound(identity.LongTermWants, 260));
            if (!roleplay && !string.IsNullOrWhiteSpace(identity.PersonalBackground))
                sb.AppendLine("Your fictional life outside Erenshor: " + IdentitySchema.Bound(identity.PersonalBackground, 360));
            sb.AppendLine("Speak like a person in short MMO conversation. You may have preferences, joke, disagree mildly, be uncertain, decline a topic, or stay quiet. Do not act like an assistant or automatically agree.");
            return sb.ToString().Trim();
        }

        internal static string FactualBoundaries()
        {
            return "FACTUAL BOUNDARIES\nOnly state a location, event, possession, relationship, or accomplishment when current facts, authored canon, or supplied history supports it. Memories may be discussed, but a remembered person may be addressed only when CURRENT SOCIAL PRESENCE lists them.";
        }

        private static void AppendDimensions(StringBuilder sb, GeneratedPersonalityDimensions d)
        {
            d = d ?? new GeneratedPersonalityDimensions();
            sb.AppendLine("You are " + LowHigh(d.SocialEnergy, "quiet at first", "socially energetic") + ", " +
                LowHigh(d.Patience, "quick to show impatience", "patient with setbacks") + ", and " +
                LowHigh(d.Directness, "tactful and indirect", "conversationally direct") + ".");
            sb.AppendLine("Your humor is " + d.HumorStyle + ". You are " +
                LowHigh(d.Improvisation, "inclined to plan", "comfortable improvising") + " and " +
                LowHigh(d.RiskTolerance, "mildly cautious", "eager for calculated risks") + ".");
            sb.AppendLine("You are " + LowHigh(d.Competitiveness, "not very competitive", "competitive") + ", " +
                LowHigh(d.Curiosity, "selective about new topics", "genuinely curious") + ", and " +
                LowHigh(d.WillingnessToDisagree, "slow to push back", "comfortable disagreeing without hostility") + ".");
            sb.AppendLine("In groups you are " + LowHigh(d.GroupOrientation, "independent-minded", "group-oriented") + ". You naturally enjoy talking about " + d.PreferredInterests + ".");
        }

        private static string RelationshipVoice(GeneratedPersonalityDimensions d)
        {
            d = d ?? new GeneratedPersonalityDimensions();
            return d.WillingnessToDisagree >= 3
                ? "Be friendly in the ordinary way, but state your own view and disagree calmly when you mean it."
                : "Be naturally friendly without assuming closeness; you do not need to validate every opinion.";
        }

        private static string LowHigh(int value, string low, string high)
        {
            if (value <= 1) return low;
            if (value >= 3) return high;
            return "moderately " + (high ?? string.Empty).ToLowerInvariant();
        }
    }

    internal sealed class RetrievedMemoryCandidate
    {
        internal StructuredMemoryRecord Record;
        internal string Source;
        internal string Text;
        internal double Score;
        internal int MatchCount;
        internal int StableOrder;
    }

    internal static class StructuredMemoryRetrieval
    {
        internal static List<RelevantMemory> Select(SimMemory memory, SimSnapshot owner, string topicText, int limit)
        {
            List<RetrievedMemoryCandidate> candidates = new List<RetrievedMemoryCandidate>();
            if (memory == null || owner == null || limit <= 0) return new List<RelevantMemory>();
            IdentitySchema.Normalize(memory);
            HashSet<string> query = Tokens(topicText);
            bool recentLifeRecall = RecentLifePolicy.IsRecentLifeQuestion(topicText);
            int order = 0;
            AddRecords(candidates, memory.AuthoredIdentity.SharedHistory, "authored-shared-history", owner, query, false, ref order);
            AddRecords(candidates, memory.AuthoredIdentity.PinnedMemories, "authored-pinned", owner, query, false, ref order);
            AddRecords(candidates, memory.StructuredMemories, "learned", owner, query, recentLifeRecall, ref order);

            // Preserve all old data. Legacy records remain candidates until they naturally age out of
            // the existing bounded stores; migration never deletes them merely because the new schema exists.
            AddLegacyStrings(candidates, memory.ImportantMemories, "legacy-important", 72, query, ref order);
            AddLegacyStrings(candidates, memory.OutingSummaries, "legacy-outing", 60, query, ref order);
            if (memory.RecentEvents != null)
            {
                for (int i = memory.RecentEvents.Count - 1; i >= 0; i--)
                {
                    MemoryEvent evt = memory.RecentEvents[i];
                    if (evt == null || string.IsNullOrWhiteSpace(evt.text)) continue;
                    if (string.Equals(evt.type, "deep_group_chat", StringComparison.OrdinalIgnoreCase) || string.Equals(evt.type, "conversation", StringComparison.OrdinalIgnoreCase)) continue;
                    AddLegacy(candidates, "legacy-event", evt.text, Math.Max(0, Math.Min(100, evt.importance)), query, order++);
                }
            }

            candidates.Sort(delegate(RetrievedMemoryCandidate a, RetrievedMemoryCandidate b)
            {
                int score = b.Score.CompareTo(a.Score);
                if (score != 0) return score;
                return a.StableOrder.CompareTo(b.StableOrder);
            });

            List<RelevantMemory> selected = new List<RelevantMemory>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool genericMemoryRecall = Regex.IsMatch(topicText ?? string.Empty,
                @"\b(?:remember|memory|memories|past|history|important|promise|relationship|how do you know me|what do you know about me)\b", RegexOptions.IgnoreCase);
            for (int i = 0; i < candidates.Count && selected.Count < Math.Min(4, limit); i++)
            {
                RetrievedMemoryCandidate candidate = candidates[i];
                if (candidate.MatchCount == 0)
                {
                    bool strongRecallCandidate = genericMemoryRecall && candidate.Record != null &&
                        (candidate.Record.Pinned || candidate.Record.Authored);
                    if (!strongRecallCandidate) continue;
                }
                string key = Normalize(candidate.Text);
                if (key.Length == 0 || !seen.Add(key)) continue;
                selected.Add(new RelevantMemory { Source = candidate.Source, Text = candidate.Text, Score = candidate.Score, Recency = candidate.StableOrder, MatchCount = candidate.MatchCount });
            }
            return selected;
        }

        private static void AddRecords(List<RetrievedMemoryCandidate> result, List<StructuredMemoryRecord> records, string source,
            SimSnapshot owner, HashSet<string> query, bool recentLifeRecall, ref int order)
        {
            if (records == null) return;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                StructuredMemoryRecord record = records[i];
                if (record == null || !MemoryKnowledgePolicy.CanUse(record, owner)) { order++; continue; }
                HashSet<string> tokens = Tokens(record.Text + " " + record.Subject + " " + Join(record.Topics));
                int overlap = Overlap(query, tokens);
                double score = overlap * 12.0;
                string recordSource = source;
                bool recentLife = string.Equals(record.Source, RecentLifePolicy.Source, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(record.SourceSystem, RecentLifePolicy.SourceSystem, StringComparison.OrdinalIgnoreCase);
                if (recentLife) recordSource = "simulated-recent-life";
                if (recentLife && recentLifeRecall) { overlap = Math.Max(1, overlap); score += 30.0; }
                score += record.Importance * 0.16;
                if (record.Authored) score += 16.0;
                if (record.Pinned) score += 22.0;
                if (record.EmotionalTags != null && record.EmotionalTags.Count > 0) score += Math.Min(6.0, record.EmotionalTags.Count * 2.0);
                score += ParticipantQueryOverlap(record.Participants, query) * 4.0;
                if (ContainsIdentity(record.Participants, "player")) score += 5.0;
                if (ContainsIdentity(record.Participants, owner.Key) || ContainsIdentity(record.Participants, owner.Name)) score += 4.0;
                DateTime when;
                if (DateTime.TryParse(record.Utc, out when))
                {
                    double days = Math.Max(0.0, (DateTime.UtcNow - when.ToUniversalTime()).TotalDays);
                    score += Math.Max(0.0, 8.0 - Math.Min(8.0, days / 7.0));
                }
                result.Add(new RetrievedMemoryCandidate { Record = record, Source = recordSource, Text = record.Text.Trim(), Score = score, MatchCount = overlap, StableOrder = order++ });
            }
        }

        private static void AddLegacyStrings(List<RetrievedMemoryCandidate> result, List<string> values, string source, int importance,
            HashSet<string> query, ref int order)
        {
            if (values == null) return;
            for (int i = values.Count - 1; i >= 0; i--)
                AddLegacy(result, source, string.Equals(source, "legacy-outing", StringComparison.OrdinalIgnoreCase)
                    ? VerifiedOutingHistoryPolicy.SanitizeForPrompt(values[i]) : values[i], importance, query, order++);
        }

        private static void AddLegacy(List<RetrievedMemoryCandidate> result, string source, string text, int importance, HashSet<string> query, int order)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            int overlap = Overlap(query, Tokens(text));
            double score = overlap * 12.0 + importance * 0.12 + Math.Max(0.0, 4.0 - Math.Min(4.0, order));
            result.Add(new RetrievedMemoryCandidate { Source = source, Text = text.Trim(), Score = score, MatchCount = overlap, StableOrder = order });
        }

        private static int Overlap(HashSet<string> a, HashSet<string> b)
        {
            int overlap = 0;
            foreach (string value in a) if (b.Contains(value)) overlap++;
            return overlap;
        }

        private static HashSet<string> Tokens(string text)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] parts = Regex.Split((text ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9+']+");
            string[] stops = new string[] { "the", "and", "that", "this", "with", "from", "have", "what", "when", "where", "your", "about", "party", "player", "current", "verified", "there", "were", "been" };
            HashSet<string> stop = new HashSet<string>(stops, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < parts.Length; i++)
            {
                string token = parts[i].Trim();
                if (token.Length < 3 || stop.Contains(token)) continue;
                set.Add(token);
                if (token == "loot" || token == "looted" || token == "drop" || token == "dropped") { set.Add("item"); set.Add("found"); }
                if (token == "fight" || token == "combat" || token == "killed" || token == "kill") { set.Add("encounter"); set.Add("fought"); }
                if (token == "job" || token == "career" || token == "work") { set.Add("career"); set.Add("job"); }
                if (token == "college" || token == "school" || token == "academy" || token == "study") { set.Add("study"); set.Add("school"); }
            }
            return set;
        }

        private static int ParticipantQueryOverlap(List<string> participants, HashSet<string> query)
        {
            if (participants == null || query == null || query.Count == 0) return 0;
            int hits = 0;
            for (int i = 0; i < participants.Count; i++) hits += Overlap(query, Tokens(participants[i]));
            return Math.Min(2, hits);
        }

        private static bool ContainsIdentity(List<string> values, string identity)
        {
            if (values == null || string.IsNullOrWhiteSpace(identity)) return false;
            for (int i = 0; i < values.Count; i++) if (string.Equals(values[i], identity, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string Join(List<string> values) { return values == null ? string.Empty : string.Join(" ", values.ToArray()); }
        private static string Normalize(string value) { return Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"\s+", " ").Trim(); }
    }

    internal sealed class ContextBudgetResult
    {
        internal int BeforeTokens;
        internal int AfterTokens;
        internal int PromptBudgetTokens;
        internal int DroppedMessages;
        internal int DroppedSections;
        internal readonly List<string> DroppedKinds = new List<string>();
        internal bool Trimmed { get { return DroppedMessages > 0 || DroppedSections > 0 || AfterTokens < BeforeTokens; } }
        internal string Describe()
        {
            return "before=" + BeforeTokens + " after=" + AfterTokens + " budget=" + PromptBudgetTokens +
                " droppedMessages=" + DroppedMessages + " droppedSections=" + DroppedSections +
                (DroppedKinds.Count == 0 ? string.Empty : " kinds=" + string.Join(",", DroppedKinds.ToArray()));
        }
    }

    internal static class PromptContextBudget
    {
        private sealed class Item { internal int Index; internal int Priority; internal string Kind; }

        internal static ContextBudgetResult Apply(List<ChatMessage> messages, int contextWindow, int responseAllowance)
        {
            ContextBudgetResult result = new ContextBudgetResult();
            if (messages == null) return result;
            result.BeforeTokens = PromptBuilder.EstimateTokenCount(messages);
            result.PromptBudgetTokens = Math.Max(512, Math.Max(1024, contextWindow) - Math.Max(64, responseAllowance) - 96);
            if (result.BeforeTokens <= result.PromptBudgetTokens) { result.AfterTokens = result.BeforeTokens; return result; }

            List<Item> removable = new List<Item>();
            for (int i = 0; i < messages.Count; i++)
            {
                ChatMessage message = messages[i];
                if (message == null || string.IsNullOrWhiteSpace(message.content)) continue;
                int priority; string kind;
                if (TryClassify(message, i, messages.Count, out priority, out kind)) removable.Add(new Item { Index = i, Priority = priority, Kind = kind });
            }
            removable.Sort(delegate(Item a, Item b) { int p = a.Priority.CompareTo(b.Priority); return p != 0 ? p : a.Index.CompareTo(b.Index); });
            HashSet<int> remove = new HashSet<int>();
            for (int i = 0; i < removable.Count && PromptBuilder.EstimateTokenCount(messages) > result.PromptBudgetTokens; i++)
            {
                Item item = removable[i];
                if (remove.Contains(item.Index)) continue;
                messages[item.Index].content = string.Empty;
                remove.Add(item.Index);
                result.DroppedMessages++;
                if (!result.DroppedKinds.Contains(item.Kind)) result.DroppedKinds.Add(item.Kind);
            }
            for (int i = messages.Count - 1; i >= 0; i--) if (messages[i] == null || string.IsNullOrWhiteSpace(messages[i].content)) messages.RemoveAt(i);

            // Older/general prompt paths place bounded context sections inside one system message.
            // Remove only known low-priority sections, never arbitrary lines or the safety/current-facts anchors.
            if (PromptBuilder.EstimateTokenCount(messages) > result.PromptBudgetTokens)
                TrimEmbeddedSystemSections(messages, result);

            // Last-resort bounded compaction. Preserve the first system contract and newest user turn;
            // trim only their interior character count rather than deleting either semantic anchor.
            while (PromptBuilder.EstimateTokenCount(messages) > result.PromptBudgetTokens && messages.Count > 2)
            {
                int candidate = FindOldestNonAnchor(messages);
                if (candidate < 0) break;
                messages.RemoveAt(candidate);
                result.DroppedMessages++;
                if (!result.DroppedKinds.Contains("old-context")) result.DroppedKinds.Add("old-context");
            }
            result.AfterTokens = PromptBuilder.EstimateTokenCount(messages);
            return result;
        }

        private static void TrimEmbeddedSystemSections(List<ChatMessage> messages, ContextBudgetResult result)
        {
            if (messages == null || messages.Count == 0 || result == null) return;
            int systemIndex = -1;
            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i] != null && string.Equals(messages[i].role, "system", StringComparison.OrdinalIgnoreCase))
                { systemIndex = i; break; }
            }
            if (systemIndex < 0) return;

            string content = messages[systemIndex].content ?? string.Empty;
            // These markers come from BuildSystemPrompt. The order is deliberately lowest-value first.
            TrimBlockIfNeeded(messages, systemIndex, ref content, result, "STYLE-ONLY examples sampled",
                new string[] { "VERIFIED CURRENT FACTS:" }, "style-examples");
            TrimBlockIfNeeded(messages, systemIndex, ref content, result, "DEFAULT IDENTITY TEMPLATE",
                EmbeddedEndMarkers(), "default-identity");
            TrimBlockIfNeeded(messages, systemIndex, ref content, result, "PERSISTENT FLAVOR PREFERENCES",
                EmbeddedEndMarkers(), "soft-persona");
            TrimLineIfNeeded(messages, systemIndex, ref content, result, "Personal background (", "background");
            TrimBlockIfNeeded(messages, systemIndex, ref content, result, "TOPIC-RELEVANT MEMORIES",
                EmbeddedEndMarkers(), "memory");
            TrimBlockIfNeeded(messages, systemIndex, ref content, result, "SOCIAL CONVERSATION HISTORY",
                EmbeddedEndMarkers(), "conversation-summary");
            TrimBlockIfNeeded(messages, systemIndex, ref content, result, "COMPACT SOCIAL TONE WITH CURRENT PARTY",
                EmbeddedEndMarkers(), "relationship");
            TrimLineIfNeeded(messages, systemIndex, ref content, result, "Relationship to player:", "authored-relationship");
            messages[systemIndex].content = content;
        }

        private static string[] EmbeddedEndMarkers()
        {
            return new string[]
            {
                "DEFAULT IDENTITY TEMPLATE", "AUTHOR-DEFINED IDENTITY", "PERSISTENT FLAVOR PREFERENCES", "TOPIC-RELEVANT MEMORIES", "SOCIAL CONVERSATION HISTORY",
                "COMPACT SOCIAL TONE WITH CURRENT PARTY", "RECENT REAL-WORLD NEWS SEARCH RESULTS",
                "UNVERIFIED REFERENCE TEXT FROM ", "Reply only with"
            };
        }

        private static void TrimBlockIfNeeded(List<ChatMessage> messages, int systemIndex, ref string content, ContextBudgetResult result,
            string startMarker, string[] endMarkers, string kind)
        {
            if (PromptBuilder.EstimateTokenCount(messages) <= result.PromptBudgetTokens) return;
            int start = content.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return;
            int end = content.Length;
            if (endMarkers != null)
            {
                for (int i = 0; i < endMarkers.Length; i++)
                {
                    string marker = endMarkers[i];
                    if (string.IsNullOrWhiteSpace(marker) || string.Equals(marker, startMarker, StringComparison.OrdinalIgnoreCase)) continue;
                    int found = content.IndexOf(marker, start + startMarker.Length, StringComparison.OrdinalIgnoreCase);
                    if (found >= 0 && found < end) end = found;
                }
            }
            if (end <= start) return;
            content = (content.Substring(0, start) + content.Substring(end)).TrimEnd();
            messages[systemIndex].content = content;
            result.DroppedSections++;
            AddDroppedKind(result, kind);
        }

        private static void TrimLineIfNeeded(List<ChatMessage> messages, int systemIndex, ref string content, ContextBudgetResult result,
            string linePrefix, string kind)
        {
            if (PromptBuilder.EstimateTokenCount(messages) <= result.PromptBudgetTokens) return;
            int start = content.IndexOf(linePrefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return;
            int lineStart = start;
            while (lineStart > 0 && content[lineStart - 1] != '\n') lineStart--;
            int end = content.IndexOf('\n', start);
            if (end < 0) end = content.Length; else end++;
            content = content.Remove(lineStart, end - lineStart);
            messages[systemIndex].content = content;
            result.DroppedSections++;
            AddDroppedKind(result, kind);
        }

        private static void AddDroppedKind(ContextBudgetResult result, string kind)
        {
            if (result == null || string.IsNullOrWhiteSpace(kind)) return;
            if (!result.DroppedKinds.Contains(kind)) result.DroppedKinds.Add(kind);
        }

        private static bool TryClassify(ChatMessage message, int index, int count, out int priority, out string kind)
        {
            priority = 99; kind = string.Empty;
            string text = message.content ?? string.Empty;
            bool last = index == count - 1;
            if (last && string.Equals(message.role, "user", StringComparison.OrdinalIgnoreCase)) return false;
            if (index == 0 && string.Equals(message.role, "system", StringComparison.OrdinalIgnoreCase)) return false;
            if (text.StartsWith("OPTIONAL EXTERNAL REFERENCE", StringComparison.OrdinalIgnoreCase)) { priority = 1; kind = "optional-search"; return true; }
            if (text.StartsWith("RETRIEVED EVIDENCE", StringComparison.OrdinalIgnoreCase)) return false;
            if (text.StartsWith("DEFAULT PERSONAL BACKGROUND SCAFFOLD", StringComparison.OrdinalIgnoreCase)) { priority = 1; kind = "default-background"; return true; }
            if (text.StartsWith("DEFAULT IDENTITY TEMPLATE", StringComparison.OrdinalIgnoreCase)) { priority = 2; kind = "default-identity"; return true; }
            if (text.StartsWith("DEFAULT RELATIONSHIP TEMPLATE", StringComparison.OrdinalIgnoreCase)) { priority = 2; kind = "default-relationship"; return true; }
            if (text.StartsWith("RELEVANT PERSONAL BACKGROUND", StringComparison.OrdinalIgnoreCase)) { priority = 3; kind = "background"; return true; }
            if (text.StartsWith("RELEVANT VERIFIED HISTORY", StringComparison.OrdinalIgnoreCase)) { priority = 4; kind = "memory"; return true; }
            if (text.StartsWith("SOFT PERSONA", StringComparison.OrdinalIgnoreCase)) { priority = 5; kind = "soft-persona"; return true; }
            if (text.StartsWith("AUTHOR-DEFINED RELATIONSHIP", StringComparison.OrdinalIgnoreCase)) { priority = 6; kind = "relationship"; return true; }
            if (text.StartsWith("BOUNDED CURRENT-SESSION SUMMARY", StringComparison.OrdinalIgnoreCase)) { priority = 7; kind = "session-summary"; return true; }
            if (text.StartsWith("VISIBLE PARTY CHAT", StringComparison.OrdinalIgnoreCase) || text.StartsWith("Earlier party chat", StringComparison.OrdinalIgnoreCase)) { priority = 8; kind = "older-conversation"; return true; }
            if (string.Equals(message.role, "user", StringComparison.OrdinalIgnoreCase) && !last) { priority = 8; kind = "older-conversation"; return true; }
            return false;
        }

        private static int FindOldestNonAnchor(List<ChatMessage> messages)
        {
            for (int i = 1; i < messages.Count - 1; i++)
            {
                ChatMessage message = messages[i];
                string text = message == null ? string.Empty : (message.content ?? string.Empty);
                if (text.StartsWith("CURRENT AUTHORITATIVE STATE", StringComparison.OrdinalIgnoreCase) ||
                    text.StartsWith("AUTHOR-DEFINED CORE IDENTITY", StringComparison.OrdinalIgnoreCase) ||
                    text.StartsWith("RETRIEVED EVIDENCE", StringComparison.OrdinalIgnoreCase)) continue;
                return i;
            }
            return -1;
        }
    }
}
