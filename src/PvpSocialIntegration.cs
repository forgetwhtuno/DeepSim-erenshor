using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    // Optional fact-only surface discovered by Erenshor PvP through reflection. PvP owns
    // gameplay; Deep Sims can only observe the sanitized lifecycle and react socially.
    public static class PvpEventBridge
    {
        private static readonly HashSet<string> AllowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pvp_challenge", "pvp_ambush", "pvp_accepted", "pvp_refused", "pvp_cancelled", "pvp_match_completed"
        };
        private static string _lastKey = string.Empty;
        private static DateTime _lastUtc = DateTime.MinValue;

        internal static void ResetRuntimeState()
        {
            _lastKey = string.Empty;
            _lastUtc = DateTime.MinValue;
        }

        // Kept for PvP builds older than contract v2, which carry no classification.
        public static void NotifyPvpEvent(string eventType, string matchId, string opponent,
            string zone, string decision, string reasonToken)
        { NotifyPvpEvent(eventType, matchId, opponent, zone, decision, reasonToken, string.Empty); }

        public static void NotifyPvpEvent(string eventType, string matchId, string opponent,
            string zone, string decision, string reasonToken, string classification)
        {
            DeepSimsPlugin plugin = DeepSimsPlugin.Instance;
            string type = Token(eventType, 48);
            if (plugin == null || !AllowedTypes.Contains(type)) return;
            string id = Token(matchId, 48); string name = Text(opponent, 80); string scene = Text(zone, 80);
            string decisionValue = Token(decision, 40); string reason = Token(reasonToken, 48);
            string verdict = Classify(Token(classification, 40), reason);
            // One match yields at most one social event of a given type. Without this, a spawn
            // failure that both cancels the encounter and despawns the team would be described twice.
            string key = id.Length > 0 ? type + "|" + id : type + "|" + name + "|" + decisionValue + "|" + reason;
            DateTime now = DateTime.UtcNow;
            if (string.Equals(key, _lastKey, StringComparison.Ordinal) && (now - _lastUtc).TotalSeconds < 6.0) return;
            _lastKey = key; _lastUtc = now;
            plugin.NotifyCompetitiveCombatSemantic("pvp", type, reason);
            bool emitObservedEvent = true;
            if (type == "pvp_match_completed")
            {
                string correlation;
                emitObservedEvent = CompetitiveEventCorrelation.TryAcceptTerminal("pvp", id, name, scene, now, out correlation);
            }

            string where = string.IsNullOrWhiteSpace(scene) ? string.Empty : " in " + scene;
            string who = string.IsNullOrWhiteSpace(name) ? "an off-map PvP party" : "the off-map PvP party led by " + name;
            string description; int importance; bool memory; double chance;
            if (type == "pvp_challenge")
            { description = who + " just challenged the player" + where + "."; importance = 28; memory = false; chance = .30; }
            else if (type == "pvp_ambush")
            { description = who + " just ambushed the player and party" + where + (reason.Length == 0 ? "." : " to " + reason.Replace('_', ' ') + "."); importance = 72; memory = false; chance = .90; }
            else if (type == "pvp_accepted")
            { description = "The player just accepted a lethal challenge from " + who + where + "."; importance = 45; memory = false; chance = .45; }
            else if (type == "pvp_refused")
            { description = "The player just declined a challenge from " + who + "."; importance = 18; memory = false; chance = .15; }
            else if (type == "pvp_cancelled")
            {
                bool invalidCancellation = verdict == "invalid";
                description = invalidCancellation
                    ? "The PvP match with " + who + " was voided without a result" + (reason.Length == 0 ? "." : " (" + reason.Replace('_', ' ') + ").")
                    : "The PvP match with " + who + " was cancelled" + (reason.Length == 0 ? "." : " (" + reason.Replace('_', ' ') + ").");
                importance = invalidCancellation ? 5 : 24; memory = false; chance = invalidCancellation ? 0.0 : .10;
            }
            else
            {
                // PvP is the authority on what the outcome means. Nothing here re-derives a verdict
                // from the raw reason token, so an internal failure is never described as a fight.
                bool ambush = decisionValue == "ambush";
                bool decisive = verdict == "player_win" || verdict == "nemesis_win";
                if (verdict == "player_win") description = "The player just defeated every member of " + who + (ambush ? " after a wild ambush" : " in arranged lethal PvP") + where + ".";
                else if (verdict == "nemesis_win") description = who + " just defeated the player" + (ambush ? " in a wild ambush" : " in arranged lethal PvP") + where + ".";
                else if (verdict == "enemy_retreated") description = who + " just disengaged and escaped from " + (ambush ? "a wild ambush" : "lethal PvP") + where + ".";
                else if (verdict == "player_fled") description = "The player just disengaged and escaped from " + who + where + ".";
                else if (verdict == "cancelled") description = "The lethal PvP match with " + who + " ended early without a result" + (reason.Length == 0 ? "." : " (" + reason.Replace('_', ' ') + ").");
                else description = "The lethal PvP match with " + who + " was voided without a result" + (reason.Length == 0 ? "." : " (" + reason.Replace('_', ' ') + ").");
                bool escaped = verdict == "player_fled" || verdict == "enemy_retreated";
                importance = decisive ? 78 : escaped ? 48 : verdict == "cancelled" ? 24 : 5;
                memory = decisive;
                chance = decisive ? .85 : escaped ? .45 : verdict == "cancelled" ? .10 : 0.0;
            }
            List<string> socialParticipants = new List<string> { "player" };
            List<SimSnapshot> currentDeep = plugin.GetActiveDeepSims();
            for (int i = 0; i < currentDeep.Count; i++)
                if (currentDeep[i] != null && !string.IsNullOrWhiteSpace(currentDeep[i].Name)) socialParticipants.Add(currentDeep[i].Name);
            if (!string.IsNullOrWhiteSpace(name)) socialParticipants.Add(name);
            List<string> socialTags = new List<string> { "pvp", "competitiveness" };
            if (type == "pvp_ambush" || type == "pvp_match_completed") socialTags.Add("danger");
            string socialEventId = id.Length > 0 ? "pvp-" + id : SocialEpisodeLedger.StableId(type + "|" + name + "|" + decisionValue + "|" + reason);
            plugin.RecordLivingSocialEpisode("pvp", id, socialEventId, description, socialParticipants, scene, importance,
                socialTags, 0f, type == "pvp_match_completed" ? .55f : .35f, importance >= 65);
            // Cross-source terminal dedupe applies to the legacy/session-event stream only. The
            // Living Social episode is still recorded so correlated Nemesis metadata can merge.
            if (emitObservedEvent) plugin.NotifyObservedGameEvent(type, description, importance, memory, chance);
        }

