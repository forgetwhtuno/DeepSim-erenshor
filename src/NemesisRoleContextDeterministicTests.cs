using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    // Pure renderer coverage: no Nemesis assembly, game state, or model call is required.
    internal static class NemesisRoleContextDeterministicTests
    {
        internal static List<string> Run()
        {
            List<string> lines = new List<string>();
            SimSnapshot aiyamon = Sim("Aiyamon", 29);
            SimSnapshot sameNameDifferentId = Sim("Aiyamon", 30);
            SimSnapshot birdie = Sim("Birdie", 7);
            WorldSnapshot party = new WorldSnapshot { Party = new List<SimSnapshot> { aiyamon } };
            NemesisRoleContextSnapshot active = Role(29, "new", "practice_duel_completed", "nemesis_win", 1);

            Add(lines, "active exact stable-ID match renders role", Contains(NemesisRolePrompt.Render(aiyamon, party, active), "designated new rival"));
            Add(lines, "non-Nemesis renders no role", NemesisRolePrompt.Render(birdie, party, active).Length == 0);
            Add(lines, "same display name different stable ID renders no role", NemesisRolePrompt.Render(sameNameDifferentId, party, active).Length == 0);
            Add(lines, "unavailable role renders no role", NemesisRolePrompt.Render(aiyamon, party, null).Length == 0);
            Add(lines, "no active rival renders no role", NemesisRolePrompt.Render(aiyamon, party, new NemesisRoleContextSnapshot { StableSimId = -1, Stage = "new" }).Length == 0);
            string early = NemesisRolePrompt.Render(aiyamon, party, active);
            Add(lines, "new stage is restrained", Contains(early, "early rivalry with limited competitive history") && !Contains(early, "hatred"));
            Add(lines, "rival stage is established but bounded", Contains(NemesisRolePrompt.Render(aiyamon, party, Role(29, "rival", "", "", 5)), "established designated rival"));
            Add(lines, "heated stage is stronger but non-cartoonish", Contains(NemesisRolePrompt.Render(aiyamon, party, Role(29, "heated", "", "", 12)), "designated heated rival") && !Contains(NemesisRolePrompt.Render(aiyamon, party, Role(29, "heated", "", "", 12)), "obsession"));
            Add(lines, "party membership and rivalry coexist", Contains(early, "currently cooperating with the player as a party member") && Contains(early, "Party membership does not erase the rivalry"));
            aiyamon.FriendStateKnown = true; aiyamon.IsFriend = true;
            Add(lines, "native friend state remains separate from rival role", aiyamon.IsFriend && Contains(NemesisRolePrompt.Render(aiyamon, party, active), "designated new rival"));
            Add(lines, "completed duel supplies verified outcome", Contains(early, "won one completed Practice Duel"));
            Add(lines, "cancelled/rejected duel is not completed history", !Contains(NemesisRolePrompt.Render(aiyamon, party, Role(29, "new", "practice_duel_cancelled", "cancelled", 0)), "Practice Duel"));
            Add(lines, "raw taunt type is not factual role history", !Contains(NemesisRolePrompt.Render(aiyamon, party, Role(29, "new", "nemesis_taunt", "", 0)), "Verified rivalry history"));
            Add(lines, "diagnostic is content-free", !Contains(NemesisRolePrompt.Diagnostic(aiyamon, active), "Practice Duel") && Contains(NemesisRolePrompt.Diagnostic(aiyamon, active), "speakerStableId=29"));
            return lines;
        }

        private static SimSnapshot Sim(string name, int id) { return new SimSnapshot { Name = name, NativeStableSimId = id, ClassName = "Paladin", Level = 3 }; }
        private static NemesisRoleContextSnapshot Role(int id, string stage, string type, string result, int count)
        { return new NemesisRoleContextSnapshot { IsActiveNemesis = true, StableSimId = id, NemesisName = "Aiyamon", Stage = stage, LatestVerifiedCompetitiveEvent = type, LatestVerifiedResult = result, CompetitiveInteractionCount = count }; }
        private static bool Contains(string value, string expected) { return (value ?? string.Empty).IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0; }
        private static void Add(List<string> lines, string name, bool pass) { lines.Add("[DeepSims NemesisRole " + (pass ? "PASS" : "FAIL") + "] " + name); }
    }
}
