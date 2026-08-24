using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ErenshorDeepSims
{
    // SimMemory is intentionally persisted through an explicit contract instead of JsonUtility.
    // The current Unity runtime serializes SimMemory's primitive/string/List<string> fields but
    // omits every custom reference and List<custom-type> field. Keeping the persistence shape here
    // preserves the runtime model and the existing JSON field names without changing cognition.
    internal static class SimMemoryPersistence
    {
        internal static SimMemory ReadFile(string path)
        {
            return Deserialize(File.ReadAllText(path, Encoding.UTF8));
        }

        internal static JsonWriteObservation WriteFile(string path, SimMemory value)
        {
            string parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            string json = Serialize(value);
            File.WriteAllText(path, json, new UTF8Encoding(false));
            return new JsonWriteObservation { Fields = JsonFieldPresenceObservation.Inspect(json) };
        }

        internal static string Serialize(SimMemory value)
        {
            SimMemoryPersistenceDto dto = SimMemoryPersistenceDto.FromModel(value ?? new SimMemory());
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(SimMemoryPersistenceDto));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, dto);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        internal static SimMemory Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(SimMemoryPersistenceDto));
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                SimMemoryPersistenceDto dto = serializer.ReadObject(stream) as SimMemoryPersistenceDto;
                return dto == null ? null : dto.ToModel();
            }
        }

        internal static SimMemory Clone(SimMemory value)
        {
            return value == null ? null : Deserialize(Serialize(value));
        }

        internal static MemoryCollectionObservation CaptureDto(SimMemory value)
        {
            SimMemoryPersistenceDto dto = SimMemoryPersistenceDto.FromModel(value ?? new SimMemory());
            return new MemoryCollectionObservation
            {
                StructuredMemories = Count(dto.StructuredMemories),
                RecentEvents = Count(dto.RecentEvents),
                Preferences = Count(dto.Preferences),
                SimRelationships = Count(dto.SimRelationships),
                SocialRelationships = Count(dto.SocialRelationships),
                AuthoredIdentity = dto.AuthoredIdentity != null
            };
        }

        private static int Count<T>(List<T> value) { return value == null ? -1 : value.Count; }
    }

    [DataContract]
    internal sealed class SimMemoryPersistenceDto
    {
        [DataMember(Order = 1)] public string SimKey;
        [DataMember(Order = 2)] public string Name;
        [DataMember(Order = 3)] public string FirstSeenUtc;
        [DataMember(Order = 4)] public string LastSeenUtc;
        [DataMember(Order = 5)] public string LastKnownScene;
        [DataMember(Order = 6)] public string LastKnownClass;
        [DataMember(Order = 7)] public string LastKnownPersonality;
        [DataMember(Order = 8)] public int LastKnownLevel;
        [DataMember(Order = 9)] public int GroupSessions;
        [DataMember(Order = 10)] public int CompletedOutings;
        [DataMember(Order = 11)] public float Familiarity;
        [DataMember(Order = 12)] public float Rapport;
        [DataMember(Order = 13)] public float Rivalry;
        [DataMember(Order = 14)] public int ConversationExchanges;
        [DataMember(Order = 15)] public int PositivePlayerExchanges;
        [DataMember(Order = 16)] public int CompetitivePlayerExchanges;
        [DataMember(Order = 17)] public int VerifiedPracticeDuels;
        [DataMember(Order = 18)] public int RelationshipDataVersion;
        [DataMember(Order = 19)] public int IdentityDataVersion;
        [DataMember(Order = 20)] public string GeneratedSimulatedPlayerBackground;
        [DataMember(Order = 21)] public string GeneratedSimulatedPlayerBackgroundVersion;
        [DataMember(Order = 22)] public string GeneratedLongTermWants;
        [DataMember(Order = 23)] public string GeneratedCaresAbout;
        [DataMember(Order = 24)] public string GeneratedIdentityMotivationVersion;
        [DataMember(Order = 25)] public AuthoredIdentityPersistenceDto AuthoredIdentity;
        [DataMember(Order = 26)] public List<StructuredMemoryPersistenceDto> StructuredMemories;
        [DataMember(Order = 27)] public List<MemoryEventPersistenceDto> RecentEvents;
        [DataMember(Order = 28)] public List<string> ImportantMemories;
        [DataMember(Order = 29)] public List<string> RecentGroupChat;
        [DataMember(Order = 30)] public List<ChatMessagePersistenceDto> Conversation;
        [DataMember(Order = 31)] public List<string> OutingSummaries;
        [DataMember(Order = 32)] public string LastOutingUtc;
        [DataMember(Order = 33)] public int TotalGroupedMinutes;
        [DataMember(Order = 34)] public List<string> ConversationSummaries;
        [DataMember(Order = 35)] public List<SimRelationshipPersistenceDto> SimRelationships;
        [DataMember(Order = 36)] public List<SocialRelationshipPersistenceDto> SocialRelationships;
        [DataMember(Order = 37)] public List<SimPreferencePersistenceDto> Preferences;

        internal static SimMemoryPersistenceDto FromModel(SimMemory source)
        {
            SimMemoryPersistenceDto dto = new SimMemoryPersistenceDto();
            if (source == null) return dto;
            dto.SimKey = source.SimKey; dto.Name = source.Name; dto.FirstSeenUtc = source.FirstSeenUtc;
            dto.LastSeenUtc = source.LastSeenUtc; dto.LastKnownScene = source.LastKnownScene;
            dto.LastKnownClass = source.LastKnownClass; dto.LastKnownPersonality = source.LastKnownPersonality;
            dto.LastKnownLevel = source.LastKnownLevel; dto.GroupSessions = source.GroupSessions;
            dto.CompletedOutings = source.CompletedOutings; dto.Familiarity = source.Familiarity;
            dto.Rapport = source.Rapport; dto.Rivalry = source.Rivalry;
            dto.ConversationExchanges = source.ConversationExchanges;
            dto.PositivePlayerExchanges = source.PositivePlayerExchanges;
            dto.CompetitivePlayerExchanges = source.CompetitivePlayerExchanges;
            dto.VerifiedPracticeDuels = source.VerifiedPracticeDuels;
            dto.RelationshipDataVersion = source.RelationshipDataVersion;
            dto.IdentityDataVersion = source.IdentityDataVersion;
            dto.GeneratedSimulatedPlayerBackground = source.GeneratedSimulatedPlayerBackground;
            dto.GeneratedSimulatedPlayerBackgroundVersion = source.GeneratedSimulatedPlayerBackgroundVersion;
            dto.GeneratedLongTermWants = source.GeneratedLongTermWants;
            dto.GeneratedCaresAbout = source.GeneratedCaresAbout;
            dto.GeneratedIdentityMotivationVersion = source.GeneratedIdentityMotivationVersion;
            dto.AuthoredIdentity = AuthoredIdentityPersistenceDto.FromModel(source.AuthoredIdentity);
            dto.StructuredMemories = Map(source.StructuredMemories, StructuredMemoryPersistenceDto.FromModel);
            dto.RecentEvents = Map(source.RecentEvents, MemoryEventPersistenceDto.FromModel);
            dto.ImportantMemories = Copy(source.ImportantMemories);
            dto.RecentGroupChat = Copy(source.RecentGroupChat);
            dto.Conversation = Map(source.Conversation, ChatMessagePersistenceDto.FromModel);
            dto.OutingSummaries = Copy(source.OutingSummaries);
            dto.LastOutingUtc = source.LastOutingUtc; dto.TotalGroupedMinutes = source.TotalGroupedMinutes;
            dto.ConversationSummaries = Copy(source.ConversationSummaries);
            dto.SimRelationships = Map(source.SimRelationships, SimRelationshipPersistenceDto.FromModel);
            dto.SocialRelationships = Map(source.SocialRelationships, SocialRelationshipPersistenceDto.FromModel);
            dto.Preferences = Map(source.Preferences, SimPreferencePersistenceDto.FromModel);
            return dto;
        }

        internal SimMemory ToModel()
        {
            SimMemory value = new SimMemory();
            value.SimKey = SimKey; value.Name = Name; value.FirstSeenUtc = FirstSeenUtc; value.LastSeenUtc = LastSeenUtc;
            value.LastKnownScene = LastKnownScene; value.LastKnownClass = LastKnownClass;
            value.LastKnownPersonality = LastKnownPersonality; value.LastKnownLevel = LastKnownLevel;
            value.GroupSessions = GroupSessions; value.CompletedOutings = CompletedOutings;
            value.Familiarity = Familiarity; value.Rapport = Rapport; value.Rivalry = Rivalry;
            value.ConversationExchanges = ConversationExchanges; value.PositivePlayerExchanges = PositivePlayerExchanges;
            value.CompetitivePlayerExchanges = CompetitivePlayerExchanges; value.VerifiedPracticeDuels = VerifiedPracticeDuels;
            value.RelationshipDataVersion = RelationshipDataVersion; value.IdentityDataVersion = IdentityDataVersion;
            value.GeneratedSimulatedPlayerBackground = GeneratedSimulatedPlayerBackground;
            value.GeneratedSimulatedPlayerBackgroundVersion = GeneratedSimulatedPlayerBackgroundVersion;
            value.GeneratedLongTermWants = GeneratedLongTermWants; value.GeneratedCaresAbout = GeneratedCaresAbout;
            value.GeneratedIdentityMotivationVersion = GeneratedIdentityMotivationVersion;
            value.AuthoredIdentity = AuthoredIdentity == null ? null : AuthoredIdentity.ToModel();
            value.StructuredMemories = Map(StructuredMemories, delegate(StructuredMemoryPersistenceDto x) { return x.ToModel(); });
            value.RecentEvents = Map(RecentEvents, delegate(MemoryEventPersistenceDto x) { return x.ToModel(); });
            value.ImportantMemories = Copy(ImportantMemories); value.RecentGroupChat = Copy(RecentGroupChat);
            value.Conversation = Map(Conversation, delegate(ChatMessagePersistenceDto x) { return x.ToModel(); });
            value.OutingSummaries = Copy(OutingSummaries); value.LastOutingUtc = LastOutingUtc;
            value.TotalGroupedMinutes = TotalGroupedMinutes; value.ConversationSummaries = Copy(ConversationSummaries);
            value.SimRelationships = Map(SimRelationships, delegate(SimRelationshipPersistenceDto x) { return x.ToModel(); });
            value.SocialRelationships = Map(SocialRelationships, delegate(SocialRelationshipPersistenceDto x) { return x.ToModel(); });
            value.Preferences = Map(Preferences, delegate(SimPreferencePersistenceDto x) { return x.ToModel(); });
            value.Normalize();
            return value;
        }

        private static List<string> Copy(List<string> source)
        { return source == null ? new List<string>() : new List<string>(source); }

        private static List<TOut> Map<TIn, TOut>(List<TIn> source, Func<TIn, TOut> convert)
        {
            List<TOut> result = new List<TOut>();
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++) if (source[i] != null) result.Add(convert(source[i]));
            return result;
        }
    }

    [DataContract]
    internal sealed class MemoryEventPersistenceDto
    {
        [DataMember(Order = 1)] public string utc;
        [DataMember(Order = 2)] public string type;
        [DataMember(Order = 3)] public string text;
        [DataMember(Order = 4)] public int importance;
        internal static MemoryEventPersistenceDto FromModel(MemoryEvent x)
        { return x == null ? null : new MemoryEventPersistenceDto { utc = x.utc, type = x.type, text = x.text, importance = x.importance }; }
        internal MemoryEvent ToModel() { return new MemoryEvent { utc = utc, type = type, text = text, importance = importance }; }
    }

    [DataContract]
    internal sealed class ChatMessagePersistenceDto
    {
        [DataMember(Order = 1)] public string role;
        [DataMember(Order = 2)] public string content;
        internal static ChatMessagePersistenceDto FromModel(ChatMessage x)
        { return x == null ? null : new ChatMessagePersistenceDto { role = x.role, content = x.content }; }
        internal ChatMessage ToModel() { return new ChatMessage(role, content); }
    }

    [DataContract]
    internal sealed class SimPreferencePersistenceDto
    {
        [DataMember(Order = 1)] public string TopicKey;
        [DataMember(Order = 2)] public string Statement;
        [DataMember(Order = 3)] public int TimesExpressed;
        [DataMember(Order = 4)] public string UpdatedUtc;
        internal static SimPreferencePersistenceDto FromModel(SimPreferenceMemory x)
        { return x == null ? null : new SimPreferencePersistenceDto { TopicKey = x.TopicKey, Statement = x.Statement, TimesExpressed = x.TimesExpressed, UpdatedUtc = x.UpdatedUtc }; }
        internal SimPreferenceMemory ToModel()
        { return new SimPreferenceMemory { TopicKey = TopicKey, Statement = Statement, TimesExpressed = TimesExpressed, UpdatedUtc = UpdatedUtc }; }
    }

    [DataContract]
    internal sealed class SimRelationshipPersistenceDto
    {
        [DataMember(Order = 1)] public string OtherSimKey;
        [DataMember(Order = 2)] public string OtherName;
        [DataMember(Order = 3)] public int SharedOutings;
        [DataMember(Order = 4)] public int SharedMinutes;
        [DataMember(Order = 5)] public int SharedConversationThreads;
        [DataMember(Order = 6)] public int PositiveExchanges;
        [DataMember(Order = 7)] public int CompetitiveExchanges;
        [DataMember(Order = 8)] public int VerifiedPracticeDuels;
        [DataMember(Order = 9)] public float Familiarity;
        [DataMember(Order = 10)] public float Rapport;
        [DataMember(Order = 11)] public float Rivalry;
        [DataMember(Order = 12)] public string LastSharedUtc;
        internal static SimRelationshipPersistenceDto FromModel(SimRelationshipMemory x)
        {
            if (x == null) return null;
            return new SimRelationshipPersistenceDto { OtherSimKey = x.OtherSimKey, OtherName = x.OtherName,
                SharedOutings = x.SharedOutings, SharedMinutes = x.SharedMinutes,
                SharedConversationThreads = x.SharedConversationThreads, PositiveExchanges = x.PositiveExchanges,
                CompetitiveExchanges = x.CompetitiveExchanges, VerifiedPracticeDuels = x.VerifiedPracticeDuels,
                Familiarity = x.Familiarity, Rapport = x.Rapport, Rivalry = x.Rivalry, LastSharedUtc = x.LastSharedUtc };
        }
        internal SimRelationshipMemory ToModel()
        {
            return new SimRelationshipMemory { OtherSimKey = OtherSimKey, OtherName = OtherName,
                SharedOutings = SharedOutings, SharedMinutes = SharedMinutes,
                SharedConversationThreads = SharedConversationThreads, PositiveExchanges = PositiveExchanges,
                CompetitiveExchanges = CompetitiveExchanges, VerifiedPracticeDuels = VerifiedPracticeDuels,
                Familiarity = Familiarity, Rapport = Rapport, Rivalry = Rivalry, LastSharedUtc = LastSharedUtc };
        }
    }

    [DataContract]
    internal sealed class SocialRelationshipPersistenceDto
    {
        [DataMember(Order = 1)] public string OtherSimKey;
        [DataMember(Order = 2)] public string OtherName;
        [DataMember(Order = 3)] public float Familiarity;
        [DataMember(Order = 4)] public float Warmth;
        [DataMember(Order = 5)] public float Trust;
        [DataMember(Order = 6)] public float Tension;
        [DataMember(Order = 7)] public float Rivalry;
        [DataMember(Order = 8)] public int MeaningfulEpisodes;
        [DataMember(Order = 9)] public string UpdatedUtc;
        [DataMember(Order = 10)] public List<string> AppliedEpisodeIds;
        internal static SocialRelationshipPersistenceDto FromModel(SocialRelationshipMemory x)
        {
            if (x == null) return null;
            return new SocialRelationshipPersistenceDto { OtherSimKey = x.OtherSimKey, OtherName = x.OtherName,
                Familiarity = x.Familiarity, Warmth = x.Warmth, Trust = x.Trust, Tension = x.Tension,
                Rivalry = x.Rivalry, MeaningfulEpisodes = x.MeaningfulEpisodes, UpdatedUtc = x.UpdatedUtc,
                AppliedEpisodeIds = x.AppliedEpisodeIds == null ? new List<string>() : new List<string>(x.AppliedEpisodeIds) };
        }
        internal SocialRelationshipMemory ToModel()
        {
            return new SocialRelationshipMemory { OtherSimKey = OtherSimKey, OtherName = OtherName,
                Familiarity = Familiarity, Warmth = Warmth, Trust = Trust, Tension = Tension, Rivalry = Rivalry,
                MeaningfulEpisodes = MeaningfulEpisodes, UpdatedUtc = UpdatedUtc,
                AppliedEpisodeIds = AppliedEpisodeIds == null ? new List<string>() : new List<string>(AppliedEpisodeIds) };
        }
    }

    [DataContract]
    internal sealed class AuthoredIdentityPersistenceDto
    {
        [DataMember(Order = 1)] public string CorePersonality;
        [DataMember(Order = 2)] public string PersonalBackground;
        [DataMember(Order = 3)] public string ErenshorPersona;
        [DataMember(Order = 4)] public string RelationshipToPlayer;
        [DataMember(Order = 5)] public string LongTermWants;
        [DataMember(Order = 6)] public string CaresAbout;
        [DataMember(Order = 7)] public List<StructuredMemoryPersistenceDto> SharedHistory;
        [DataMember(Order = 8)] public List<StructuredMemoryPersistenceDto> PinnedMemories;
        internal static AuthoredIdentityPersistenceDto FromModel(AuthoredIdentityProfile x)
        {
            if (x == null) return null;
            return new AuthoredIdentityPersistenceDto { CorePersonality = x.CorePersonality,
                PersonalBackground = x.PersonalBackground, ErenshorPersona = x.ErenshorPersona,
                RelationshipToPlayer = x.RelationshipToPlayer, LongTermWants = x.LongTermWants,
                CaresAbout = x.CaresAbout,
                SharedHistory = Map(x.SharedHistory), PinnedMemories = Map(x.PinnedMemories) };
        }
        internal AuthoredIdentityProfile ToModel()
        {
            return new AuthoredIdentityProfile { CorePersonality = CorePersonality, PersonalBackground = PersonalBackground,
                ErenshorPersona = ErenshorPersona, RelationshipToPlayer = RelationshipToPlayer,
                LongTermWants = LongTermWants, CaresAbout = CaresAbout,
                SharedHistory = Map(SharedHistory), PinnedMemories = Map(PinnedMemories) };
        }
        private static List<StructuredMemoryPersistenceDto> Map(List<StructuredMemoryRecord> source)
        {
            List<StructuredMemoryPersistenceDto> result = new List<StructuredMemoryPersistenceDto>();
            if (source != null) for (int i = 0; i < source.Count; i++) if (source[i] != null) result.Add(StructuredMemoryPersistenceDto.FromModel(source[i]));
            return result;
        }
        private static List<StructuredMemoryRecord> Map(List<StructuredMemoryPersistenceDto> source)
        {
            List<StructuredMemoryRecord> result = new List<StructuredMemoryRecord>();
            if (source != null) for (int i = 0; i < source.Count; i++) if (source[i] != null) result.Add(source[i].ToModel());
            return result;
        }
    }

    [DataContract]
    internal sealed class StructuredMemoryPersistenceDto
    {
        [DataMember(Order = 1)] public string Id;
        [DataMember(Order = 2)] public string MemoryType;
        [DataMember(Order = 3)] public string Text;
        [DataMember(Order = 4)] public string Subject;
        [DataMember(Order = 5)] public int Importance;
        [DataMember(Order = 6)] public List<string> Participants;
        [DataMember(Order = 7)] public List<string> KnownBy;
        [DataMember(Order = 8)] public List<string> Topics;
        [DataMember(Order = 9)] public List<string> EmotionalTags;
        [DataMember(Order = 10)] public string Utc;
        [DataMember(Order = 11)] public string Source;
        [DataMember(Order = 12)] public string EpisodeId;
        [DataMember(Order = 13)] public string SourceSystem;
        [DataMember(Order = 14)] public string SourceCorrelationId;
        [DataMember(Order = 15)] public string FactSummary;
        [DataMember(Order = 16)] public string InterpretationSummary;
        [DataMember(Order = 17)] public string CharacterScope;
        [DataMember(Order = 18)] public string MemoryTier;
        [DataMember(Order = 19)] public List<string> EvidenceIds;
        [DataMember(Order = 20)] public string CallbackConcept;
        [DataMember(Order = 21)] public bool InsideJoke;
        [DataMember(Order = 22)] public int RecurrenceCount;
        [DataMember(Order = 23)] public float HumorScore;
        [DataMember(Order = 24)] public float ConflictScore;
        [DataMember(Order = 25)] public string LastRecalledUtc;
        [DataMember(Order = 26)] public string OwnerSimKey;
        [DataMember(Order = 27)] public string OwnerSimName;
        [DataMember(Order = 28)] public bool ExplicitPublic;
        [DataMember(Order = 29)] public bool Authored;
        [DataMember(Order = 30)] public bool Pinned;

        internal static StructuredMemoryPersistenceDto FromModel(StructuredMemoryRecord x)
        {
            if (x == null) return null;
            return new StructuredMemoryPersistenceDto { Id = x.Id, MemoryType = x.MemoryType, Text = x.Text,
                Subject = x.Subject, Importance = x.Importance, Participants = Copy(x.Participants), KnownBy = Copy(x.KnownBy),
                Topics = Copy(x.Topics), EmotionalTags = Copy(x.EmotionalTags), Utc = x.Utc, Source = x.Source,
                EpisodeId = x.EpisodeId, SourceSystem = x.SourceSystem, SourceCorrelationId = x.SourceCorrelationId,
                FactSummary = x.FactSummary, InterpretationSummary = x.InterpretationSummary,
                CharacterScope = x.CharacterScope, MemoryTier = x.MemoryTier, EvidenceIds = Copy(x.EvidenceIds),
                CallbackConcept = x.CallbackConcept, InsideJoke = x.InsideJoke, RecurrenceCount = x.RecurrenceCount,
                HumorScore = x.HumorScore, ConflictScore = x.ConflictScore, LastRecalledUtc = x.LastRecalledUtc,
                OwnerSimKey = x.OwnerSimKey, OwnerSimName = x.OwnerSimName, ExplicitPublic = x.ExplicitPublic,
                Authored = x.Authored, Pinned = x.Pinned };
        }
        internal StructuredMemoryRecord ToModel()
        {
            return new StructuredMemoryRecord { Id = Id, MemoryType = MemoryType, Text = Text, Subject = Subject,
                Importance = Importance, Participants = Copy(Participants), KnownBy = Copy(KnownBy), Topics = Copy(Topics),
                EmotionalTags = Copy(EmotionalTags), Utc = Utc, Source = Source, EpisodeId = EpisodeId,
                SourceSystem = SourceSystem, SourceCorrelationId = SourceCorrelationId, FactSummary = FactSummary,
                InterpretationSummary = InterpretationSummary, CharacterScope = CharacterScope, MemoryTier = MemoryTier,
                EvidenceIds = Copy(EvidenceIds), CallbackConcept = CallbackConcept, InsideJoke = InsideJoke,
                RecurrenceCount = RecurrenceCount, HumorScore = HumorScore, ConflictScore = ConflictScore,
                LastRecalledUtc = LastRecalledUtc, OwnerSimKey = OwnerSimKey, OwnerSimName = OwnerSimName,
                ExplicitPublic = ExplicitPublic, Authored = Authored, Pinned = Pinned };
        }
        private static List<string> Copy(List<string> source)
        { return source == null ? new List<string>() : new List<string>(source); }
    }
}
