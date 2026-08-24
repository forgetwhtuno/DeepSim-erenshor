using System;
using System.Collections.Generic;
using System.Reflection;

namespace ErenshorDeepSims
{
    internal static class SimMemoryPersistenceDeterministicTests
    {
        internal static List<string> Run()
        {
            List<string> lines = new List<string>();
            try
            {
                Add(lines, "DTO field names cover every current SimMemory field", ContractCoversRuntimeModel());
                SimMemory full = FullFixture();
                SimMemory roundTrip = SimMemoryPersistence.Deserialize(SimMemoryPersistence.Serialize(full));
                Add(lines, "StructuredMemories preserve every structured field", RecordEqual(full.StructuredMemories[0], roundTrip.StructuredMemories[0]));
                Add(lines, "RecentEvents preserve nonempty legacy events", roundTrip.RecentEvents.Count == 1 &&
                    roundTrip.RecentEvents[0].utc == "2026-08-24T01:02:03Z" && roundTrip.RecentEvents[0].importance == 77);
                Add(lines, "Preferences preserve multiple topics", roundTrip.Preferences.Count == 2 &&
                    roundTrip.Preferences[0].TopicKey == "zones" && roundTrip.Preferences[1].TimesExpressed == 4);
                Add(lines, "SimRelationships preserve multiple counters", roundTrip.SimRelationships.Count == 2 &&
                    roundTrip.SimRelationships[0].SharedOutings == 8 && roundTrip.SimRelationships[1].CompetitiveExchanges == 6);
                Add(lines, "SocialRelationships preserve entries and applied episodes", roundTrip.SocialRelationships.Count == 1 &&
                    roundTrip.SocialRelationships[0].AppliedEpisodeIds.Count == 2 && roundTrip.SocialRelationships[0].Trust == 0.6f);
                Add(lines, "AuthoredIdentity preserves nested authored records", roundTrip.AuthoredIdentity != null &&
                    roundTrip.AuthoredIdentity.CorePersonality == "dry and loyal" &&
                    roundTrip.AuthoredIdentity.SharedHistory.Count == 1 && roundTrip.AuthoredIdentity.PinnedMemories.Count == 1 &&
                    RecordEqual(full.AuthoredIdentity.SharedHistory[0], roundTrip.AuthoredIdentity.SharedHistory[0]));
                Add(lines, "combined legacy and structured SimMemory is semantically stable", SemanticEqual(full, roundTrip));

                const string legacy = "{\"SimKey\":\"legacy-sim\",\"Name\":\"Legacy\",\"GroupSessions\":3," +
                    "\"RelationshipDataVersion\":1,\"IdentityDataVersion\":1,\"ImportantMemories\":[\"legacy memory\"]," +
                    "\"RecentGroupChat\":[\"legacy chat\"],\"OutingSummaries\":[\"legacy outing\"]," +
                    "\"LastOutingUtc\":\"2026-08-20T00:00:00Z\",\"TotalGroupedMinutes\":44," +
                    "\"ConversationSummaries\":[\"legacy summary\"]}";
                SimMemory legacyLoaded = SimMemoryPersistence.Deserialize(legacy);
                string migrated = SimMemoryPersistence.Serialize(legacyLoaded);
                JsonFieldPresenceObservation migratedFields = JsonFieldPresenceObservation.Inspect(migrated);
                Add(lines, "legacy-only JSON loads, preserves old data, and emits full schema",
                    legacyLoaded != null && legacyLoaded.GroupSessions == 3 && legacyLoaded.ImportantMemories.Count == 1 &&
                    legacyLoaded.RecentGroupChat.Count == 1 && legacyLoaded.OutingSummaries.Count == 1 &&
                    legacyLoaded.ConversationSummaries.Count == 1 && legacyLoaded.StructuredMemories.Count == 0 &&
                    legacyLoaded.AuthoredIdentity != null && migratedFields.ContainsStructuredMemories &&
                    migratedFields.ContainsRecentEvents && migratedFields.ContainsPreferences &&
                    migratedFields.ContainsSimRelationships && migratedFields.ContainsSocialRelationships &&
                    migratedFields.ContainsAuthoredIdentity);

                SimMemory empty = new SimMemory { SimKey = "empty", Name = "Empty" };
                empty.Normalize();
                SimMemory emptyRoundTrip = SimMemoryPersistence.Clone(empty);
                Add(lines, "empty collections round-trip as initialized empty collections", emptyRoundTrip != null &&
                    emptyRoundTrip.StructuredMemories.Count == 0 && emptyRoundTrip.RecentEvents.Count == 0 &&
                    emptyRoundTrip.Preferences.Count == 0 && emptyRoundTrip.SimRelationships.Count == 0 &&
                    emptyRoundTrip.SocialRelationships.Count == 0 && emptyRoundTrip.Conversation.Count == 0 &&
                    emptyRoundTrip.AuthoredIdentity != null);

                SimMemory twice = SimMemoryPersistence.Clone(SimMemoryPersistence.Clone(full));
                Add(lines, "multiple save/load cycles neither lose nor multiply records", SemanticEqual(full, twice) &&
                    twice.StructuredMemories.Count == 1 && twice.RecentEvents.Count == 1 && twice.Preferences.Count == 2 &&
                    twice.SimRelationships.Count == 2 && twice.SocialRelationships.Count == 1 && twice.Conversation.Count == 2);
            }
            catch (Exception ex)
            {
                Add(lines, "persistence tests complete without exception", false, ex.GetType().Name + ": " + ex.Message);
            }
            return lines;
        }

