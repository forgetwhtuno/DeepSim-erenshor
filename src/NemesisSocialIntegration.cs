using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    // Optional fact-only bridge. Nemesis owns its templates/cadence; Deep Sims may let
    // current party Sims react socially but receives no gameplay authority.
    public static class NemesisEventBridge
    {
        private static readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "nemesis_designated", "nemesis_taunt", "nemesis_zone_taunt", "nemesis_reply", "nemesis_ambush", "nemesis_ambush_started",
            "nemesis_match_completed", "nemesis_victory", "nemesis_defeat", "nemesis_retreat",
            "nemesis_win", "nemesis_loss", "nemesis_escape"
        };
        private static string _last; private static DateTime _lastUtc;

        internal static void ResetRuntimeState()
        {
            _last = null;
            _lastUtc = DateTime.MinValue;
        }

        public static void NotifyNemesisEvent(string eventType, string nemesisName, string zone, string matchId, string result)
        {
            DeepSimsPlugin plugin = DeepSimsPlugin.Instance; string type = Token(eventType, 48);
            if (plugin == null || !Allowed.Contains(type)) return;
            string name = Text(nemesisName, 80), scene = Text(zone, 80), id = Token(matchId, 48), outcome = Token(result, 48);
            string key = type + "|" + name + "|" + id + "|" + outcome; DateTime now = DateTime.UtcNow;
            if (key == _last && (now - _lastUtc).TotalSeconds < 6) return; _last = key; _lastUtc = now;
            bool emitObservedEvent = true;
            if (type == "nemesis_match_completed")
            {
                string correlation;
                emitObservedEvent = CompetitiveEventCorrelation.TryAcceptTerminal("nemesis", id, name, scene, now, out correlation);
            }
            string who = name.Length == 0 ? "the player's nemesis" : name + ", the player's designated nemesis";
            string description; int importance; bool memory; double chance;
            if (type == "nemesis_designated") { description = who + " was just designated as a persistent social rival."; importance = 40; memory = true; chance = .35; }
            else if (type == "nemesis_ambush_started") { description = who + " just began a verified PvP ambush" + Where(scene) + "."; importance = 75; memory = false; chance = .85; }
            else if (type == "nemesis_match_completed") { description = "A verified PvP match with " + who + " just ended with result " + outcome.Replace('_', ' ') + Where(scene) + "."; importance = 72; memory = true; chance = .8; }
            else { description = who + " just exchanged brief rivalry dialogue with the player."; importance = 18; memory = false; chance = .18; }
            List<string> socialParticipants = new List<string> { "player" };
            List<SimSnapshot> currentDeep = plugin.GetActiveDeepSims();
            for (int i = 0; i < currentDeep.Count; i++)
                if (currentDeep[i] != null && !string.IsNullOrWhiteSpace(currentDeep[i].Name)) socialParticipants.Add(currentDeep[i].Name);
            if (!string.IsNullOrWhiteSpace(name)) socialParticipants.Add(name);
            List<string> socialTags = new List<string> { "rivalry", "nemesis" };
            if (type == "nemesis_ambush_started" || type == "nemesis_match_completed") { socialTags.Add("pvp"); socialTags.Add("danger"); }
            string socialSource = (type == "nemesis_ambush_started" || type == "nemesis_match_completed") ? "nemesis_pvp" : "nemesis";
            string socialEventId = id.Length > 0 ? "pvp-" + id : SocialEpisodeLedger.StableId(type + "|" + name + "|" + outcome);
            plugin.RecordLivingSocialEpisode(socialSource, id, socialEventId, description, socialParticipants, scene, importance,
                socialTags, 0f, type == "nemesis_match_completed" ? .60f : .35f, importance >= 65);
            // Keep one legacy/session event for a correlated PvP/Nemesis terminal, but always let
            // the Living Social ledger merge Nemesis rivalry metadata into the shared PvP episode.
            if (emitObservedEvent) plugin.NotifyObservedGameEvent(type, description, importance, memory, chance);
        }

        public static bool RequestNemesisLine(string nemesisName, string stage, string situation,
            string verifiedRecord, string templateFallback, Action<string> completed)
        {
            DeepSimsPlugin plugin = DeepSimsPlugin.Instance;
            return plugin != null && plugin.QueueNemesisVoice(Text(nemesisName, 80), Token(stage, 24), Text(situation, 180),
                Text(verifiedRecord, 240), Text(templateFallback, 180), completed);
        }
        private static string Where(string scene) { return scene.Length == 0 ? "" : " in " + scene; }
        private static string Token(string value, int max) { if (string.IsNullOrWhiteSpace(value)) return ""; char[] source = value.Trim().ToLowerInvariant().ToCharArray(), output = new char[Math.Min(max, source.Length)]; int n = 0; for (int i = 0; i < source.Length && n < output.Length; i++) if (char.IsLetterOrDigit(source[i]) || source[i] == '_') output[n++] = source[i]; return new string(output, 0, n); }
        private static string Text(string value, int max) { string clean = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Replace('=', ':').Trim(); return clean.Length <= max ? clean : clean.Substring(0, max); }
    }
}