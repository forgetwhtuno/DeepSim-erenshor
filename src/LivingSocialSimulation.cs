using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ErenshorDeepSims
{
    [Serializable]
    public sealed class SocialEpisodeRecord
    {
        public string EpisodeId;
        public string SourceSystem;
        public string SourceCorrelationId;
        public string CharacterScope;
        public string Utc;
        public string Location;
        public string FactualSummary;
        public List<string> Participants;
        public List<string> KnownBy;
        public List<string> Tags;
        public int Importance;
        public float HumorScore;
        public float ConflictScore;
        public float RelationshipImpact;
        public string Provenance;
        public bool ExplicitPublic;

        public void Normalize()
        {
            EpisodeId = Clean(EpisodeId, 96);
            SourceSystem = Token(SourceSystem, 48);
            SourceCorrelationId = Clean(SourceCorrelationId, 96);
            CharacterScope = Clean(CharacterScope, 96);
            Utc = Clean(Utc, 64);
            Location = Clean(Location, 120);
            FactualSummary = Clean(FactualSummary, 520);
            Provenance = Token(Provenance, 48);
            if (Participants == null) Participants = new List<string>();
            if (KnownBy == null) KnownBy = new List<string>();
            if (Tags == null) Tags = new List<string>();
            BoundList(Participants, 16, 120);
            BoundList(KnownBy, 20, 120);
            BoundList(Tags, 12, 48);
            Importance = ClampInt(Importance, 0, 100);
            HumorScore = Clamp01(HumorScore);
            ConflictScore = Clamp01(ConflictScore);
            RelationshipImpact = Math.Max(-1f, Math.Min(1f, RelationshipImpact));
        }

        private static int ClampInt(int v, int min, int max) { return Math.Max(min, Math.Min(max, v)); }
        private static float Clamp01(float v) { return Math.Max(0f, Math.Min(1f, v)); }
        private static string Clean(string value, int max) { return IdentitySchema.Bound(value, max); }
        private static string Token(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            StringBuilder sb = new StringBuilder();
            string s = value.Trim().ToLowerInvariant();
            for (int i = 0; i < s.Length && sb.Length < max; i++)
            {
                char c = s[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
            }
            return sb.ToString();
        }
        private static void BoundList(List<string> values, int maxCount, int maxChars)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = values.Count - 1; i >= 0; i--)
            {
                string v = Clean(values[i], maxChars);
                if (v.Length == 0 || !seen.Add(v)) values.RemoveAt(i); else values[i] = v;
            }
            if (values.Count > maxCount) values.RemoveRange(maxCount, values.Count - maxCount);
        }
    }

    [Serializable]
    public sealed class SocialRelationshipMemory
    {
        public string OtherSimKey;
        public string OtherName;
        public float Familiarity;
        public float Warmth;
        public float Trust;
        public float Tension;
        public float Rivalry;
        public int MeaningfulEpisodes;
        public string UpdatedUtc;
        public List<string> AppliedEpisodeIds;

        public void Normalize()
        {
            OtherSimKey = IdentitySchema.Bound(OtherSimKey, 120);
            OtherName = IdentitySchema.Bound(OtherName, 120);
            Familiarity = Clamp01(Familiarity);
            Warmth = ClampSigned(Warmth);
            Trust = ClampSigned(Trust);
            Tension = Clamp01(Tension);
            Rivalry = Clamp01(Rivalry);
            MeaningfulEpisodes = Math.Max(0, Math.Min(100000, MeaningfulEpisodes));
            UpdatedUtc = IdentitySchema.Bound(UpdatedUtc, 64);
            if (AppliedEpisodeIds == null) AppliedEpisodeIds = new List<string>();
            if (AppliedEpisodeIds.Count > 24) AppliedEpisodeIds.RemoveRange(0, AppliedEpisodeIds.Count - 24);
        }

        private static float Clamp01(float v) { return Math.Max(0f, Math.Min(1f, v)); }
        private static float ClampSigned(float v) { return Math.Max(-1f, Math.Min(1f, v)); }
    }

    internal sealed class SocialAffectState
    {
        internal float Amusement;
        internal float Warmth;
        internal float Irritation;
        internal float Anger;
        internal float Embarrassment;
        internal float Concern;
        internal float Confidence;
        internal float Excitement;
        internal float Disappointment;
        internal float Relief;
        internal float Competitiveness;
        internal float Fatigue;
        internal double LastActiveSeconds;

        internal void Decay(double activeSeconds)
        {
            if (LastActiveSeconds <= 0) { LastActiveSeconds = activeSeconds; return; }
            double elapsed = Math.Max(0.0, activeSeconds - LastActiveSeconds);
            LastActiveSeconds = activeSeconds;
            if (elapsed <= 0) return;
            Amusement = DecayValue(Amusement, elapsed, 180.0);
            Irritation = DecayValue(Irritation, elapsed, 900.0);
            Anger = DecayValue(Anger, elapsed, 1200.0);
            Embarrassment = DecayValue(Embarrassment, elapsed, 720.0);
            Concern = DecayValue(Concern, elapsed, 900.0);
            Confidence = DecayValue(Confidence, elapsed, 900.0);
            Excitement = DecayValue(Excitement, elapsed, 300.0);
            Disappointment = DecayValue(Disappointment, elapsed, 720.0);
            Relief = DecayValue(Relief, elapsed, 300.0);
            Competitiveness = DecayValue(Competitiveness, elapsed, 900.0);
            Fatigue = DecayValue(Fatigue, elapsed, 900.0);
            Warmth = DecayValue(Warmth, elapsed, 1800.0);
        }

        internal string Describe()
        {
            List<string> tags = new List<string>();
            Add(tags, "amused", Amusement, .22f); Add(tags, "warm", Warmth, .25f);
            Add(tags, "irritated", Irritation, .25f); Add(tags, "angry", Anger, .35f);
            Add(tags, "embarrassed", Embarrassment, .25f); Add(tags, "concerned", Concern, .25f);
            Add(tags, "confident", Confidence, .30f); Add(tags, "excited", Excitement, .28f);
            Add(tags, "disappointed", Disappointment, .28f); Add(tags, "relieved", Relief, .25f);
            Add(tags, "competitive", Competitiveness, .28f); Add(tags, "tired", Fatigue, .30f);
            return tags.Count == 0 ? "baseline" : string.Join(",", tags.ToArray());
        }

        private static float DecayValue(float value, double elapsed, double halfLife)
        {
            if (value <= 0f) return 0f;
            double factor = Math.Pow(0.5, elapsed / Math.Max(1.0, halfLife));
            float result = (float)(value * factor);
            return result < .01f ? 0f : result;
        }
        private static void Add(List<string> values, string name, float value, float threshold) { if (value >= threshold) values.Add(name); }
        internal static float Clamp(float value) { return Math.Max(0f, Math.Min(1f, value)); }
    }

    internal sealed class SocialDriveState
    {
        internal string Key = string.Empty;
        internal string Topic = string.Empty;
        internal double ExpiresAtActiveSeconds;
        internal string EvidenceId = string.Empty;
        internal bool IsActive(double activeSeconds) { return Key.Length > 0 && activeSeconds < ExpiresAtActiveSeconds; }
    }

    internal sealed class SocialCurationEvidence
    {
        internal string Id;
        internal string Kind;
        internal string SourceSystem;
        internal string CharacterScope;
        internal string FactualText;
        internal string RawConversationText;
        internal List<string> Participants;
        internal List<string> KnownBy;
        internal int Importance;
        internal float HumorHint;
        internal float ConflictHint;
        internal DateTime Utc;
    }

    [Serializable]
    internal sealed class SocialCurationCandidate
    {
        public float significance;
        public string category;
        public List<string> participants;
        public string factual_basis;
        public string interpretation;
        public bool funny;
        public bool conflict;
        public string callback_candidate;
        public string follow_up_topic;
        public List<string> evidence_ids;
        public float confidence;
    }

    [Serializable]
    internal sealed class SocialCurationEnvelope
    {
        public List<SocialCurationCandidate> candidates;
    }

    internal enum SocialCurationDisposition
    {
        Invalid,
        ExplicitEmpty,
        Accepted
    }

    internal sealed class SocialCurationResult
    {
        internal SocialCurationDisposition Disposition;
        internal List<StructuredMemoryRecord> Accepted = new List<StructuredMemoryRecord>();
        internal Dictionary<string, int> RejectedReasons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        internal void NoteRejected(string reason)
        {
            string key = string.IsNullOrWhiteSpace(reason) ? "policy_rejected" : reason;
            int count; RejectedReasons.TryGetValue(key, out count); RejectedReasons[key] = count + 1;
        }
    }

    internal sealed class SocialPromptState
    {
        internal string Affect = "baseline";
        internal string Relationship = "familiarity=low,warmth=neutral,trust=neutral,tension=low,rivalry=low";
        internal string Drive = "none";
        internal string OptionalSeed = "NONE";
        internal string OptionalSeedId = string.Empty;
        internal string MemoryBlock = string.Empty;
        internal string CampBlock = string.Empty;
    }

    internal static class SocialKnowledgePolicy
    {
        internal static bool IdentityListed(List<string> list, string key, string name)
        {
            if (list == null || list.Count == 0) return false;
            for (int i = 0; i < list.Count; i++)
            {
                string v = list[i] ?? string.Empty;
                if ((!string.IsNullOrWhiteSpace(key) && string.Equals(v, key, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(name) && string.Equals(v, name, StringComparison.OrdinalIgnoreCase))) return true;
            }
            return false;
        }

        internal static bool CanKnow(SocialEpisodeRecord episode, SimSnapshot sim, string characterScope)
        {
            if (episode == null || sim == null) return false;
            episode.Normalize();
            if (!string.IsNullOrWhiteSpace(episode.CharacterScope) && !string.Equals(episode.CharacterScope, characterScope ?? string.Empty, StringComparison.Ordinal)) return false;
            if (episode.ExplicitPublic) return true;
            return IdentityListed(episode.KnownBy, sim.Key, sim.Name);
        }

        internal static bool IntersectsKnowledge(IEnumerable<SocialCurationEvidence> evidence, string ownerKey, string ownerName)
        {
            bool saw = false;
            foreach (SocialCurationEvidence item in evidence)
            {
                if (item == null) continue;
                saw = true;
                if (!IdentityListed(item.KnownBy, ownerKey, ownerName)) return false;
            }
            return saw;
        }
    }

    internal sealed class SocialEpisodeLedger
    {
        private readonly List<SocialEpisodeRecord> _items = new List<SocialEpisodeRecord>();
        private readonly Dictionary<string, string> _correlationToEpisode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private const int Capacity = 64;

        internal SocialEpisodeRecord AddOrMerge(SocialEpisodeRecord episode)
        {
            if (episode == null) return null;
            episode.Normalize();
            string correlation = CanonicalCorrelation(episode.SourceSystem, episode.SourceCorrelationId);
            if (correlation.Length > 0)
            {
                string existingId;
                if (_correlationToEpisode.TryGetValue(correlation, out existingId))
                {
                    SocialEpisodeRecord existing = Find(existingId);
                    if (existing != null)
                    {
                        Merge(existing, episode);
                        return existing;
                    }
                }
            }
            if (episode.EpisodeId.Length == 0) episode.EpisodeId = StableId(episode.SourceSystem + "|" + episode.Utc + "|" + episode.FactualSummary);
            _items.Add(episode);
            if (correlation.Length > 0) _correlationToEpisode[correlation] = episode.EpisodeId;
            while (_items.Count > Capacity)
            {
                SocialEpisodeRecord removed = _items[0];
                _items.RemoveAt(0);
                string key = CanonicalCorrelation(removed.SourceSystem, removed.SourceCorrelationId);
                if (key.Length > 0 && _correlationToEpisode.ContainsKey(key) && _correlationToEpisode[key] == removed.EpisodeId) _correlationToEpisode.Remove(key);
            }
            return episode;
        }

        internal List<SocialEpisodeRecord> Recent(int max)
        {
            List<SocialEpisodeRecord> result = new List<SocialEpisodeRecord>();
            int start = Math.Max(0, _items.Count - Math.Max(0, max));
            for (int i = start; i < _items.Count; i++) result.Add(_items[i]);
            return result;
        }

        internal int Count { get { return _items.Count; } }
        internal void Clear() { _items.Clear(); _correlationToEpisode.Clear(); }

        internal static string CanonicalCorrelation(string sourceSystem, string correlationId)
        {
            if (string.IsNullOrWhiteSpace(correlationId)) return string.Empty;
            string source = (sourceSystem ?? string.Empty).Trim().ToLowerInvariant();
            string id = correlationId.Trim().ToLowerInvariant();
            // Nemesis forwards PvP's MatchId through its match/correlation field. Collapse only this
            // proven cross-system relation; never infer correlation from participant names.
            if (source == "pvp" || source == "nemesis_pvp" || source == "nemesis-pvp") return "pvp:" + id;
            if (source == "nemesis") return "nemesis:" + id;
            if (source == "duel") return "duel:" + id;
            return source + ":" + id;
        }

        private SocialEpisodeRecord Find(string id)
        {
            for (int i = 0; i < _items.Count; i++) if (string.Equals(_items[i].EpisodeId, id, StringComparison.Ordinal)) return _items[i];
            return null;
        }
        private static void Merge(SocialEpisodeRecord target, SocialEpisodeRecord source)
        {
            target.Importance = Math.Max(target.Importance, source.Importance);
            target.HumorScore = Math.Max(target.HumorScore, source.HumorScore);
            target.ConflictScore = Math.Max(target.ConflictScore, source.ConflictScore);
            if (source.FactualSummary.Length > target.FactualSummary.Length) target.FactualSummary = source.FactualSummary;
            AddAll(target.Participants, source.Participants); AddAll(target.KnownBy, source.KnownBy); AddAll(target.Tags, source.Tags);
            if (target.Provenance.Length == 0) target.Provenance = source.Provenance;
        }
        private static void AddAll(List<string> target, List<string> source)
        {
            if (target == null || source == null) return;
            for (int i = 0; i < source.Count; i++) if (!Contains(target, source[i])) target.Add(source[i]);
        }
        private static bool Contains(List<string> list, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            for (int i = 0; i < list.Count; i++) if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        internal static string StableId(string value)
        {
            unchecked
            {
                uint h = 2166136261;
                string s = value ?? string.Empty;
                for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619; }
                return "social-" + h.ToString("x8");
            }
        }
    }

    internal static class SocialAffectPolicy
    {
        internal static void Apply(SocialAffectState state, SocialEpisodeRecord episode, string ownerName)
        {
            if (state == null || episode == null) return;
            string tags = string.Join(" ", (episode.Tags ?? new List<string>()).ToArray()).ToLowerInvariant();
            float scale = Math.Max(.08f, Math.Min(.45f, episode.Importance / 220f));
            if (episode.HumorScore >= .45f || tags.Contains("humor")) state.Amusement = SocialAffectState.Clamp(state.Amusement + scale * Math.Max(.5f, episode.HumorScore));
            if (tags.Contains("compliment") || tags.Contains("help") || tags.Contains("success") || tags.Contains("reconcile")) state.Warmth = SocialAffectState.Clamp(state.Warmth + scale * .7f);
            if (tags.Contains("insult") || tags.Contains("argument") || episode.ConflictScore >= .45f) state.Irritation = SocialAffectState.Clamp(state.Irritation + scale * Math.Max(.6f, episode.ConflictScore));
            if (state.Irritation >= .62f && (tags.Contains("insult") || tags.Contains("argument"))) state.Anger = SocialAffectState.Clamp(state.Anger + scale * .55f);
            if (tags.Contains("embarrassment") || tags.Contains("duel_loss")) state.Embarrassment = SocialAffectState.Clamp(state.Embarrassment + scale * .65f);
            if (tags.Contains("danger") || tags.Contains("pvp") || tags.Contains("concern")) state.Concern = SocialAffectState.Clamp(state.Concern + scale * (tags.Contains("pvp") ? 1f : .65f));
            if (tags.Contains("duel_win") || tags.Contains("success")) state.Confidence = SocialAffectState.Clamp(state.Confidence + scale * .6f);
            if (tags.Contains("duel") || tags.Contains("rivalry") || tags.Contains("pvp")) state.Competitiveness = SocialAffectState.Clamp(state.Competitiveness + scale * .75f);
            if (tags.Contains("apology") || tags.Contains("reconcile"))
            {
                state.Irritation = Math.Max(0f, state.Irritation - scale * 1.3f);
                state.Anger = Math.Max(0f, state.Anger - scale * 1.0f);
                state.Warmth = SocialAffectState.Clamp(state.Warmth + scale * .35f);
                state.Relief = SocialAffectState.Clamp(state.Relief + scale * .5f);
            }
            if (tags.Contains("camp_relax"))
            {
                state.Irritation = Math.Max(0f, state.Irritation - .14f);
                state.Anger = Math.Max(0f, state.Anger - .12f);
                state.Concern = Math.Max(0f, state.Concern - .08f);
                state.Relief = SocialAffectState.Clamp(state.Relief + .10f);
            }
        }
    }

    internal static class SocialRelationshipPolicy
    {
        internal static bool Apply(SocialRelationshipMemory relation, SocialEpisodeRecord episode, string ownerKey, string ownerName, string otherKey, string otherName)
        {
            if (relation == null || episode == null) return false;
            episode.Normalize(); relation.Normalize();
            // Knowing about an event is not enough. Both pair members must be participants in the
            // social episode before it can alter their pairwise relationship.
            if (!SocialKnowledgePolicy.IdentityListed(episode.Participants, ownerKey, ownerName) ||
                !SocialKnowledgePolicy.IdentityListed(episode.Participants, otherKey, otherName)) return false;
            for (int i = 0; i < relation.AppliedEpisodeIds.Count; i++) if (string.Equals(relation.AppliedEpisodeIds[i], episode.EpisodeId, StringComparison.Ordinal)) return false;
            float significance = Math.Max(.05f, Math.Min(.35f, episode.Importance / 300f));
            string tags = string.Join(" ", episode.Tags.ToArray()).ToLowerInvariant();
            relation.Familiarity = Clamp01(relation.Familiarity + significance * .35f);
            if (tags.Contains("success") || tags.Contains("help") || tags.Contains("compliment") || tags.Contains("humor") || tags.Contains("reconcile"))
                relation.Warmth = ClampSigned(relation.Warmth + significance * .28f);
            if (tags.Contains("help") || tags.Contains("apology") || tags.Contains("reconcile")) relation.Trust = ClampSigned(relation.Trust + significance * .22f);
            if (tags.Contains("insult") || tags.Contains("argument") || episode.ConflictScore >= .55f) relation.Tension = Clamp01(relation.Tension + significance * .35f);
            if (tags.Contains("apology") || tags.Contains("reconcile") || tags.Contains("camp_relax")) relation.Tension = Clamp01(relation.Tension - significance * .30f);
            if (tags.Contains("duel") || tags.Contains("rivalry") || tags.Contains("pvp")) relation.Rivalry = Clamp01(relation.Rivalry + significance * .24f);
            relation.MeaningfulEpisodes++;
            relation.UpdatedUtc = DateTime.UtcNow.ToString("o");
            relation.AppliedEpisodeIds.Add(episode.EpisodeId);
            if (relation.AppliedEpisodeIds.Count > 24) relation.AppliedEpisodeIds.RemoveAt(0);
            relation.Normalize();
            return true;
        }

        internal static string Describe(SocialRelationshipMemory relation)
        {
            if (relation == null) return "familiarity=low,warmth=neutral,trust=neutral,tension=low,rivalry=low";
            relation.Normalize();
            return "familiarity=" + Band01(relation.Familiarity) + ",warmth=" + BandSigned(relation.Warmth) +
                ",trust=" + BandSigned(relation.Trust) + ",tension=" + Band01(relation.Tension) + ",rivalry=" + Band01(relation.Rivalry);
        }
        private static string Band01(float v) { return v >= .7f ? "high" : v >= .3f ? "medium" : "low"; }
        private static string BandSigned(float v) { return v >= .35f ? "warm" : v <= -.35f ? "cool" : "neutral"; }
        private static float Clamp01(float v) { return Math.Max(0f, Math.Min(1f, v)); }
        private static float ClampSigned(float v) { return Math.Max(-1f, Math.Min(1f, v)); }
    }

    internal static class SocialMemoryPolicy
    {
        internal const string TierRecent = "recent";
        internal const string TierSignificant = "significant";
        internal const string TierPinned = "pinned";
        internal const string TierInsideJoke = "inside_joke";

        internal static bool IsTrivialConversation(string text)
        {
            string clean = (text ?? string.Empty).Trim().ToLowerInvariant();
            return Regex.IsMatch(clean, @"^(?:hi|hey|hello|yo|ok|okay|k|thanks|ty|ready\??|yep|yeah|nah|nope|sure)[.!?]*$");
        }

        internal static int DeterministicImportance(SocialCurationCandidate candidate, IEnumerable<SocialCurationEvidence> evidence)
        {
            int basis = (int)Math.Round(Math.Max(0f, Math.Min(1f, candidate == null ? 0f : candidate.significance)) * 70f);
            int maxEvidence = 0;
            foreach (SocialCurationEvidence item in evidence) if (item != null) maxEvidence = Math.Max(maxEvidence, item.Importance);
            int result = Math.Max(basis, (int)Math.Round(maxEvidence * .75));
            if (candidate != null && candidate.funny) result += 8;
            if (candidate != null && candidate.conflict) result += 10;
            return Math.Max(0, Math.Min(100, result));
        }

        internal static bool ShouldFade(StructuredMemoryRecord record, DateTime nowUtc)
        {
            if (record == null || record.Authored || record.Pinned) return false;
            record.Normalize();
            if (string.Equals(record.MemoryTier, TierInsideJoke, StringComparison.OrdinalIgnoreCase)) return false;
            DateTime when;
            if (!DateTime.TryParse(record.Utc, out when)) return false;
            double days = Math.Max(0.0, (nowUtc - when.ToUniversalTime()).TotalDays);
            if (string.Equals(record.Source, RecentLifePolicy.Source, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(record.SourceSystem, RecentLifePolicy.SourceSystem, StringComparison.OrdinalIgnoreCase)) return days > 7.0;
            if (string.Equals(record.MemoryTier, TierSignificant, StringComparison.OrdinalIgnoreCase) || record.Importance >= 70) return days > 180.0;
            return days > (record.Importance >= 50 ? 60.0 : 21.0);
        }

        internal static bool CanPromoteInsideJoke(StructuredMemoryRecord record)
        {
            if (record == null) return false;
            record.Normalize();
            bool humor = string.Equals(record.MemoryType, "shared_humor", StringComparison.OrdinalIgnoreCase) || record.HumorScore >= .65f;
            bool grounded = record.EvidenceIds.Count > 0 && !string.IsNullOrWhiteSpace(record.CallbackConcept) && record.KnownBy.Count >= 2;
            return humor && grounded && record.RecurrenceCount >= 2;
        }
    }

    internal static class SocialCurationPolicy
    {
        internal static bool IsUnsupportedFactClaim(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return Regex.IsMatch(text, @"\b(?:received|got|looted|acquired|was awarded|gave you|invited you to (?:the )?guild|promoted you|completed (?:the )?quest|accepted (?:the )?quest|joined (?:the )?party|left (?:the )?party|killed|defeated|won (?:the )?duel|lost (?:the )?duel|won (?:the )?pvp|died)\b", RegexOptions.IgnoreCase);
        }

        internal static SocialCurationResult Validate(SocialCurationEnvelope envelope, IList<SocialCurationEvidence> pending,
            SimSnapshot owner, string characterScope)
        {
            SocialCurationResult result = new SocialCurationResult();
            if (envelope == null || envelope.candidates == null) { result.Disposition = SocialCurationDisposition.Invalid; return result; }
            if (envelope.candidates.Count == 0) { result.Disposition = SocialCurationDisposition.ExplicitEmpty; return result; }
            Dictionary<string, SocialCurationEvidence> byId = new Dictionary<string, SocialCurationEvidence>(StringComparer.Ordinal);
            if (pending != null) for (int i = 0; i < pending.Count; i++) if (pending[i] != null && !string.IsNullOrWhiteSpace(pending[i].Id)) byId[pending[i].Id] = pending[i];
            for (int i = 0; i < envelope.candidates.Count && result.Accepted.Count < 4; i++)
            {
                SocialCurationCandidate candidate = envelope.candidates[i];
                StructuredMemoryRecord accepted; string rejectionReason;
                if (TryValidateCandidate(candidate, byId, owner, characterScope, out accepted, out rejectionReason)) result.Accepted.Add(accepted);
                else result.NoteRejected(rejectionReason);
            }
            result.Disposition = result.Accepted.Count > 0 ? SocialCurationDisposition.Accepted : SocialCurationDisposition.Invalid;
            return result;
        }

        private static bool TryValidateCandidate(SocialCurationCandidate candidate, Dictionary<string, SocialCurationEvidence> byId,
            SimSnapshot owner, string characterScope, out StructuredMemoryRecord record, out string rejectionReason)
        {
            record = null;
            rejectionReason = "none";
            if (candidate == null || owner == null) { rejectionReason = "missing_candidate_or_owner"; return false; }
            if (candidate.confidence < .55f) { rejectionReason = "low_confidence"; return false; }
            if (candidate.significance < .35f) { rejectionReason = "low_significance"; return false; }
            if (candidate.evidence_ids == null || candidate.evidence_ids.Count == 0) { rejectionReason = "missing_evidence"; return false; }
            List<SocialCurationEvidence> evidence = new List<SocialCurationEvidence>();
            for (int i = 0; i < candidate.evidence_ids.Count; i++)
            {
                SocialCurationEvidence item;
                if (!byId.TryGetValue(candidate.evidence_ids[i] ?? string.Empty, out item) || item == null) { rejectionReason = "unknown_evidence"; return false; }
                if (!string.Equals(item.CharacterScope ?? string.Empty, characterScope ?? string.Empty, StringComparison.Ordinal)) { rejectionReason = "scope_mismatch"; return false; }
                evidence.Add(item);
            }
            if (!SocialKnowledgePolicy.IntersectsKnowledge(evidence, owner.Key, owner.Name)) { rejectionReason = "knowledge_denied"; return false; }
            HashSet<string> participantSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> knowledgeIntersection = null;
            for (int i = 0; i < evidence.Count; i++)
            {
                AddAll(participantSet, evidence[i].Participants);
                HashSet<string> current = new HashSet<string>(evidence[i].KnownBy ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                if (knowledgeIntersection == null) knowledgeIntersection = current; else knowledgeIntersection.IntersectWith(current);
            }
            if (knowledgeIntersection == null || knowledgeIntersection.Count == 0) { rejectionReason = "knowledge_intersection_empty"; return false; }
            if (candidate.participants != null)
                for (int i = 0; i < candidate.participants.Count; i++) if (!participantSet.Contains(candidate.participants[i] ?? string.Empty)) { rejectionReason = "participant_not_in_evidence"; return false; }
            string factual = IdentitySchema.Bound(candidate.factual_basis, 420);
            string interpretation = IdentitySchema.Bound(candidate.interpretation, 280);
            // The model may summarize the evidence, but it cannot create a new gameplay/native fact.
            if (IsUnsupportedFactClaim(factual) && !AllEvidenceSupportsTokens(factual, evidence)) { rejectionReason = "unsupported_fact"; return false; }
            string callback = IdentitySchema.Bound(candidate.callback_candidate, 80);
            if (callback.Length > 0 && !CallbackGrounded(callback, evidence)) { rejectionReason = "callback_ungrounded"; return false; }
            int importance = SocialMemoryPolicy.DeterministicImportance(candidate, evidence);
            if (importance < 35) { rejectionReason = "importance_below_threshold"; return false; }
            record = new StructuredMemoryRecord();
            record.Id = SocialEpisodeLedger.StableId("curated|" + string.Join("|", candidate.evidence_ids.ToArray()) + "|" + (candidate.category ?? string.Empty));
            record.MemoryType = IdentitySchema.Bound(candidate.category, 48);
            if (record.MemoryType.Length == 0) record.MemoryType = candidate.funny ? "shared_humor" : candidate.conflict ? "social_conflict" : "social_memory";
            record.FactSummary = factual.Length > 0 ? factual : EvidenceFactSummary(evidence);
            record.Text = record.FactSummary; // Grounding corpora see FACT only, never interpretation.
            record.InterpretationSummary = interpretation;
            record.Subject = IdentitySchema.Bound(candidate.follow_up_topic, 100);
            record.Importance = importance;
            record.Participants = new List<string>();
            if (candidate.participants != null) record.Participants.AddRange(candidate.participants);
            if (record.Participants.Count == 0) foreach (string value in participantSet) record.Participants.Add(value);
            record.KnownBy = new List<string>(); foreach (string value in knowledgeIntersection) record.KnownBy.Add(value);
            record.Topics = MemoryTopicPolicy.ExtractTopics(record.FactSummary + " " + record.Subject);
            record.EmotionalTags = MemoryTopicPolicy.ExtractEmotionalTags(record.InterpretationSummary);
            record.Utc = DateTime.UtcNow.ToString("o");
            record.Source = "social_curation";
            record.SourceSystem = "deep_sims";
            record.CharacterScope = characterScope ?? string.Empty;
            record.EvidenceIds = new List<string>(candidate.evidence_ids);
            record.CallbackConcept = callback;
            record.HumorScore = candidate.funny ? Math.Max(.65f, candidate.significance) : 0f;
            record.ConflictScore = candidate.conflict ? Math.Max(.55f, candidate.significance) : 0f;
            record.MemoryTier = importance >= 70 ? SocialMemoryPolicy.TierSignificant : SocialMemoryPolicy.TierRecent;
            record.RecurrenceCount = 1;
            record.OwnerSimKey = owner.Key ?? string.Empty;
            record.OwnerSimName = owner.Name ?? string.Empty;
            record.Normalize();
            rejectionReason = "none";
            return true;
        }

        internal static bool CallbackGrounded(string callback, IList<SocialCurationEvidence> evidence)
        {
            HashSet<string> callbackTokens = Tokens(callback);
            if (callbackTokens.Count == 0) return false;
            StringBuilder basis = new StringBuilder();
            for (int i = 0; i < evidence.Count; i++) basis.Append(' ').Append(evidence[i].FactualText).Append(' ').Append(evidence[i].RawConversationText);
            HashSet<string> evidenceTokens = Tokens(basis.ToString());
            int overlap = 0; foreach (string token in callbackTokens) if (evidenceTokens.Contains(token)) overlap++;
            return overlap >= Math.Min(2, callbackTokens.Count);
        }

        private static bool AllEvidenceSupportsTokens(string claim, IList<SocialCurationEvidence> evidence)
        {
            HashSet<string> claimTokens = Tokens(claim);
            StringBuilder basis = new StringBuilder();
            for (int i = 0; i < evidence.Count; i++) basis.Append(' ').Append(evidence[i].FactualText);
            HashSet<string> evidenceTokens = Tokens(basis.ToString());
            foreach (string token in claimTokens)
            {
                if (token.Length < 5) continue;
                if (Regex.IsMatch(token, @"^(?:received|looted|acquired|awarded|invited|promoted|completed|joined|killed|defeated|duel|pvp|died)$") && !evidenceTokens.Contains(token)) return false;
            }
            return true;
        }
        private static string EvidenceFactSummary(IList<SocialCurationEvidence> evidence)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < evidence.Count && parts.Count < 3; i++) if (!string.IsNullOrWhiteSpace(evidence[i].FactualText)) parts.Add(evidence[i].FactualText);
            return IdentitySchema.Bound(string.Join("; ", parts.ToArray()), 420);
        }
        private static void AddAll(HashSet<string> target, List<string> values) { if (values != null) for (int i = 0; i < values.Count; i++) if (!string.IsNullOrWhiteSpace(values[i])) target.Add(values[i]); }
        private static HashSet<string> Tokens(string text)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] values = Regex.Split((text ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9']+");
            for (int i = 0; i < values.Length; i++) if (values[i].Length >= 3) result.Add(values[i]);
            return result;
        }
    }

    internal static class SocialPivotPolicy
    {
        internal const double OpportunityRate = .20;
        internal const double PivotCooldownSeconds = 150.0;
        internal const double TopicFatigueSeconds = 600.0;
        internal const double InsideJokeCooldownSeconds = 1200.0;

        internal static bool HasOpportunity(string stableKey)
        {
            unchecked
            {
                uint h = 2166136261;
                string value = stableKey ?? string.Empty;
                for (int i = 0; i < value.Length; i++) { h ^= value[i]; h *= 16777619; }
                return (h % 10000) < 2000;
            }
        }

        internal static bool IsUrgentOrTactical(string text)
        {
            return Regex.IsMatch(text ?? string.Empty, @"\b(?:attack|assist|pull|stop pulling|hold pulls|follow|come|run|flee|escape|guard|wait|heal|mana|burn|careful|target|now|quick)\b", RegexOptions.IgnoreCase);
        }

        internal static bool CanOffer(bool combat, string playerText, bool unresolvedThread, bool recentPivot, bool topicFatigued,
            SocialAffectState affect)
        {
            if (combat || IsUrgentOrTactical(playerText) || unresolvedThread || recentPivot || topicFatigued) return false;
            if (affect != null && (affect.Anger >= .55f || affect.Fatigue >= .65f)) return false;
            return true;
        }

        internal static bool OutputUsesSeed(string output, string seed)
        {
            if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(seed)) return false;
            HashSet<string> a = TokenSet(output), b = TokenSet(seed); int overlap = 0;
            foreach (string token in b) if (a.Contains(token)) overlap++;
            return overlap >= Math.Min(2, b.Count);
        }
        private static HashSet<string> TokenSet(string text)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] words = Regex.Split((text ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9']+");
            for (int i = 0; i < words.Length; i++) if (words[i].Length >= 4) set.Add(words[i]);
            return set;
        }
    }

    internal sealed class LivingSocialRuntime
    {
        private readonly object _gate = new object();
        private readonly SocialEpisodeLedger _episodes = new SocialEpisodeLedger();
        private readonly Dictionary<string, SocialAffectState> _affect = new Dictionary<string, SocialAffectState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SocialDriveState> _drives = new Dictionary<string, SocialDriveState>(StringComparer.OrdinalIgnoreCase);
        private readonly List<SocialCurationEvidence> _pending = new List<SocialCurationEvidence>();
        private readonly Dictionary<string, double> _lastPivotBySim = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _lastTopicByKey = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _lastInsideJokeById = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private long _evidenceSequence;
        private double _nextCurationAllowedActiveSeconds;
        private string _scope = CharacterScopeKey.Unscoped;
        private string _lastCurationDecision = "none";

        internal void ResetForCharacter(string scope)
        {
            lock (_gate)
            {                _scope = string.IsNullOrWhiteSpace(scope) ? CharacterScopeKey.Unscoped : scope;
                _episodes.Clear(); _affect.Clear(); _drives.Clear(); _pending.Clear(); _lastPivotBySim.Clear(); _lastTopicByKey.Clear(); _lastInsideJokeById.Clear();
                _evidenceSequence = 0; _nextCurationAllowedActiveSeconds = 0; _lastCurationDecision = "none";
            
            }
        }

        internal SocialEpisodeRecord ObserveVerifiedEpisode(string sourceSystem, string correlationId, string eventId, string factualSummary,
            IList<SimSnapshot> active, IList<string> participants, string location, int importance, IList<string> tags, float humor, float conflict,
            double activeSeconds)
        {
            lock (_gate)
            {                SocialEpisodeRecord episode = new SocialEpisodeRecord();
                episode.EpisodeId = string.IsNullOrWhiteSpace(eventId) ? SocialEpisodeLedger.StableId((sourceSystem ?? "event") + "|" + DateTime.UtcNow.Ticks + "|" + factualSummary) : IdentitySchema.Bound(eventId, 96);
                episode.SourceSystem = sourceSystem ?? "event"; episode.SourceCorrelationId = correlationId ?? string.Empty; episode.CharacterScope = _scope;
                episode.Utc = DateTime.UtcNow.ToString("o"); episode.Location = location ?? string.Empty; episode.FactualSummary = factualSummary ?? string.Empty;
                episode.Participants = new List<string>(); episode.KnownBy = new List<string>(); episode.Tags = new List<string>();
                if (participants != null)
                {
                    for (int i = 0; i < participants.Count; i++)
                    {
                        AddUnique(episode.Participants, participants[i]);
                        // Verified participants necessarily know the event they participated in.
                        // This only annotates episode knowledge; per-Sim durable persistence is still
                        // restricted to the caller-supplied current witnesses and character scope.
                        AddUnique(episode.KnownBy, participants[i]);
                    }
                }
                if (active != null)
                {
                    for (int i = 0; i < active.Count; i++)
                    {
                        SimSnapshot sim = active[i]; if (sim == null) continue;
                        AddUnique(episode.KnownBy, sim.Key); AddUnique(episode.KnownBy, sim.Name);
                    }
                }
                AddUnique(episode.KnownBy, "player");
                if (tags != null) for (int i = 0; i < tags.Count; i++) AddUnique(episode.Tags, tags[i]);
                episode.Importance = importance; episode.HumorScore = humor; episode.ConflictScore = conflict; episode.Provenance = "verified_event";
                SocialEpisodeRecord canonical = _episodes.AddOrMerge(episode);
                if (canonical == null) return null;
                if (active != null)
                    for (int i = 0; i < active.Count; i++)
                    {
                        SimSnapshot sim = active[i]; if (sim == null || !SocialKnowledgePolicy.CanKnow(canonical, sim, _scope)) continue;
                        SocialAffectState state = AffectFor(sim, activeSeconds); SocialAffectPolicy.Apply(state, canonical, sim.Name);
                        UpdateDriveFromEpisode(sim, canonical, activeSeconds);
                    }
                AddEvidenceFromEpisode(canonical);
                return canonical;
            
            }
        }

        internal void ObserveConversation(string speaker, string text, IList<SimSnapshot> active, double activeSeconds)
        {
            lock (_gate)
            {                if (string.IsNullOrWhiteSpace(text) || SocialMemoryPolicy.IsTrivialConversation(text)) return;
                SocialCurationEvidence item = new SocialCurationEvidence();
                item.Id = "chat-" + (++_evidenceSequence).ToString(); item.Kind = "chat"; item.SourceSystem = "chat"; item.CharacterScope = _scope;
                item.Utc = DateTime.UtcNow; item.RawConversationText = IdentitySchema.Bound(text, 420);
                item.FactualText = "Conversation evidence: " + IdentitySchema.Bound(speaker, 80) + " said a line in current party chat.";
                item.Participants = new List<string>(); item.KnownBy = new List<string>(); AddUnique(item.Participants, speaker); AddUnique(item.Participants, "player");
                AddUnique(item.KnownBy, "player");
                if (active != null) for (int i = 0; i < active.Count; i++) { SimSnapshot sim = active[i]; if (sim == null) continue; AddUnique(item.KnownBy, sim.Key); AddUnique(item.KnownBy, sim.Name); }
                string lower = text.ToLowerInvariant();
                item.HumorHint = Regex.IsMatch(lower, @"\b(?:lol|lmao|haha|heh|funny|joke|kidding|jk)\b") ? .65f : 0f;
                item.ConflictHint = Regex.IsMatch(lower, @"\b(?:idiot|stupid|shut up|hate|angry|mad at|annoying|sorry|apolog)\b") ? .55f : 0f;
                item.Importance = item.HumorHint > 0 || item.ConflictHint > 0 ? 48 : text.Length >= 90 ? 42 : 28;
                AddPending(item);
                // Conversation can alter transient affect only for deterministic, explicit surface cues;
                // relationship persistence waits for validated curation.
                if (active != null)
                    for (int i = 0; i < active.Count; i++)
                    {
                        SimSnapshot sim = active[i]; if (sim == null) continue; SocialAffectState state = AffectFor(sim, activeSeconds);
                        if (item.HumorHint > 0) state.Amusement = SocialAffectState.Clamp(state.Amusement + .12f);
                        if (Regex.IsMatch(lower, @"\b(?:sorry|apolog)\b")) { state.Irritation = Math.Max(0f, state.Irritation - .12f); state.Anger = Math.Max(0f, state.Anger - .10f); }
                    }
            
            }
        }

        internal bool ShouldQueueCuration(bool strongEvent, double activeSeconds)
        {
            lock (_gate)
            {                if (_pending.Count == 0 || activeSeconds < _nextCurationAllowedActiveSeconds) return false;
                if (strongEvent) return true;
                int meaningful = 0; for (int i = 0; i < _pending.Count; i++) if (_pending[i].Importance >= 35) meaningful++;
                return meaningful >= 5;
            
            }
        }

        internal int PendingEvidenceCount { get { lock (_gate) return _pending.Count; } }

        internal List<SocialCurationEvidence> SnapshotPending(int max)
        {
            lock (_gate)
            {                List<SocialCurationEvidence> result = new List<SocialCurationEvidence>();
                int start = Math.Max(0, _pending.Count - Math.Max(1, max)); for (int i = start; i < _pending.Count; i++) result.Add(_pending[i]); return result;
            
            }
        }
        internal string BuildCurationPrompt(IList<SocialCurationEvidence> evidence)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Return strict JSON only: {\"candidates\":[{\"significance\":0.0,\"category\":\"\",\"participants\":[],\"factual_basis\":\"\",\"interpretation\":\"\",\"funny\":false,\"conflict\":false,\"callback_candidate\":\"\",\"follow_up_topic\":\"\",\"evidence_ids\":[],\"confidence\":0.0}]}");
            sb.AppendLine("Classify only socially meaningful memories. Interpretive words such as funny, awkward, irritating, embarrassing or heartwarming are fictional social interpretation, not world facts. Never invent loot, guild authority, quests, party membership, Duel/PvP results, combat outcomes, deaths, or acquisitions. Routine acknowledgements should yield candidates:[]. Callback text must be a short concept grounded in evidence, never a new quote.");
            for (int i = 0; i < evidence.Count; i++)
            {
                SocialCurationEvidence e = evidence[i]; if (e == null) continue;
                sb.Append("EVIDENCE id=").Append(e.Id).Append(" kind=").Append(e.Kind).Append(" source=").Append(e.SourceSystem)
                    .Append(" importance=").Append(e.Importance).Append(" participants=").Append(Join(e.Participants)).Append(" knownBy=").Append(Join(e.KnownBy)).AppendLine();
                sb.Append("FACT=").Append(IdentitySchema.Bound(e.FactualText, 420)).AppendLine();
                if (!string.IsNullOrWhiteSpace(e.RawConversationText)) sb.Append("CHAT_TEXT=").Append(IdentitySchema.Bound(e.RawConversationText, 420)).AppendLine();
            }
            return sb.ToString();
        }

        internal void NoteCurationFailure(double activeSeconds, string reason)
        {
            lock (_gate)
            {                _lastCurationDecision = "failed:" + IdentitySchema.Bound(reason, 48); _nextCurationAllowedActiveSeconds = activeSeconds + 120.0;
            
            }
        }
        internal void NoteCurationResult(SocialCurationDisposition disposition, IList<SocialCurationEvidence> reviewed, double activeSeconds)
        {
            lock (_gate)
            {                _lastCurationDecision = disposition.ToString().ToLowerInvariant();
                if (disposition == SocialCurationDisposition.Accepted || disposition == SocialCurationDisposition.ExplicitEmpty) Consume(reviewed);
                _nextCurationAllowedActiveSeconds = activeSeconds + (disposition == SocialCurationDisposition.Invalid ? 120.0 : 45.0);
            
            }
        }

        internal SocialAffectState AffectFor(SimSnapshot sim, double activeSeconds)
        {
            lock (_gate)
            {                if (sim == null) return new SocialAffectState();
                string key = !string.IsNullOrWhiteSpace(sim.Key) ? sim.Key : sim.Name;
                SocialAffectState state; if (!_affect.TryGetValue(key, out state)) { state = new SocialAffectState(); _affect[key] = state; }
                state.Decay(activeSeconds); return state;
            
            }
        }
        internal SocialDriveState DriveFor(SimSnapshot sim, double activeSeconds)
        {
            lock (_gate)
            {                if (sim == null) return new SocialDriveState(); string key = !string.IsNullOrWhiteSpace(sim.Key) ? sim.Key : sim.Name;
                SocialDriveState drive; if (!_drives.TryGetValue(key, out drive) || drive == null || !drive.IsActive(activeSeconds)) return new SocialDriveState(); return drive;
            
            }
        }

        internal SocialPromptState BuildPromptState(SimSnapshot sim, SimMemory memory, string playerText, bool combat, bool unresolvedThread,
            string campContext, double activeSeconds, string opportunityKey)
        {
            lock (_gate)
            {                SocialPromptState result = new SocialPromptState(); SocialAffectState affect = AffectFor(sim, activeSeconds); result.Affect = affect.Describe();
                SocialDriveState drive = DriveFor(sim, activeSeconds); result.Drive = drive.IsActive(activeSeconds) ? drive.Key + (drive.Topic.Length > 0 ? ":" + drive.Topic : string.Empty) : "none";
                result.CampBlock = IdentitySchema.Bound(campContext, 500);
                List<StructuredMemoryRecord> social = SelectSocialMemories(memory, sim, playerText, 2);
                if (social.Count > 0)
                {
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < social.Count; i++)
                    {
                        StructuredMemoryRecord m = social[i];
                        sb.Append("FACT: ").Append(IdentitySchema.Bound(m.FactSummary.Length > 0 ? m.FactSummary : m.Text, 260));
                        if (m.InterpretationSummary.Length > 0) sb.Append(" | THIS SIM'S INTERPRETATION (roleplay, not verified fact): ").Append(IdentitySchema.Bound(m.InterpretationSummary, 180));
                        sb.AppendLine();
                    }
                    result.MemoryBlock = sb.ToString().Trim();
                }
                string simKey = sim == null ? string.Empty : (!string.IsNullOrWhiteSpace(sim.Key) ? sim.Key : sim.Name);
                double lastPivot; bool recentPivot = _lastPivotBySim.TryGetValue(simKey, out lastPivot) && activeSeconds - lastPivot < SocialPivotPolicy.PivotCooldownSeconds;
                string topicKey = NormalizeTopic(playerText); double lastTopic; bool topicFatigued = topicKey.Length > 0 && _lastTopicByKey.TryGetValue(simKey + "|" + topicKey, out lastTopic) && activeSeconds - lastTopic < SocialPivotPolicy.TopicFatigueSeconds;
                bool opportunity = SocialPivotPolicy.HasOpportunity(opportunityKey);
                if (opportunity && SocialPivotPolicy.CanOffer(combat, playerText, unresolvedThread, recentPivot, topicFatigued, affect))
                {
                    StructuredMemoryRecord seedMemory = SelectSeedMemory(social, activeSeconds);
                    if (seedMemory != null)
                    {
                        result.OptionalSeed = SeedText(seedMemory); result.OptionalSeedId = seedMemory.Id;
                    }
                    else if (drive.IsActive(activeSeconds))
                    {
                        result.OptionalSeed = "active social drive: " + drive.Key + (drive.Topic.Length > 0 ? " about " + drive.Topic : string.Empty); result.OptionalSeedId = "drive:" + drive.Key;
                    }
                }
                return result;
            
            }
        }

        internal void NotePivotIfUsed(SimSnapshot sim, SocialPromptState state, string output, string playerText, double activeSeconds)
        {
            lock (_gate)
            {                if (sim == null || state == null || state.OptionalSeed == "NONE" || !SocialPivotPolicy.OutputUsesSeed(output, state.OptionalSeed)) return;
                string key = !string.IsNullOrWhiteSpace(sim.Key) ? sim.Key : sim.Name; _lastPivotBySim[key] = activeSeconds;
                string topic = NormalizeTopic(playerText); if (topic.Length > 0) _lastTopicByKey[key + "|" + topic] = activeSeconds;
                if (state.OptionalSeedId.StartsWith("inside:", StringComparison.Ordinal)) _lastInsideJokeById[state.OptionalSeedId.Substring(7)] = activeSeconds;
            
            }
        }

        internal string DescribeStatus(SimSnapshot sim, double activeSeconds)
        {
            lock (_gate)
            {                SocialAffectState affect = AffectFor(sim, activeSeconds); SocialDriveState drive = DriveFor(sim, activeSeconds);
                List<SocialEpisodeRecord> recent = _episodes.Recent(3); List<string> ids = new List<string>(); for (int i = 0; i < recent.Count; i++) ids.Add(recent[i].EpisodeId + ":" + recent[i].SourceSystem);
                return "episodes=" + _episodes.Count + "; recent=" + (ids.Count == 0 ? "none" : string.Join(",", ids.ToArray())) + "; affect=" + affect.Describe() +
                    "; drive=" + (drive.IsActive(activeSeconds) ? drive.Key : "none") + "; pendingEvidence=" + _pending.Count + "; curation=" + _lastCurationDecision;
            
            }
        }

        private void AddEvidenceFromEpisode(SocialEpisodeRecord episode)
        {
            SocialCurationEvidence item = new SocialCurationEvidence(); item.Id = episode.EpisodeId; item.Kind = "verified_event"; item.SourceSystem = episode.SourceSystem;
            item.CharacterScope = episode.CharacterScope; item.FactualText = episode.FactualSummary; item.RawConversationText = string.Empty;
            item.Participants = new List<string>(episode.Participants); item.KnownBy = new List<string>(episode.KnownBy); item.Importance = episode.Importance;
            item.HumorHint = episode.HumorScore; item.ConflictHint = episode.ConflictScore; item.Utc = DateTime.UtcNow; AddPending(item);
        }
        private void AddPending(SocialCurationEvidence item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id)) return;
            // Correlated adapters may enrich the same factual episode more than once (for example
            // PvP lifecycle + Nemesis rivalry metadata). Keep one pending evidence item per episode ID.
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                SocialCurationEvidence existing = _pending[i];
                if (existing != null && string.Equals(existing.Id, item.Id, StringComparison.Ordinal))
                {
                    _pending[i] = item;
                    return;
                }
            }
            _pending.Add(item); while (_pending.Count > 18) _pending.RemoveAt(0);
        }
        private void Consume(IList<SocialCurationEvidence> reviewed)
        {
            if (reviewed == null) return; HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < reviewed.Count; i++) if (reviewed[i] != null) ids.Add(reviewed[i].Id);
            for (int i = _pending.Count - 1; i >= 0; i--) if (ids.Contains(_pending[i].Id)) _pending.RemoveAt(i);
        }
        private void UpdateDriveFromEpisode(SimSnapshot sim, SocialEpisodeRecord episode, double activeSeconds)
        {
            string tags = string.Join(" ", episode.Tags.ToArray()).ToLowerInvariant(); string key = string.Empty, topic = string.Empty; double ttl = 480.0;
            if (tags.Contains("argument") || tags.Contains("insult")) { key = "resolve_tension"; topic = episode.FactualSummary; ttl = 600; }
            else if (tags.Contains("pvp") || tags.Contains("danger")) { key = "discuss_recent_danger"; topic = episode.FactualSummary; ttl = 900; }
            else if (tags.Contains("duel")) { key = "talk_about_spar"; topic = episode.FactualSummary; ttl = 720; }
            else if (tags.Contains("humor")) { key = "joke"; topic = episode.FactualSummary; ttl = 420; }
            else if (episode.Importance >= 65) { key = "reminisce"; topic = episode.FactualSummary; ttl = 900; }
            if (key.Length == 0) return; string simKey = !string.IsNullOrWhiteSpace(sim.Key) ? sim.Key : sim.Name;
            _drives[simKey] = new SocialDriveState { Key = key, Topic = IdentitySchema.Bound(topic, 120), EvidenceId = episode.EpisodeId, ExpiresAtActiveSeconds = activeSeconds + ttl };
        }
        private StructuredMemoryRecord SelectSeedMemory(List<StructuredMemoryRecord> social, double activeSeconds)
        {
            for (int i = 0; i < social.Count; i++)
            {
                StructuredMemoryRecord m = social[i]; if (m == null) continue;
                if (m.InsideJoke)
                {
                    double last; if (_lastInsideJokeById.TryGetValue(m.Id, out last) && activeSeconds - last < SocialPivotPolicy.InsideJokeCooldownSeconds) continue;
                    return m;
                }
                if (m.Importance >= 55) return m;
            }
            return null;
        }
        private static string SeedText(StructuredMemoryRecord memory)
        {
            if (memory == null) return "NONE";
            if (memory.InsideJoke && memory.CallbackConcept.Length > 0) return "shared inside-joke callback concept: " + memory.CallbackConcept;
            if (memory.Subject.Length > 0) return "grounded follow-up topic: " + memory.Subject;
            return "grounded shared memory: " + IdentitySchema.Bound(memory.FactSummary.Length > 0 ? memory.FactSummary : memory.Text, 120);
        }
        private static List<StructuredMemoryRecord> SelectSocialMemories(SimMemory memory, SimSnapshot owner, string topic, int max)
        {
            List<StructuredMemoryRecord> result = new List<StructuredMemoryRecord>(); if (memory == null || owner == null || memory.StructuredMemories == null) return result;
            HashSet<string> query = Tokens(topic); List<StructuredMemoryRecord> candidates = new List<StructuredMemoryRecord>();
            for (int i = memory.StructuredMemories.Count - 1; i >= 0; i--)
            {
                StructuredMemoryRecord m = memory.StructuredMemories[i]; if (m == null || !MemoryKnowledgePolicy.CanUse(m, owner)) continue; m.Normalize();
                if (!m.Source.StartsWith("social_", StringComparison.OrdinalIgnoreCase) && !m.InsideJoke) continue; candidates.Add(m);
            }
            candidates.Sort(delegate(StructuredMemoryRecord a, StructuredMemoryRecord b)
            {
                int ao = Overlap(query, Tokens(a.FactSummary + " " + a.InterpretationSummary + " " + a.Subject)); int bo = Overlap(query, Tokens(b.FactSummary + " " + b.InterpretationSummary + " " + b.Subject));
                int compare = bo.CompareTo(ao); if (compare != 0) return compare; return b.Importance.CompareTo(a.Importance);
            });
            for (int i = 0; i < candidates.Count && result.Count < max; i++) result.Add(candidates[i]); return result;
        }
        private static int Overlap(HashSet<string> a, HashSet<string> b) { int n = 0; foreach (string v in a) if (b.Contains(v)) n++; return n; }
        private static HashSet<string> Tokens(string text) { HashSet<string> s = new HashSet<string>(StringComparer.OrdinalIgnoreCase); string[] p = Regex.Split((text ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9']+"); for (int i = 0; i < p.Length; i++) if (p[i].Length >= 4) s.Add(p[i]); return s; }
        private static string NormalizeTopic(string text) { List<string> p = new List<string>(Tokens(text)); p.Sort(StringComparer.Ordinal); if (p.Count > 4) p.RemoveRange(4, p.Count - 4); return string.Join("_", p.ToArray()); }
        private static string Join(List<string> values) { return values == null ? string.Empty : string.Join(",", values.ToArray()); }
        private static void AddUnique(List<string> values, string value) { if (values == null || string.IsNullOrWhiteSpace(value)) return; for (int i = 0; i < values.Count; i++) if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase)) return; values.Add(value.Trim()); }
    }
}
