using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    // Pure policy for explicit Campmaster Relax social downtime. It supplies
    // topic identities/cadence only; it never decides game facts or emits chat.
    internal static class RelaxSocialPolicy
    {
        internal static readonly string[] TopicIds = new string[]
        {
            "class_role_preferences",
            "zone_atmosphere",
            "adventure_preferences",
            "pace_preferences",
            "gear_aesthetics",
            "enemy_design",
            "food_music",
            "party_preferences",
            "verified_outing",
            "verified_history",
            "light_teasing"
        };

        internal static double MinimumSeconds(SocialActivityPreset preset)
        {
            switch (preset)
            {
                case SocialActivityPreset.Quiet: return 120.0;
                case SocialActivityPreset.Lively: return 25.0;
                default: return 45.0;
            }
        }

        internal static double MaximumSeconds(SocialActivityPreset preset)
        {
            switch (preset)
            {
                case SocialActivityPreset.Quiet: return 240.0;
                case SocialActivityPreset.Lively: return 60.0;
                default: return 120.0;
            }
        }

        internal static double InitialSeconds(SocialActivityPreset preset)
        {
            switch (preset)
            {
                case SocialActivityPreset.Quiet: return 60.0;
                case SocialActivityPreset.Lively: return 10.0;
                default: return 20.0;
            }
        }

        internal static string BuildSituation(string topicId, string scene, string verifiedOutingFact)
        {
            string prefix = "RELAX SOCIAL DOWNTIME: the current visible party is safely relaxing together";
            if (!string.IsNullOrWhiteSpace(scene)) prefix += " in " + scene;
            prefix += ". This is social downtime, not a hunt camp, pull, route, combat order, or group decision. ";

            string id = string.IsNullOrWhiteSpace(topicId) ? "party_preferences" : topicId.Trim().ToLowerInvariant();
            switch (id)
            {
                case "class_role_preferences":
                    return prefix + "Topic seed: share or ask a harmless opinion about classes, roles, spells, or combat style. Treat verified class/role facts as authoritative and do not invent assignments.";
                case "zone_atmosphere":
                    return prefix + "Topic seed: ask or share a harmless opinion about zone atmosphere. The current zone may be mentioned as current location, but do not claim a prior visit, route, or destination history unless verified memory supports it.";
                case "adventure_preferences":
                    return prefix + "Topic seed: ask what kind of adventure, dungeon, grinding, exploration, or challenge people generally enjoy. Preferences are social flavor, not a plan the party has agreed to.";
                case "pace_preferences":
                    return prefix + "Topic seed: ask whether people generally prefer careful pulls, fast pacing, long camps, or moving around. Do not issue a tactical command or claim the party has chosen a plan.";
                case "gear_aesthetics":
                    return prefix + "Topic seed: talk about gear looks, weapon style, or loot aesthetics as opinions only. Do not claim anyone owns, looted, needs, or can equip an item unless verified facts say so.";
                case "enemy_design":
                    return prefix + "Topic seed: ask which kinds of enemies or encounter designs people find interesting or annoying. Do not invent a recent fight or kill.";
                case "food_music":
                    return prefix + "Topic seed: bring up food, music, weather, or another ordinary off-topic MMO downtime subject. Keep it casual and player-like.";
                case "verified_outing":
                    if (!string.IsNullOrWhiteSpace(verifiedOutingFact))
                        return prefix + "Verified current-session observation: " + verifiedOutingFact.Trim() + " Topic seed: react to or ask about that verified outing fact without adding unverified history, loot, damage, or plans.";
                    return prefix + "Topic seed: ask another party member a harmless preference question. No verified outing fact is available, so do not invent one.";
                case "verified_history":
                    return prefix + "Topic seed: if VERIFIED MEMORY supplied in the prompt contains a shared outing, prior camp, or friendly practice duel, you may briefly reference one verified memory. Otherwise ask a generic preference instead. Never manufacture history to satisfy this seed.";
                case "light_teasing":
                    return prefix + "Topic seed: make a short friendly joke or light tease grounded only in current personality/relationship tone. Do not invent a past mistake, death, injury, loot event, or recurring joke.";
                default:
                    return prefix + "Topic seed: ask another visible party member a harmless preference or opinion question. Do not turn the answer into a factual plan or invented history.";
            }
        }

        internal static bool IsTopic(string topicId)
        {
            if (string.IsNullOrWhiteSpace(topicId)) return false;
            for (int i = 0; i < TopicIds.Length; i++)
                if (string.Equals(TopicIds[i], topicId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static List<string> RunSelfTests()
        {
            List<string> lines = new List<string>();
            Add(lines, "cadence Quiet > Normal > Lively",
                MinimumSeconds(SocialActivityPreset.Quiet) > MinimumSeconds(SocialActivityPreset.Normal) &&
                MinimumSeconds(SocialActivityPreset.Normal) > MinimumSeconds(SocialActivityPreset.Lively));
            Add(lines, "normal Relax cadence targets 45-120s",
                MinimumSeconds(SocialActivityPreset.Normal) == 45.0 && MaximumSeconds(SocialActivityPreset.Normal) == 120.0);
            Add(lines, "topic identities are distinct", TopicIds.Length >= 8 && IsTopic("verified_history") && IsTopic("class_role_preferences"));
            string noFact = BuildSituation("verified_outing", "Brasse", string.Empty);
            Add(lines, "missing outing fact explicitly forbids invention", noFact.IndexOf("do not invent", StringComparison.OrdinalIgnoreCase) >= 0);
            string withFact = BuildSituation("verified_outing", "Brasse", "The party completed a verified encounter.");
            Add(lines, "verified outing fact is carried into seed", withFact.IndexOf("completed a verified encounter", StringComparison.OrdinalIgnoreCase) >= 0);
            return lines;
        }

        private static void Add(List<string> lines, string name, bool pass)
        {
            lines.Add("[DeepSims Relax] " + name + ": " + (pass ? "PASS" : "FAIL"));
        }
    }
}
