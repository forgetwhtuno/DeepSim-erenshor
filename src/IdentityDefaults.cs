using System;
using System.Collections.Generic;
using System.Text;

namespace ErenshorDeepSims
{
    internal enum IdentityValueSource
    {
        None = 0,
        DefaultTemplate = 1,
        AuthoredOverride = 2,
        PersistedGeneratedDefault = 3
    }

    internal sealed class DefaultIdentityProfile
    {
        internal string CorePersonality = string.Empty;
        internal string PersonalBackground = string.Empty;
        internal string ErenshorPersona = string.Empty;
        internal string RelationshipToPlayer = string.Empty;
        internal string LongTermWants = string.Empty;
        internal string CaresAbout = string.Empty;
        internal string Summary = string.Empty;
        internal string StableVariation = string.Empty;
        internal string ClassTemplate = string.Empty;
        internal string ExperienceTone = string.Empty;
        internal GeneratedPersonalityDimensions Dimensions = new GeneratedPersonalityDimensions();
    }

    internal sealed class GeneratedPersonalityDimensions
    {
        internal int SocialEnergy;
        internal int RiskTolerance;
        internal int Improvisation;
        internal int Competitiveness;
        internal int Curiosity;
        internal int Patience;
        internal int WillingnessToDisagree;
        internal int Directness;
        internal int GroupOrientation;
        internal string HumorStyle = "light";
        internal string PreferredInterests = "everyday adventuring";
    }

    // Deterministic roleplay scaffolding only. Nothing produced here is persisted and none of it is
    // evidence that a specific life event, origin, affiliation, relationship, or accomplishment
    // happened. Live/native facts and explicit authored/learned records remain separate authorities.
    internal static class DefaultIdentityPolicy
    {
        internal static readonly string[] CurrentClasses = new string[]
        {
            "Arcanist", "Druid", "Paladin", "Reaver", "Stormcaller", "Windblade"
        };

        private static readonly string[] SocialAxis = new string[] { "reserved", "outgoing" };
        private static readonly string[] MoodAxis = new string[] { "serious", "playful" };
        private static readonly string[] PaceAxis = new string[] { "patient", "impatient" };
        private static readonly string[] RegisterAxis = new string[] { "formal", "casual" };

        internal static DefaultIdentityProfile Build(SimSnapshot sim)
        {
            DefaultIdentityProfile result = new DefaultIdentityProfile();
            string cls = SimContextReader.NormalizeClassName(sim == null ? string.Empty : sim.ClassName);
            if (string.IsNullOrWhiteSpace(cls) || string.Equals(cls, "unknown class", StringComparison.OrdinalIgnoreCase)) cls = "adventurer";

            string stableKey = StableIdentityKey(sim);
            uint hash = StableHash(stableKey);
            string variation = BuildVariation(hash);
            string nativePersonality = NativePersonalityAnchor(sim);
            string experience = ExperienceTone(sim == null ? 0 : sim.Level);
            string classTemplate = ClassTemplateFor(cls);
            result.Dimensions = BuildDimensions(sim, cls, hash);

            result.StableVariation = variation;
            result.ExperienceTone = experience;
            result.ClassTemplate = classTemplate;
            result.CorePersonality = nativePersonality + " " + variation + ". " + experience + ".";
            // The roleplay scaffold deliberately has NO real-world/player biography. MMO background
            // lives in SimulatedPlayerBackgroundPolicy and is persisted separately from this in-world
            // persona so perspective switching cannot leak one layer into the other.
            result.PersonalBackground = string.Empty;
            result.ErenshorPersona = "An Erenshor adventurer comfortable discussing travel, dangerous expeditions, group tactics, recovery, equipment, and everyday adventuring life. " + classTemplate;
            result.RelationshipToPlayer = RelationshipTemplate(hash);
            result.LongTermWants = WantsTemplate(cls, hash);
            result.CaresAbout = CaresTemplate(cls, hash);
            result.Summary = cls + " adventurer; " + nativePersonality + " " + variation + ". " + experience + ". " +
                "This is a non-factual roleplay scaffold, not biography or verified history.";
            return result;
        }