#if SHARED_CONTRACTS
        // Exercised by the shared cross-mod conformance table through /dsguardtest, so this mirror
        // cannot drift from ErenshorPvP.ErenshorPvpApi.ClassifyOutcome unnoticed.
        internal static List<string> RunSelfTests()
        {
            List<string> results = new List<string>();
            string mirror = ErenshorSharedContracts.PvpContractConformance.RunClassifierConformance(
                "deep sims pvp mirror", delegate(string reason) { return Classify(string.Empty, Token(reason, 48)); });
            results.Add("[DeepSims PvP " + mirror + "]");
            // A supplied verdict always wins over the local mirror, including when the raw token
            // would have classified differently.
            bool honoursSupplied = Classify("player_win", "third_party_aggro") == "player_win" &&
                Classify("invalid", "proxy_death") == "invalid";
            results.Add(honoursSupplied
                ? "[DeepSims PvP PASS supplied classification wins over the local mirror]"
                : "[DeepSims PvP FAIL supplied classification ignored]");
            return results;
        }
#endif

        // Uses PvP's verdict when it supplies one. The fallback exists only for pre-v2 PvP builds
        // and mirrors ErenshorPvP.ErenshorPvpApi.ClassifyOutcome.
        private static string Classify(string classification, string reason)
        {
            if (!string.IsNullOrEmpty(classification)) return classification;
            if (reason == "proxy_death") return "player_win";
            if (reason == "player_death") return "nemesis_win";
            if (reason == "player_fled") return "player_fled";
            if (reason == "retreat") return "enemy_retreated";
            if (reason == "scene_transition" || reason == "manual" || reason == "shutdown" ||
                reason == "timer" || reason == "cleanup") return "cancelled";
            return "invalid";
        }

        private static string Token(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            char[] chars = value.Trim().ToLowerInvariant().ToCharArray(); int count = 0;
            for (int i = 0; i < chars.Length && count < max; i++) if (char.IsLetterOrDigit(chars[i]) || chars[i] == '_') chars[count++] = chars[i];
            return new string(chars, 0, count);
        }

        private static string Text(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string clean = value.Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Replace('=', ':').Trim();
            return clean.Length <= max ? clean : clean.Substring(0, max);
        }
    }
}