        private static SimMemory FullFixture()
        {
            SimMemory m = new SimMemory
            {
                SimKey = "fixture-sim", Name = "Fixture", FirstSeenUtc = "2026-08-01T00:00:00Z",
                LastSeenUtc = "2026-08-24T02:03:04Z", LastKnownScene = "Hidden", LastKnownClass = "Druid",
                LastKnownPersonality = "Patient", LastKnownLevel = 23, GroupSessions = 9, CompletedOutings = 4,
                Familiarity = 0.7f, Rapport = 0.5f, Rivalry = 0.2f, ConversationExchanges = 14,
                PositivePlayerExchanges = 5, CompetitivePlayerExchanges = 2, VerifiedPracticeDuels = 1,
                RelationshipDataVersion = RelationshipModel.CurrentVersion, IdentityDataVersion = IdentitySchema.CurrentVersion,
                GeneratedSimulatedPlayerBackground = "fictional background", GeneratedSimulatedPlayerBackgroundVersion = "bg-v1",
                GeneratedLongTermWants = "see distant zones", GeneratedCaresAbout = "the party",
                GeneratedIdentityMotivationVersion = "motivation-v1", LastOutingUtc = "2026-08-23T00:00:00Z",
                TotalGroupedMinutes = 321
            };
            m.Normalize();
            StructuredMemoryRecord learned = Record("learned-1", false, false);
            m.StructuredMemories.Add(learned);
            m.RecentEvents.Add(new MemoryEvent { utc = "2026-08-24T01:02:03Z", type = "quest_complete", text = "verified event", importance = 77 });
            m.ImportantMemories.Add("legacy important memory");
            m.RecentGroupChat.Add("legacy recent group chat");
            m.Conversation.Add(new ChatMessage("user", "legacy player line"));
            m.Conversation.Add(new ChatMessage("assistant", "legacy Sim line"));
            m.OutingSummaries.Add("legacy outing summary");
            m.ConversationSummaries.Add("legacy conversation summary");
            m.Preferences.Add(new SimPreferenceMemory { TopicKey = "zones", Statement = "prefers ruins", TimesExpressed = 2, UpdatedUtc = "2026-08-22T00:00:00Z" });
            m.Preferences.Add(new SimPreferenceMemory { TopicKey = "roles", Statement = "likes support", TimesExpressed = 4, UpdatedUtc = "2026-08-23T00:00:00Z" });
            m.SimRelationships.Add(new SimRelationshipMemory { OtherSimKey = "other-a", OtherName = "Other A", SharedOutings = 8,
                SharedMinutes = 210, SharedConversationThreads = 7, PositiveExchanges = 4, CompetitiveExchanges = 1,
                VerifiedPracticeDuels = 2, Familiarity = 0.8f, Rapport = 0.6f, Rivalry = 0.3f, LastSharedUtc = "2026-08-23T00:00:00Z" });
            m.SimRelationships.Add(new SimRelationshipMemory { OtherSimKey = "other-b", OtherName = "Other B", SharedOutings = 3,
                SharedMinutes = 90, SharedConversationThreads = 2, PositiveExchanges = 1, CompetitiveExchanges = 6,
                VerifiedPracticeDuels = 1, Familiarity = 0.4f, Rapport = 0.1f, Rivalry = 0.7f, LastSharedUtc = "2026-08-22T00:00:00Z" });
            m.SocialRelationships.Add(new SocialRelationshipMemory { OtherSimKey = "other-a", OtherName = "Other A",
                Familiarity = 0.8f, Warmth = 0.4f, Trust = 0.6f, Tension = 0.2f, Rivalry = 0.3f,
                MeaningfulEpisodes = 5, UpdatedUtc = "2026-08-24T00:00:00Z",
                AppliedEpisodeIds = new List<string> { "episode-1", "episode-2" } });
            m.AuthoredIdentity.CorePersonality = "dry and loyal";
            m.AuthoredIdentity.PersonalBackground = "a careful traveler";
            m.AuthoredIdentity.ErenshorPersona = "quiet guide";
            m.AuthoredIdentity.RelationshipToPlayer = "trusted companion";
            m.AuthoredIdentity.LongTermWants = "finish the old road";
            m.AuthoredIdentity.CaresAbout = "keeping friends safe";
            m.AuthoredIdentity.SharedHistory.Add(Record("history-1", true, false));
            m.AuthoredIdentity.PinnedMemories.Add(Record("pinned-1", true, true));
            m.Normalize();
            return m;
        }

