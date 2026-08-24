using System;
using System.Text;
using System.Threading;

namespace ErenshorDeepSims
{
    internal sealed class MemoryCollectionObservation
    {
        internal int StructuredMemories;
        internal int RecentEvents;
        internal int Preferences;
        internal int SimRelationships;
        internal int SocialRelationships;
        internal bool AuthoredIdentity;

        internal static MemoryCollectionObservation Capture(SimMemory memory)
        {
            MemoryCollectionObservation result = new MemoryCollectionObservation();
            if (memory == null) return result;
            result.StructuredMemories = memory.StructuredMemories == null ? -1 : memory.StructuredMemories.Count;
            result.RecentEvents = memory.RecentEvents == null ? -1 : memory.RecentEvents.Count;
            result.Preferences = memory.Preferences == null ? -1 : memory.Preferences.Count;
            result.SimRelationships = memory.SimRelationships == null ? -1 : memory.SimRelationships.Count;
            result.SocialRelationships = memory.SocialRelationships == null ? -1 : memory.SocialRelationships.Count;
            result.AuthoredIdentity = memory.AuthoredIdentity != null;
            return result;
        }

        internal string Describe()
        {
            return "structured=" + StructuredMemories + " recentEvents=" + RecentEvents +
                " preferences=" + Preferences + " simRelationships=" + SimRelationships +
                " socialRelationships=" + SocialRelationships + " authoredIdentity=" + AuthoredIdentity;
        }

        internal bool SameAs(MemoryCollectionObservation other)
        {
            return other != null && StructuredMemories == other.StructuredMemories &&
                RecentEvents == other.RecentEvents && Preferences == other.Preferences &&
                SimRelationships == other.SimRelationships && SocialRelationships == other.SocialRelationships &&
                AuthoredIdentity == other.AuthoredIdentity;
        }
    }

    internal sealed class JsonFieldPresenceObservation
    {
        internal bool ContainsStructuredMemories;
        internal bool ContainsRecentEvents;
        internal bool ContainsPreferences;
        internal bool ContainsSimRelationships;
        internal bool ContainsSocialRelationships;
        internal bool ContainsAuthoredIdentity;
        internal int CharacterLength;
        internal int ByteLength;

        internal static JsonFieldPresenceObservation Inspect(string json)
        {
            string value = json ?? string.Empty;
            return new JsonFieldPresenceObservation
            {
                ContainsStructuredMemories = HasField(value, "StructuredMemories"),
                ContainsRecentEvents = HasField(value, "RecentEvents"),
                ContainsPreferences = HasField(value, "Preferences"),
                ContainsSimRelationships = HasField(value, "SimRelationships"),
                ContainsSocialRelationships = HasField(value, "SocialRelationships"),
                ContainsAuthoredIdentity = HasField(value, "AuthoredIdentity"),
                CharacterLength = value.Length,
                ByteLength = Encoding.UTF8.GetByteCount(value)
            };
        }

        private static bool HasField(string json, string field)
        {
            return json.IndexOf("\"" + field + "\"", StringComparison.Ordinal) >= 0;
        }

        internal string Describe()
        {
            return "containsStructuredMemories=" + ContainsStructuredMemories +
                " containsRecentEvents=" + ContainsRecentEvents +
                " containsPreferences=" + ContainsPreferences +
                " containsSimRelationships=" + ContainsSimRelationships +
                " containsSocialRelationships=" + ContainsSocialRelationships +
                " containsAuthoredIdentity=" + ContainsAuthoredIdentity +
                " chars=" + CharacterLength + " bytes=" + ByteLength;
        }
    }

    internal sealed class JsonWriteObservation
    {
        internal JsonFieldPresenceObservation Fields;
    }

    internal sealed class AutonomousAdvanceObservation
    {
        internal bool TopicFatigueAdvanced;
        internal bool ConversationMomentAdded;
        internal bool PreferencePersisted;
        internal bool CallbackStateAdvanced;

        internal string Describe()
        {
            return "topicFatigueAdvanced=" + TopicFatigueAdvanced +
                " conversationMomentAdded=" + ConversationMomentAdded +
                " preferencePersisted=" + PreferencePersisted +
                " callbackStateAdvanced=" + CallbackStateAdvanced;
        }
    }

    internal static class CognitionObservability
    {
        internal static string IdentityToken(string value)
        {
            uint hash = 2166136261u;
            string source = value ?? string.Empty;
            for (int i = 0; i < source.Length; i++) { hash ^= char.ToLowerInvariant(source[i]); hash *= 16777619u; }
            return "sim-" + hash.ToString("x8");
        }

        internal static string BoundedToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "none";
            StringBuilder result = new StringBuilder();
            string source = value.Trim().ToLowerInvariant();
            for (int i = 0; i < source.Length && result.Length < 64; i++)
            {
                char c = source[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ':') result.Append(c);
                else if (result.Length > 0 && result[result.Length - 1] != '_') result.Append('_');
            }
            return result.Length == 0 ? "none" : result.ToString();
        }

        internal static void SafeInfo(IDeepSimsLog log, string line)
        {
            if (log == null) return;
            try { log.LogInfo(line); } catch { }
        }
    }

    internal static class RecentLifeDiagnosticCounters
    {
        private static long _generated, _stored, _alreadyStored, _knowledgeDenied, _expired, _selectedAsSeed;

        internal static void Generated(int count) { if (count > 0) Interlocked.Add(ref _generated, count); }
        internal static void StoreDecision(string decision)
        {
            if (string.Equals(decision, "stored", StringComparison.OrdinalIgnoreCase)) Interlocked.Increment(ref _stored);
            else if (string.Equals(decision, "already_stored", StringComparison.OrdinalIgnoreCase)) Interlocked.Increment(ref _alreadyStored);
            else if (string.Equals(decision, "knowledge_denied", StringComparison.OrdinalIgnoreCase)) Interlocked.Increment(ref _knowledgeDenied);
        }
        internal static void Expired() { Interlocked.Increment(ref _expired); }
        internal static void SelectedAsSeed() { Interlocked.Increment(ref _selectedAsSeed); }
        internal static string Describe()
        {
            return "generated=" + Interlocked.Read(ref _generated) + " stored=" + Interlocked.Read(ref _stored) +
                " alreadyStored=" + Interlocked.Read(ref _alreadyStored) + " knowledgeDenied=" + Interlocked.Read(ref _knowledgeDenied) +
                " expired=" + Interlocked.Read(ref _expired) + " selectedAsSeed=" + Interlocked.Read(ref _selectedAsSeed);
        }
        internal static void ResetForTests()
        {
            Interlocked.Exchange(ref _generated, 0); Interlocked.Exchange(ref _stored, 0);
            Interlocked.Exchange(ref _alreadyStored, 0); Interlocked.Exchange(ref _knowledgeDenied, 0);
            Interlocked.Exchange(ref _expired, 0); Interlocked.Exchange(ref _selectedAsSeed, 0);
        }
    }
}
