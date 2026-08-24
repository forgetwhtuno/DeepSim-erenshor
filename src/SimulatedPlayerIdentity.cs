using System;
using System.Text.RegularExpressions;

namespace ErenshorDeepSims
{
    // Three-layer identity rule:
    //  1) verified Erenshor facts live in SimSnapshot;
    //  2) the Erenshor roleplay persona lives in authored/default ErenshorPersona;
    //  3) this fictional person-behind-the-character background exists only for MMO perspective.
    // Generated text is mundane, bounded, deterministic, and sidecar-owned. It is not real user data
    // and is never evidence about native Erenshor state.
    internal static class SimulatedPlayerBackgroundPolicy
    {
        internal const string GeneratorVersion = "mmo-background-v1";

        private static readonly string[] Backgrounds = new string[]
        {
            "Works a regular office schedule and usually logs on in the evening. Likes exploration and helping newer players; weekends are a little more flexible.",
            "Works rotating shifts, so login times can be irregular. Likes short dungeon runs and crafting when there is not time for a long session.",
            "Is a student with an ordinary schedule and usually plays after classes. Likes exploring and small-group play more than rushing progression.",
            "Is between jobs right now and has a fairly flexible schedule. Likes raiding, tinkering with gear, and relaxed group chat.",
            "Works daytime hours and mostly plays after dinner. Likes dependable groups, learning encounters, and keeping sessions low-drama.",
            "Has a busy weekday schedule and tends to log on for shorter evening sessions. Likes gathering, crafting, and helping friends finish things.",
            "Works later hours than most people and is often online at odd times. Likes dungeon groups, experimenting with builds, and joking through wipes.",
            "Has a fairly ordinary work-and-home routine and uses Erenshor to unwind. Likes exploration, steady progression, and familiar party groups."
        };

        internal static bool Ensure(SimMemory memory, SimSnapshot sim)
        {
            if (memory == null) return false;
            if (!string.IsNullOrWhiteSpace(memory.GeneratedSimulatedPlayerBackground))
            {
                if (string.IsNullOrWhiteSpace(memory.GeneratedSimulatedPlayerBackgroundVersion))
                {
                    memory.GeneratedSimulatedPlayerBackgroundVersion = GeneratorVersion;
                    return true;
                }
                return false;
            }
            string stable = sim != null && !string.IsNullOrWhiteSpace(sim.Key) ? sim.Key : memory.SimKey;
            if (string.IsNullOrWhiteSpace(stable)) stable = sim != null && !string.IsNullOrWhiteSpace(sim.Name) ? sim.Name : memory.Name;
            if (string.IsNullOrWhiteSpace(stable)) return false; // conservative unknown
            uint hash = DefaultIdentityPolicy.StableHash(stable.Trim().ToLowerInvariant());
            memory.GeneratedSimulatedPlayerBackground = Backgrounds[(int)(hash % (uint)Backgrounds.Length)];
            memory.GeneratedSimulatedPlayerBackgroundVersion = GeneratorVersion;
            return true;
        }

        internal static bool IsBackgroundQuestion(string text)
        {
            return Regex.IsMatch(text ?? string.Empty,
                @"\b(?:irl|real life|outside erenshor|outside the game|background|college|school|study|studying|job|career|work|weekend|weekends|online so late|up so late|log on|login|other games|what games|outside this game|outside the game|do for fun|do outside|free time)\b",
                RegexOptions.IgnoreCase);
        }

        internal static string StatusLabel(SimMemory memory)
        {
            if (memory != null && memory.AuthoredIdentity != null && !string.IsNullOrWhiteSpace(memory.AuthoredIdentity.PersonalBackground)) return "Authored";
            if (memory != null && !string.IsNullOrWhiteSpace(memory.GeneratedSimulatedPlayerBackground)) return "Generated";
            return "Unknown";
        }
    }

    internal static class GeneratedIdentityMotivationPolicy
    {
        internal const string GeneratorVersion = "identity-motivations-v1";

        internal static bool Ensure(SimMemory memory, SimSnapshot sim)
        {
            if (memory == null || sim == null || string.IsNullOrWhiteSpace(sim.Key)) return false;
            bool changed = false;
            DefaultIdentityProfile defaults = DefaultIdentityPolicy.Build(sim);
            if (string.IsNullOrWhiteSpace(memory.GeneratedLongTermWants) && !string.IsNullOrWhiteSpace(defaults.LongTermWants))
            {
                memory.GeneratedLongTermWants = defaults.LongTermWants;
                changed = true;
            }
            if (string.IsNullOrWhiteSpace(memory.GeneratedCaresAbout) && !string.IsNullOrWhiteSpace(defaults.CaresAbout))
            {
                memory.GeneratedCaresAbout = defaults.CaresAbout;
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(memory.GeneratedLongTermWants) &&
                !string.IsNullOrWhiteSpace(memory.GeneratedCaresAbout) &&
                string.IsNullOrWhiteSpace(memory.GeneratedIdentityMotivationVersion))
            {
                memory.GeneratedIdentityMotivationVersion = GeneratorVersion;
                changed = true;
            }
            return changed;
        }
    }
}
