using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    internal sealed class RelationshipTone
    {
        internal float Familiarity;
        internal float Rapport;
        internal float Rivalry;
        internal string FamiliarityLabel;
        internal string RapportLabel;
        internal string RivalryLabel;
    }

    // Relationship values are bounded tone controls derived from persisted observations. They are
    // never game facts and never authorize claims about friendship, past events, or biographies.
    internal static class RelationshipModel
    {
        internal const int CurrentVersion = 1;
        private const int MaximumCounter = 100000;

        internal static void Normalize(SimMemory memory)
        {
            if (memory == null) return;
            bool migrating = memory.RelationshipDataVersion < CurrentVersion;
            memory.GroupSessions = Counter(memory.GroupSessions);
            memory.CompletedOutings = Counter(memory.CompletedOutings);
            memory.TotalGroupedMinutes = Counter(memory.TotalGroupedMinutes);
            memory.ConversationExchanges = Counter(memory.ConversationExchanges);
            memory.PositivePlayerExchanges = Counter(memory.PositivePlayerExchanges);
            memory.CompetitivePlayerExchanges = Counter(memory.CompetitivePlayerExchanges);
            memory.VerifiedPracticeDuels = Counter(memory.VerifiedPracticeDuels);
            memory.Familiarity = Unit(memory.Familiarity);
            memory.Rapport = Unit(memory.Rapport);
            memory.Rivalry = Unit(memory.Rivalry);
            if (migrating)
            {
                if (memory.CompletedOutings == 0 && memory.OutingSummaries != null) memory.CompletedOutings = memory.OutingSummaries.Count;
                int completedOutings = memory.CompletedOutings;
                memory.Familiarity = Unit(Math.Min(0.55f, memory.TotalGroupedMinutes / 600f) +
                    Math.Min(0.30f, completedOutings * 0.025f) + Math.Min(0.15f, memory.ConversationExchanges * 0.003f));
                memory.Rapport = Unit(Math.Min(0.35f, memory.ConversationExchanges * 0.002f) +
                    Math.Min(0.30f, memory.PositivePlayerExchanges * 0.018f));
                memory.Rivalry = Unit(Math.Min(0.42f, memory.VerifiedPracticeDuels * 0.06f) +
                    Math.Min(0.18f, memory.CompetitivePlayerExchanges * 0.012f));
            }

            if (memory.SimRelationships != null)
            {
                for (int i = memory.SimRelationships.Count - 1; i >= 0; i--)
                {
                    SimRelationshipMemory relation = memory.SimRelationships[i];
                    if (relation == null)
                    {
                        memory.SimRelationships.RemoveAt(i);
                        continue;
                    }
                    Normalize(relation);
                    if (migrating)
                    {
                        relation.Familiarity = Unit(Math.Min(0.55f, relation.SharedMinutes / 600f) +
                            Math.Min(0.30f, relation.SharedOutings * 0.03f) + Math.Min(0.15f, relation.SharedConversationThreads * 0.004f));
                        relation.Rapport = Unit(Math.Min(0.35f, relation.SharedConversationThreads * 0.005f) +
                            Math.Min(0.25f, relation.PositiveExchanges * 0.015f));
                        relation.Rivalry = Unit(Math.Min(0.42f, relation.VerifiedPracticeDuels * 0.06f) +
                            Math.Min(0.23f, relation.CompetitiveExchanges * 0.012f));
                    }
                }
            }
            memory.RelationshipDataVersion = CurrentVersion;
        }

        internal static void Normalize(SimRelationshipMemory relation)
        {
            if (relation == null) return;
            relation.OtherSimKey = relation.OtherSimKey ?? string.Empty;
            relation.OtherName = relation.OtherName ?? string.Empty;
            relation.LastSharedUtc = relation.LastSharedUtc ?? string.Empty;
            relation.SharedOutings = Counter(relation.SharedOutings);
            relation.SharedMinutes = Counter(relation.SharedMinutes);
            relation.SharedConversationThreads = Counter(relation.SharedConversationThreads);
            relation.PositiveExchanges = Counter(relation.PositiveExchanges);
            relation.CompetitiveExchanges = Counter(relation.CompetitiveExchanges);
            relation.VerifiedPracticeDuels = Counter(relation.VerifiedPracticeDuels);
            relation.Familiarity = Unit(relation.Familiarity);
            relation.Rapport = Unit(relation.Rapport);
            relation.Rivalry = Unit(relation.Rivalry);
        }

        internal static void RefreshPlayer(SimMemory memory, SimSnapshot sim)
        {
            if (memory == null) return;
            Normalize(memory);
            int completedOutings = memory.CompletedOutings;
            float familiarity = Math.Min(0.55f, memory.TotalGroupedMinutes / 600f) +
                Math.Min(0.30f, completedOutings * 0.025f) +
                Math.Min(0.15f, memory.ConversationExchanges * 0.003f);
            float rapport = Math.Min(0.35f, memory.ConversationExchanges * 0.002f) +
                Math.Min(0.30f, memory.PositivePlayerExchanges * 0.018f);
            float rivalry = Math.Min(0.42f, memory.VerifiedPracticeDuels * 0.06f) +
                Math.Min(0.18f, memory.CompetitivePlayerExchanges * 0.012f);
            if (sim != null && sim.Rival) rivalry += 0.18f;
            memory.Familiarity = Unit(familiarity);
            memory.Rapport = Unit(rapport);
            memory.Rivalry = Unit(rivalry);
            memory.RelationshipDataVersion = CurrentVersion;
        }

        internal static void RefreshPair(SimRelationshipMemory relation, SimSnapshot owner, SimSnapshot other)
        {
            if (relation == null) return;
            Normalize(relation);
            float familiarity = Math.Min(0.55f, relation.SharedMinutes / 600f) +
                Math.Min(0.30f, relation.SharedOutings * 0.03f) +
                Math.Min(0.15f, relation.SharedConversationThreads * 0.004f);
            float rapport = Math.Min(0.35f, relation.SharedConversationThreads * 0.005f) +
                Math.Min(0.25f, relation.PositiveExchanges * 0.015f);
            float rivalry = Math.Min(0.42f, relation.VerifiedPracticeDuels * 0.06f) +
                Math.Min(0.23f, relation.CompetitiveExchanges * 0.012f);
            if ((owner != null && owner.Rival) || (other != null && other.Rival)) rivalry += 0.15f;
            relation.Familiarity = Unit(familiarity);
            relation.Rapport = Unit(rapport);
            relation.Rivalry = Unit(rivalry);
        }

        internal static RelationshipTone Describe(SimMemory memory)
        {
            if (memory == null) return Describe(0f, 0f, 0f);
            Normalize(memory);
            return Describe(memory.Familiarity, memory.Rapport, memory.Rivalry);
        }

        internal static RelationshipTone Describe(SimRelationshipMemory relation)
        {
            if (relation == null) return Describe(0f, 0f, 0f);
            Normalize(relation);
            return Describe(relation.Familiarity, relation.Rapport, relation.Rivalry);
        }

        internal static RelationshipTone Describe(float familiarity, float rapport, float rivalry)
        {
            RelationshipTone tone = new RelationshipTone();
            tone.Familiarity = Unit(familiarity);
            tone.Rapport = Unit(rapport);
            tone.Rivalry = Unit(rivalry);
            tone.FamiliarityLabel = tone.Familiarity < 0.12f ? "new" : tone.Familiarity < 0.32f ? "acquainted" : tone.Familiarity < 0.58f ? "familiar" : "established";
            tone.RapportLabel = tone.Rapport < 0.10f ? "neutral" : tone.Rapport < 0.30f ? "mild" : tone.Rapport < 0.55f ? "warm" : "strong";
            tone.RivalryLabel = tone.Rivalry < 0.15f ? "low" : tone.Rivalry < 0.35f ? "mild" : tone.Rivalry < 0.60f ? "moderate" : "strong";
            return tone;
        }

        internal static bool IsPositiveAcknowledgement(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string value = " " + text.Trim().ToLowerInvariant() + " ";
            string[] phrases = new string[] { " thanks ", " thank you ", " ty ", " grats ", " congrats ", " congratulations ", " good job ", " nice job ", " well done ", " gg " };
            for (int i = 0; i < phrases.Length; i++) if (value.IndexOf(phrases[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        internal static List<string> RunSelfTests()
        {
            List<string> results = new List<string>();
            SimMemory source = new SimMemory();
            source.SimKey = "Dancer";
            source.Name = "Dancer";
            source.TotalGroupedMinutes = 95;
            source.ConversationExchanges = 12;
            source.PositivePlayerExchanges = 3;
            source.VerifiedPracticeDuels = 1;
            source.OutingSummaries = new List<string>();
            for (int i = 0; i < 8; i++) source.OutingSummaries.Add("Verified outing " + i + ".");
            source.SimRelationships = new List<SimRelationshipMemory>();
            source.SimRelationships.Add(new SimRelationshipMemory { OtherSimKey = "Phanty", OtherName = "Phanty", SharedOutings = 8, SharedMinutes = 95, SharedConversationThreads = 6 });
            RefreshPlayer(source, new SimSnapshot { Rival = true });
            RefreshPair(source.SimRelationships[0], new SimSnapshot { Rival = true }, new SimSnapshot());
            SimMemory reloaded = SimMemoryPersistence.Clone(source);
            if (reloaded != null) reloaded.Normalize();
            bool persisted = reloaded != null && reloaded.RelationshipDataVersion == CurrentVersion &&
                reloaded.VerifiedPracticeDuels == 1 && reloaded.SimRelationships != null && reloaded.SimRelationships.Count == 1 &&
                reloaded.SimRelationships[0].SharedOutings == 8 && reloaded.Familiarity > 0f;
            results.Add("[DeepSims Relationship " + (persisted ? "PASS" : "FAIL") + "] persistence JSON round-trip");

            SimMemory malformed = new SimMemory();
            malformed.Familiarity = float.NaN;
            malformed.Rapport = float.PositiveInfinity;
            malformed.Rivalry = -4f;
            malformed.TotalGroupedMinutes = -50;
            malformed.SimRelationships = new List<SimRelationshipMemory>();
            malformed.SimRelationships.Add(new SimRelationshipMemory { SharedOutings = -2, SharedMinutes = int.MaxValue, Rapport = float.NaN });
            malformed.Normalize();
            bool safe = malformed.Familiarity == 0f && malformed.Rapport == 0f && malformed.Rivalry == 0f && malformed.TotalGroupedMinutes == 0 &&
                malformed.SimRelationships[0].SharedOutings == 0 && malformed.SimRelationships[0].SharedMinutes == MaximumCounter && malformed.SimRelationships[0].Rapport == 0f;
            results.Add("[DeepSims Relationship " + (safe ? "PASS" : "FAIL") + "] malformed state fails safely");
            results.Add("[DeepSims Relationship " + (IsPositiveAcknowledgement("nice job") && !IsPositiveAcknowledgement("we killed a boss") ? "PASS" : "FAIL") + "] conservative positive acknowledgement classification");
            return results;
        }

        private static int Counter(int value)
        {
            if (value < 0) return 0;
            return value > MaximumCounter ? MaximumCounter : value;
        }

        private static float Unit(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