        internal static GeneratedPersonalityDimensions BuildDimensions(SimSnapshot sim, string className, uint hash)
        {
            GeneratedPersonalityDimensions d = new GeneratedPersonalityDimensions();
            d.SocialEnergy = Axis(hash, 0);
            d.RiskTolerance = Axis(hash, 3);
            d.Improvisation = Axis(hash, 6);
            d.Competitiveness = Axis(hash, 9);
            d.Curiosity = Axis(hash, 12);
            d.Patience = Axis(hash, 15);
            d.WillingnessToDisagree = Axis(hash, 18);
            d.Directness = Axis(hash, 21);
            d.GroupOrientation = Axis(hash, 24);

            // Class and native temperament are bounded nudges, never fixed profiles or history.
            if (string.Equals(className, "Arcanist", StringComparison.OrdinalIgnoreCase)) { d.Curiosity++; d.RiskTolerance--; }
            else if (string.Equals(className, "Druid", StringComparison.OrdinalIgnoreCase)) { d.Patience++; d.GroupOrientation++; }
            else if (string.Equals(className, "Paladin", StringComparison.OrdinalIgnoreCase)) { d.GroupOrientation++; d.Improvisation--; }
            else if (string.Equals(className, "Reaver", StringComparison.OrdinalIgnoreCase)) { d.Directness++; d.RiskTolerance++; }
            else if (string.Equals(className, "Stormcaller", StringComparison.OrdinalIgnoreCase)) { d.Curiosity++; d.Improvisation--; }
            else if (string.Equals(className, "Windblade", StringComparison.OrdinalIgnoreCase)) { d.Improvisation++; d.SocialEnergy++; }
            if (sim != null)
            {
                if (sim.Rival || sim.PersonalityCode == 2) { d.Competitiveness++; d.WillingnessToDisagree++; }
                if (sim.PersonalityCode == 3) { d.Directness++; d.WillingnessToDisagree++; d.SocialEnergy--; }
                if (sim.PersonalityCode == 0 || sim.PersonalityCode == 1) { d.GroupOrientation++; }
                if (sim.Patience > 0) d.Patience += sim.Patience >= 65 ? 1 : (sim.Patience <= 35 ? -1 : 0);
            }
            d.SocialEnergy = ClampAxis(d.SocialEnergy); d.RiskTolerance = ClampAxis(d.RiskTolerance);
            d.Improvisation = ClampAxis(d.Improvisation); d.Competitiveness = ClampAxis(d.Competitiveness);
            d.Curiosity = ClampAxis(d.Curiosity); d.Patience = ClampAxis(d.Patience);
            d.WillingnessToDisagree = ClampAxis(d.WillingnessToDisagree); d.Directness = ClampAxis(d.Directness);
            d.GroupOrientation = ClampAxis(d.GroupOrientation);
            string[] humor = { "dry", "understated", "playful", "wry", "rare but warm" };
            d.HumorStyle = humor[(int)((hash >> 27) % (uint)humor.Length)];
            d.PreferredInterests = InterestFor(className, hash);
            return d;
        }

        private static int Axis(uint hash, int shift) { return (int)((hash >> shift) & 7u) % 5; }
        private static int ClampAxis(int value) { return Math.Max(0, Math.Min(4, value)); }

        private static string InterestFor(string className, uint hash)
        {
            string[] common = { "exploration and discoveries", "group tactics and preparation", "equipment choices", "difficult fights and recovery", "travel and everyday party life" };
            string weighted = string.Equals(className, "Arcanist", StringComparison.OrdinalIgnoreCase) ? "magic and discoveries" :
                string.Equals(className, "Druid", StringComparison.OrdinalIgnoreCase) ? "recovery and preparation" :
                string.Equals(className, "Paladin", StringComparison.OrdinalIgnoreCase) ? "protection and group tactics" :
                string.Equals(className, "Reaver", StringComparison.OrdinalIgnoreCase) ? "difficult fights and resilience" :
                string.Equals(className, "Stormcaller", StringComparison.OrdinalIgnoreCase) ? "timing and exploration" :
                string.Equals(className, "Windblade", StringComparison.OrdinalIgnoreCase) ? "decisive action and improvisation" : common[0];
            return ((hash >> 29) & 1u) == 0u ? weighted : common[(int)((hash >> 25) % (uint)common.Length)];
        }

