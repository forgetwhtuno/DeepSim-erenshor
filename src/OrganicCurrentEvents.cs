using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ErenshorDeepSims
{
    internal static class OrganicCurrentEventsPolicy
    {
        internal const int MinCooldownMinutes = 20;
        internal const int MaxCooldownMinutes = 60;
        private static readonly string[] Queries =
        {
            "NASA space science news", "AI technology news", "video game industry news",
            "film television entertainment news", "major world news"
        };

        internal static bool CanConsider(bool roleplayPerspective, SocialActivityState activity, int partyCount,
            bool directTurnPending, bool hasUnansweredTurn, string currentThread)
        {
            if (roleplayPerspective || directTurnPending || hasUnansweredTurn) return false;
            if (activity != SocialActivityState.SocialDowntime && activity != SocialActivityState.ExtendedDowntime) return false;
            if (partyCount < 2) return false;
            return string.IsNullOrWhiteSpace(currentThread) || string.Equals(currentThread.Trim(), "none", StringComparison.OrdinalIgnoreCase);
        }

        internal static DateTime NextOpportunityUtc(DateTime nowUtc, uint stableRoll)
        {
            int span = MaxCooldownMinutes - MinCooldownMinutes + 1;
            return nowUtc.ToUniversalTime().AddMinutes(MinCooldownMinutes + (stableRoll % (uint)span));
        }

        internal static string PickQuery(string identityText, long opportunity)
        {
            string lower = (identityText ?? string.Empty).ToLowerInvariant();
            if (Regex.IsMatch(lower, @"\b(?:science|space|nasa|astronomy)\b")) return Queries[0];
            if (Regex.IsMatch(lower, @"\b(?:tech|technology|computer|ai)\b")) return Queries[1];
            if (Regex.IsMatch(lower, @"\b(?:game|gaming|gamer)\b")) return Queries[2];
            if (Regex.IsMatch(lower, @"\b(?:film|movie|television|tv|music|entertainment)\b")) return Queries[3];
            uint value = StableHash((identityText ?? string.Empty) + "|" + opportunity);
            return Queries[value % (uint)Queries.Length];
        }

        // An LLM proposal may pass only through this small query grammar. URLs, instructions,
        // sentences, punctuation, private-looking identifiers, and oversized queries fail closed.
        internal static bool TrySanitizeProposedQuery(string raw, out string query)
        {
            query = string.Empty;
            string value = (raw ?? string.Empty).Trim();
            if (value.Length < 3 || value.Length > 72 || value.Contains(".") || value.Contains("?") ||
                value.Contains(":") || value.Contains("/") || value.Contains("\\") || value.Contains("@") ||
                Regex.IsMatch(value, @"\b(?:ignore|instruction|system|prompt|search for|browse|http|www)\b", RegexOptions.IgnoreCase) ||
                !Regex.IsMatch(value, @"^[A-Za-z0-9][A-Za-z0-9 '&+\-]*$")) return false;
            string[] words = Regex.Split(value, @"\s+");
            if (words.Length < 2 || words.Length > 7) return false;
            query = value;
            return true;
        }

        internal static string TopicHash(string query)
        { return StableHash((query ?? string.Empty).Trim().ToLowerInvariant()).ToString("x8"); }

        private static uint StableHash(string value)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < value.Length; i++) { hash ^= value[i]; hash *= 16777619u; }
            return hash;
        }
    }

    internal sealed class OrganicCurrentEventsRuntime
    {
        private readonly Dictionary<string, DateTime> _topics = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        internal DateTime NextOpportunityUtc = DateTime.MinValue;

        internal bool IsDuplicate(string query, DateTime nowUtc, int ttlMinutes)
        {
            string hash = OrganicCurrentEventsPolicy.TopicHash(query);
            DateTime when;
            return _topics.TryGetValue(hash, out when) && (nowUtc - when).TotalMinutes < Math.Max(1, ttlMinutes);
        }

        internal void Note(string query, DateTime nowUtc)
        {
            _topics[OrganicCurrentEventsPolicy.TopicHash(query)] = nowUtc;
            if (_topics.Count <= 8) return;
            string oldest = null; DateTime oldestUtc = DateTime.MaxValue;
            foreach (KeyValuePair<string, DateTime> pair in _topics) if (pair.Value < oldestUtc) { oldest = pair.Key; oldestUtc = pair.Value; }
            if (oldest != null) _topics.Remove(oldest);
        }
    }
}