        private static StructuredMemoryRecord Record(string id, bool authored, bool pinned)
        {
            StructuredMemoryRecord r = new StructuredMemoryRecord
            {
                Id = id, MemoryType = authored ? "authored_history" : "episodic", Text = "structured factual evidence",
                Subject = "subject", Importance = 83, Participants = new List<string> { "fixture-sim", "other-a" },
                KnownBy = new List<string> { "fixture-sim", "other-a", "player" }, Topics = new List<string> { "quest", "ruins" },
                EmotionalTags = new List<string> { "amused" }, Utc = "2026-08-24T01:00:00Z", Source = "verified_event:test",
                EpisodeId = "episode-42", SourceSystem = "quest", SourceCorrelationId = "correlation-42",
                FactSummary = "bounded fact", InterpretationSummary = "private interpretation", CharacterScope = "slot0_fixture",
                MemoryTier = pinned ? "pinned" : "significant", EvidenceIds = new List<string> { "evidence-1", "evidence-2" },
                CallbackConcept = "old road", InsideJoke = true, RecurrenceCount = 3, HumorScore = 0.7f,
                ConflictScore = 0.2f, LastRecalledUtc = "2026-08-24T02:00:00Z", OwnerSimKey = "fixture-sim",
                OwnerSimName = "Fixture", ExplicitPublic = false, Authored = authored, Pinned = pinned
            };
            r.Normalize();
            return r;
        }

        private static bool SemanticEqual(SimMemory a, SimMemory b)
        { return a != null && b != null && string.Equals(SimMemoryPersistence.Serialize(a), SimMemoryPersistence.Serialize(b), StringComparison.Ordinal); }

        private static bool ContractCoversRuntimeModel()
        {
            FieldInfo[] modelFields = typeof(SimMemory).GetFields(BindingFlags.Instance | BindingFlags.Public);
            FieldInfo[] dtoFields = typeof(SimMemoryPersistenceDto).GetFields(BindingFlags.Instance | BindingFlags.Public);
            HashSet<string> dtoNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < dtoFields.Length; i++) dtoNames.Add(dtoFields[i].Name);
            if (modelFields.Length != dtoNames.Count) return false;
            for (int i = 0; i < modelFields.Length; i++) if (!dtoNames.Contains(modelFields[i].Name)) return false;
            return true;
        }

        private static bool RecordEqual(StructuredMemoryRecord a, StructuredMemoryRecord b)
        {
            if (a == null || b == null) return false;
            return a.Id == b.Id && a.MemoryType == b.MemoryType && a.Text == b.Text && a.Subject == b.Subject &&
                a.Importance == b.Importance && Equal(a.Participants, b.Participants) && Equal(a.KnownBy, b.KnownBy) &&
                Equal(a.Topics, b.Topics) && Equal(a.EmotionalTags, b.EmotionalTags) && a.Utc == b.Utc &&
                a.Source == b.Source && a.EpisodeId == b.EpisodeId && a.SourceSystem == b.SourceSystem &&
                a.SourceCorrelationId == b.SourceCorrelationId && a.FactSummary == b.FactSummary &&
                a.InterpretationSummary == b.InterpretationSummary && a.CharacterScope == b.CharacterScope &&
                a.MemoryTier == b.MemoryTier && Equal(a.EvidenceIds, b.EvidenceIds) && a.CallbackConcept == b.CallbackConcept &&
                a.InsideJoke == b.InsideJoke && a.RecurrenceCount == b.RecurrenceCount && a.HumorScore == b.HumorScore &&
                a.ConflictScore == b.ConflictScore && a.LastRecalledUtc == b.LastRecalledUtc &&
                a.OwnerSimKey == b.OwnerSimKey && a.OwnerSimName == b.OwnerSimName &&
                a.ExplicitPublic == b.ExplicitPublic && a.Authored == b.Authored && a.Pinned == b.Pinned;
        }

        private static bool Equal(List<string> a, List<string> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static void Add(List<string> lines, string name, bool pass, string detail = "")
        { lines.Add("[SimMemoryPersistence " + (pass ? "PASS" : "FAIL") + "] " + name + (pass || detail.Length == 0 ? string.Empty : " (" + detail + ")")); }
    }
}