        internal static bool HasSpecificClassTemplate(string className)
        {
            string normalized = SimContextReader.NormalizeClassName(className);
            for (int i = 0; i < CurrentClasses.Length; i++)
                if (string.Equals(CurrentClasses[i], normalized, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static bool ContainsUnsupportedSpecificHistory(string value)
        {
            string lower = (value ?? string.Empty).ToLowerInvariant();
            string[] forbidden = new string[]
            {
                "born in", "birthplace", "mother", "father", "parents", "sister", "brother", "siblings",
                "wife", "husband", "married", "mentor", "master taught", "graduated", "served in the war",
                "fought in the war", "lost my", "dead family", "secret society", "member of the order",
                "member of an order", "temple member", "academy graduate", "defeated the", "completed the quest"
            };
            for (int i = 0; i < forbidden.Length; i++) if (lower.Contains(forbidden[i])) return true;
            return false;
        }

        private static string RelationshipTemplate(uint hash)
        {
            string[] baselines = new string[]
            {
                "Starts from polite fellow-adventurer respect and lets verified shared experience establish any greater familiarity.",
                "Starts socially reserved but cooperative with the player; no friendship, rivalry, romance, or long shared history is assumed.",
                "Starts casually approachable with the player while treating closeness and shared history as things that must be verified.",
                "Starts practical and task-focused with the player, with trust left to authored or verified shared experience."
            };
            return baselines[(int)((hash >> 8) % (uint)baselines.Length)];
        }

        private static string WantsTemplate(string className, uint hash)
        {
            string classGoal;
            if (string.Equals(className, "Arcanist", StringComparison.OrdinalIgnoreCase)) classGoal = "understand difficult encounters and use magic with better judgment";
            else if (string.Equals(className, "Druid", StringComparison.OrdinalIgnoreCase)) classGoal = "keep groups resilient and become more adaptable when plans go wrong";
            else if (string.Equals(className, "Paladin", StringComparison.OrdinalIgnoreCase)) classGoal = "become a more dependable protector without taking reckless risks for the group";
            else if (string.Equals(className, "Reaver", StringComparison.OrdinalIgnoreCase)) classGoal = "push their limits while learning when survival matters more than momentum";
            else if (string.Equals(className, "Stormcaller", StringComparison.OrdinalIgnoreCase)) classGoal = "improve timing and awareness so dangerous fights stay controlled";
            else if (string.Equals(className, "Windblade", StringComparison.OrdinalIgnoreCase)) classGoal = "turn quick decisions into reliable results without outrunning the group";
            else classGoal = "grow into a capable and reliable adventuring companion";
            string[] concerns = new string[]
            {
                "They want to " + classGoal + ", and worry most about avoidable mistakes becoming someone else's burden.",
                "They want to " + classGoal + ", while keeping setbacks from making the party lose confidence.",
                "They want to " + classGoal + ", and prefer steady improvement over claims of glory or status.",
                "They want to " + classGoal + ", while preserving enough independence to make honest choices."
            };
            return concerns[(int)((hash >> 12) % (uint)concerns.Length)];
        }

        private static string CaresTemplate(string className, uint hash)
        {
            string classCare;
            if (string.Equals(className, "Arcanist", StringComparison.OrdinalIgnoreCase)) classCare = "clear thinking, useful knowledge, and disciplined use of power";
            else if (string.Equals(className, "Druid", StringComparison.OrdinalIgnoreCase)) classCare = "recovery, adaptability, and noticing what keeps a group healthy";
            else if (string.Equals(className, "Paladin", StringComparison.OrdinalIgnoreCase)) classCare = "responsibility, protection, and promises that can actually be kept";
            else if (string.Equals(className, "Reaver", StringComparison.OrdinalIgnoreCase)) classCare = "resilience, candor, and companions who keep moving under pressure";
            else if (string.Equals(className, "Stormcaller", StringComparison.OrdinalIgnoreCase)) classCare = "awareness, precise timing, and giving others room to act";
            else if (string.Equals(className, "Windblade", StringComparison.OrdinalIgnoreCase)) classCare = "decisive action, adaptability, and companions who can trust each other's momentum";
            else classCare = "competence, practical cooperation, and learning from setbacks";
            string[] socialCare = new string[]
            {
                "Cares about " + classCare + "; also values low-drama companionship and honest feedback.",
                "Cares about " + classCare + "; also values shared effort and humor that does not come at a companion's expense.",
                "Cares about " + classCare + "; also values personal space, fairness, and people following through.",
                "Cares about " + classCare + "; also values curiosity, mutual respect, and calm after a difficult moment."
            };
            return socialCare[(int)((hash >> 16) % (uint)socialCare.Length)];
        }

        private static string ClassTemplateFor(string className)
        {
            if (string.Equals(className, "Arcanist", StringComparison.OrdinalIgnoreCase))
                return "Tends to approach problems through magic, observation, control, and curiosity rather than brute force.";
            if (string.Equals(className, "Druid", StringComparison.OrdinalIgnoreCase))
                return "Tends to notice survival, recovery, natural surroundings, adaptability, and keeping a group functioning.";
            if (string.Equals(className, "Paladin", StringComparison.OrdinalIgnoreCase))
                return "Tends toward discipline, protection, frontline responsibility, and measured confidence.";
            if (string.Equals(className, "Reaver", StringComparison.OrdinalIgnoreCase))
                return "Tends to be direct, resilient, and comfortable balancing aggression with survival in close combat.";
            if (string.Equals(className, "Stormcaller", StringComparison.OrdinalIgnoreCase))
                return "Tends to be observant, mobile, and practical, favoring timing, distance, and precision.";
            if (string.Equals(className, "Windblade", StringComparison.OrdinalIgnoreCase))
                return "Tends toward quick decisions, aggressive momentum, improvisation, and confidence in close combat.";
            return "Approaches adventure pragmatically and adapts to the needs of the group.";
        }

        private static string ExperienceTone(int level)
        {
            if (level > 0 && level <= 8) return "Uses a somewhat less-seasoned, more tentative tone around unfamiliar danger without inventing past failures";
            if (level > 8 && level <= 20) return "Sounds increasingly practiced and steady without implying specific accomplishments";
            if (level > 20) return "May sound more composed and confident without claiming any particular victory, quest, or exploit";
            return "Uses a capable but non-specific adventurer tone without inventing accomplishments";
        }

        private static string BuildVariation(uint hash)
        {
            // Stable variation is deliberately limited to conversational style dimensions. Native
            // Erenshor personality remains the behavioral authority, so generated defaults never
            // independently label a cautious/blunt/friendly Sim as its opposite.
            string a = SocialAxis[(int)(hash & 1u)];
            string c = MoodAxis[(int)((hash >> 1) & 1u)];
            string d = PaceAxis[(int)((hash >> 2) & 1u)];
            string f = RegisterAxis[(int)((hash >> 3) & 1u)];
            return "Usually " + a + " socially, " + c + " in ordinary conversation, " + d + " with setbacks, and relatively " + f + " in speech";
        }

        private static string NativePersonalityAnchor(SimSnapshot sim)
        {
            if (sim == null) return "Native personality is unavailable; generated defaults only provide light conversational style.";
            if (sim.Rival) return "Native personality authority: rival / antagonistic.";
            switch (sim.PersonalityCode)
            {
                case 0:
                case 1: return "Native personality authority: nice / friendly.";
                case 2: return "Native personality authority: tryhard / competitive.";
                case 3: return "Native personality authority: mean / blunt.";
            }
            if (!string.IsNullOrWhiteSpace(sim.Personality) && !string.Equals(sim.Personality, "unknown", StringComparison.OrdinalIgnoreCase))
                return "Native personality authority: " + sim.Personality.Trim() + ".";
            if (!string.IsNullOrWhiteSpace(sim.Bio) || !string.IsNullOrWhiteSpace(sim.PersonalityRaw))
                return "Native personality authority comes from the Sim's bio/dialogue examples; generated defaults only provide light conversational style.";
            return "Native personality is unavailable; generated defaults only provide light conversational style.";
        }

        private static string StableIdentityKey(SimSnapshot sim)
        {
            if (sim == null) return "unknown-sim";
            if (!string.IsNullOrWhiteSpace(sim.Key)) return sim.Key.Trim().ToLowerInvariant();
            return ((sim.Name ?? string.Empty) + "|" + (sim.ClassName ?? string.Empty)).Trim().ToLowerInvariant();
        }

        internal static uint StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string source = value ?? string.Empty;
                for (int i = 0; i < source.Length; i++)
                {
                    hash ^= source[i];
                    hash *= 16777619u;
                }
                return hash;
            }
        }
    }
}
