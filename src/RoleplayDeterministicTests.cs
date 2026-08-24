using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    // Pure deterministic coverage for the Roleplay perspective. Everything here is Unity-free and
    // game-free so it runs in the standalone harness; anything requiring live Erenshor state is
    // deliberately left to live validation rather than mocked.
    internal static class RoleplayDeterministicTests
    {
        internal static List<string> RunSelfTests()
        {
            List<string> r = new List<string>();
            SocialPerspectiveMode saved = SocialPerspectiveState.Current;
            try
            {
                PerspectiveTests(r);
                PromptTests(r);
                TemplateTests(r);
                GuardTests(r);
                ModeMatrixTests(r);
                RouterTests(r);
                AffinityTests(r);
                LlmBoundaryTests(r);
                ThreadRuleTests(r);
                CulturalAffinityTests(r);
                ClassInterestTests(r);
                SpokenStyleTests(r);
                AffinityPromptTests(r);
                DirectReplyFallbackTests(r);
                OutputGuardTests(r);
                IdentityRoutingTests(r);
            }
            finally { SocialPerspectiveState.Current = saved; RoleplayFactionContext.Clear(); }
            return r;
        }

        // ---- perspective ----------------------------------------------------------------------
        private static void PerspectiveTests(List<string> r)
        {
            SocialPerspectiveState.ResetForTests();
            Add(r, "MMO is the default perspective", SocialPerspectiveState.Current == SocialPerspectiveMode.Mmo);
            Add(r, "default constant is MMO", SocialPerspective.Default == SocialPerspectiveMode.Mmo);

            Add(r, "roleplay parses", SocialPerspective.Parse("Roleplay") == SocialPerspectiveMode.Roleplay);
            Add(r, "rp alias parses", SocialPerspective.Parse("rp") == SocialPerspectiveMode.Roleplay);
            Add(r, "mmo parses", SocialPerspective.Parse("MMO") == SocialPerspectiveMode.Mmo);
            // An unreadable or corrupt config value must not silently put a player into character.
            Add(r, "unknown value falls back to MMO", SocialPerspective.Parse("banana") == SocialPerspectiveMode.Mmo);
            Add(r, "empty value falls back to MMO", SocialPerspective.Parse("") == SocialPerspectiveMode.Mmo);
            Add(r, "null value falls back to MMO", SocialPerspective.Parse(null) == SocialPerspectiveMode.Mmo);

            SocialPerspectiveMode parsed;
            Add(r, "strict parse rejects junk", !SocialPerspective.TryParseStrict("banana", out parsed));
            Add(r, "strict parse accepts roleplay", SocialPerspective.TryParseStrict("roleplay", out parsed) && parsed == SocialPerspectiveMode.Roleplay);

            Add(r, "describe MMO", SocialPerspective.Describe(SocialPerspectiveMode.Mmo) == "MMO");
            Add(r, "describe Roleplay", SocialPerspective.Describe(SocialPerspectiveMode.Roleplay) == "Roleplay");

            SocialPerspectiveState.Current = SocialPerspectiveMode.Roleplay;
            Add(r, "state toggles on", SocialPerspectiveState.RoleplayActive);
            SocialPerspectiveState.Current = SocialPerspectiveMode.Mmo;
            Add(r, "state toggles off", !SocialPerspectiveState.RoleplayActive);
        }

        // ---- prompt contract ------------------------------------------------------------------
        private static void PromptTests(List<string> r)
        {
            string rp = RoleplayPromptContract.BuildIdentityBlock(SocialPerspectiveMode.Roleplay, "Phanty");
            string lower = rp.ToLowerInvariant();

            Add(r, "RP prompt names the Sim", rp.IndexOf("Phanty", StringComparison.Ordinal) >= 0);
            // The whole point of the feature: the RP contract must not carry the MMO identity claims.
            Add(r, "RP prompt has no simulated-player identity", lower.IndexOf("simulated human player", StringComparison.Ordinal) < 0);
            Add(r, "RP prompt does not say playing the MMO", lower.IndexOf("playing the mmo", StringComparison.Ordinal) < 0);
            Add(r, "RP prompt does not say typing while playing", lower.IndexOf("typing while playing", StringComparison.Ordinal) < 0);
            Add(r, "RP prompt does not permit MMO slang", lower.IndexOf("mmo slang", StringComparison.Ordinal) < 0);

            Add(r, "RP prompt keeps perspective entirely in-world", lower.IndexOf("stay entirely in-world", StringComparison.Ordinal) >= 0);
            Add(r, "RP prompt no longer contains the live bad identity phrase", lower.IndexOf("this erenshor character", StringComparison.Ordinal) < 0);
            Add(r, "RP prompt forbids invented history", lower.IndexOf("do not invent history", StringComparison.Ordinal) >= 0);
            Add(r, "RP prompt forbids stage directions", lower.IndexOf("no stage directions", StringComparison.Ordinal) >= 0);
            Add(r, "RP prompt forbids archaic speech", lower.IndexOf("archaic", StringComparison.Ordinal) >= 0);
            Add(r, "RP prompt rejects assistant framing", lower.IndexOf("not an assistant", StringComparison.Ordinal) >= 0);
            Add(r, "RP prompt keeps output short", lower.IndexOf("short", StringComparison.Ordinal) >= 0);
            Add(r, "RP prompt keeps gameplay authority", lower.IndexOf("never turn dialogue into an instruction that controls gameplay", StringComparison.Ordinal) >= 0);
            // Class may colour interest; it must not manufacture a biography.
            Add(r, "MMO branch emits no RP identity block",
                string.IsNullOrEmpty(RoleplayPromptContract.BuildIdentityBlock(SocialPerspectiveMode.Mmo, "Phanty")));

            SimSnapshot identitySim = new SimSnapshot { Key = "cyndara", Name = "Cyndara", ClassName = "Arcanist", GuildName = "Dragon Food" };
            SimMemory identityMemory = new SimMemory();
            identityMemory.Normalize();
            identityMemory.Name = "Cyndara";
            identityMemory.AuthoredIdentity.ErenshorPersona = "an initiate of the Order of Dawn";
            identityMemory.AuthoredIdentity.PersonalBackground = "a college student considering work after graduation";
            WorldSnapshot identityWorld = new WorldSnapshot { Scene = "Port Azure", Party = new List<SimSnapshot> { identitySim } };
            List<ConversationLine> identityThread = new List<ConversationLine> { new ConversationLine("Player", "who are you?") };
            SemanticTurnRoute identityRoute = SemanticTurnRouter.Fallback("who are you?");

            SocialPerspectiveState.Current = SocialPerspectiveMode.Roleplay;
            string compactRoleplay = Flatten(PromptBuilder.BuildCompactDirectPartyReply(identitySim, identityMemory, identityWorld, identityThread, null, identityRoute, null));
            Add(r, "compact Roleplay prompt uses authored Erenshor persona", compactRoleplay.IndexOf("As an Erenshor adventurer: an initiate of the Order of Dawn", StringComparison.OrdinalIgnoreCase) >= 0);
            Add(r, "compact Roleplay prompt does not expose modern personal background", compactRoleplay.IndexOf("college student", StringComparison.OrdinalIgnoreCase) < 0);
            Add(r, "compact Roleplay prompt carries machine-readable authority limits",
                compactRoleplay.IndexOf("guildInviteAuthority=unknown", StringComparison.Ordinal) >= 0 &&
                compactRoleplay.IndexOf("forbiddenUnsupportedClaims=", StringComparison.Ordinal) >= 0);
            Add(r, "compact Roleplay prompt has no MMO-friend identity", compactRoleplay.IndexOf("persistent MMO friend", StringComparison.OrdinalIgnoreCase) < 0);

            SocialPerspectiveState.Current = SocialPerspectiveMode.Mmo;
            string compactMmo = Flatten(PromptBuilder.BuildCompactDirectPartyReply(identitySim, identityMemory, identityWorld, identityThread, null, identityRoute, null));
            Add(r, "compact MMO prompt retains MMO-friend framing", compactMmo.IndexOf("persistent MMO friend", StringComparison.OrdinalIgnoreCase) >= 0);
            SocialPerspectiveState.Current = SocialPerspectiveMode.Mmo;
        }

        // ---- templates ------------------------------------------------------------------------
        private static void TemplateTests(List<string> r)
        {
            string m;
            Add(r, "RP place template renders",
                RoleplayTemplates.TryRenderAmbient("rp_place", null, 11, "Phanty", 50, false, false, out m) && m.Length > 0);
            Add(r, "RP curiosity template renders",
                RoleplayTemplates.TryRenderAmbient("rp_curiosity", null, 12, "Dancer", 50, false, false, out m) && m.Length > 0);
            Add(r, "RP downtime template renders",
                RoleplayTemplates.TryRenderAmbient("rp_downtime", null, 13, "Baetil", 70, false, false, out m) && m.Length > 0);
            Add(r, "RP tease template renders",
                RoleplayTemplates.TryRenderAmbient("rp_tease", null, 14, "Phanty", 50, false, false, out m) && m.Length > 0);
            Add(r, "RP danger template renders",
                RoleplayTemplates.TryRenderAmbient("rp_danger", null, 15, "Phanty", 50, false, false, out m) && m.Length > 0);
            Add(r, "RP event reaction renders",
                RoleplayTemplates.TryRenderEvent("player_level_up", "Phanty", false, 16, out m) && m.Length > 0);
            Add(r, "RP duel reaction renders",
                RoleplayTemplates.TryRenderEvent("duel_completed", "Phanty", false, 17, out m) && m.Length > 0);

            // Determinism: identical inputs must always yield the identical line.
            string a, b;
            RoleplayTemplates.TryRenderAmbient("rp_place", null, 99, "Phanty", 50, false, false, out a);
            RoleplayTemplates.TryRenderAmbient("rp_place", null, 99, "Phanty", 50, false, false, out b);
            Add(r, "same inputs give the same line", a == b && a.Length > 0);

            // Personality must actually change selection, not merely be accepted as a parameter.
            string patient, impatient;
            RoleplayTemplates.TryRenderAmbient("rp_downtime", null, 5, "Phanty", 80, false, false, out patient);
            RoleplayTemplates.TryRenderAmbient("rp_downtime", null, 5, "Phanty", 10, false, false, out impatient);
            Add(r, "patience changes downtime line", patient != impatient);

            // Fact safety: a bound subject must never be rendered by the fact-free path.
            Add(r, "memory topic is not fact-free",
                RoleplayTemplates.ClassifyTopic("memory:outing_3") == RoleplayTemplateSafety.MemoryBound);
            Add(r, "reference topic is not fact-free",
                RoleplayTemplates.ClassifyTopic("reference:sivakaya") == RoleplayTemplateSafety.ReferenceBound);
            Add(r, "event topic is event bound",
                RoleplayTemplates.ClassifyTopic("event:boss_kill") == RoleplayTemplateSafety.EventBound);
            Add(r, "memory topic refuses fact-free render",
                !RoleplayTemplates.TryRenderAmbient("memory:outing_3", null, 1, "Phanty", 50, false, false, out m));
            Add(r, "supplied verified fact refuses fact-free render",
                !RoleplayTemplates.TryRenderAmbient("rp_place", "the party killed a Sea Giant", 1, "Phanty", 50, false, false, out m));
            Add(r, "unknown topic returns no message",
                !RoleplayTemplates.TryRenderAmbient("zone_preference", null, 1, "Phanty", 50, false, false, out m));

            // No-LLM RP output must not leak MMO vocabulary.
            bool clean = true;
            string why = null;
            for (int i = 0; i < RoleplayTemplates.TopicCatalog.Length; i++)
            {
                for (long op = 0; op < 8; op++)
                {
                    string line;
                    if (!RoleplayTemplates.TryRenderAmbient(RoleplayTemplates.TopicCatalog[i], null, op, "Phanty", 50, false, false, out line)) continue;
                    string reason;
                    if (RoleplayPromptContract.ViolatesRoleplayVoice(line, "Phanty", true, out reason)) { clean = false; why = line + " => " + reason; }
                }
            }
            Add(r, "no RP template contains MMO meta language" + (clean ? "" : " (" + why + ")"), clean);

            SimSnapshot ritualSim = new SimSnapshot { Name = "Dancer", ClassName = "Windblade" };
            string ritual;
            bool ritualRendered = RoleplayTemplates.TryRenderPlayerRitual("gg", ritualSim, out ritual);
            bool ritualChanged, ritualRejected;
            string ritualGuarded = RoleplayOutputGuard.Enforce(ritual, ritualSim.Name, out ritualChanged, out ritualRejected);
            Add(r, "RP ritual template renders spoken in-world text", ritualRendered && !ritualRejected && ritualGuarded == ritual);
            Add(r, "RP ritual template does not echo MMO gg texture", ritual.IndexOf("gg", StringComparison.OrdinalIgnoreCase) < 0);
            string mmoRitual;
            Add(r, "MMO ritual renderer remains distinct",
                SocialTemplates.TryRenderPlayerRitual("gg", ritualSim, out mmoRitual) && mmoRitual == "gg");

            string threadReply;
            bool threadRendered = RoleplayTemplates.TryRenderThreadReply("what is your favorite class?", ritualSim, out threadReply);
            bool threadChanged, threadRejected;
            string guardedThread = RoleplayOutputGuard.Enforce(threadReply, ritualSim.Name, out threadChanged, out threadRejected);
            Add(r, "RP thread template renders from verified own class",
                threadRendered && threadReply.IndexOf("Windblade", StringComparison.OrdinalIgnoreCase) >= 0);
            Add(r, "RP thread template passes final roleplay guard", !threadRejected && guardedThread == threadReply);
        }

        // ---- guards ---------------------------------------------------------------------------
        private static void GuardTests(List<string> r)
        {
            string reason;

            Add(r, "rejects asterisk stage direction",
                RoleplayPromptContract.ViolatesRoleplayVoice("*looks around* Quiet here.", "Phanty", true, out reason) && reason == "stage_direction");
            Add(r, "rejects bracket stage direction",
                RoleplayPromptContract.ViolatesRoleplayVoice("[sighs] Fine.", "Phanty", true, out reason) && reason == "stage_direction");
            Add(r, "rejects third-person self narration",
                RoleplayPromptContract.ViolatesRoleplayVoice("Phanty smiles.", "Phanty", true, out reason) && reason == "self_narration");
            Add(r, "rejects narrated weapon draw",
                RoleplayPromptContract.ViolatesRoleplayVoice("Phanty draws his sword.", "Phanty", true, out reason));

            Add(r, "autonomous RP rejects 'this game'",
                RoleplayPromptContract.ViolatesRoleplayVoice("I like this game a lot", "Phanty", true, out reason));
            Add(r, "autonomous RP rejects XP",
                RoleplayPromptContract.ViolatesRoleplayVoice("good xp here", "Phanty", true, out reason));
            Add(r, "autonomous RP rejects reroll",
                RoleplayPromptContract.ViolatesRoleplayVoice("might reroll after this", "Phanty", true, out reason));
            Add(r, "autonomous RP rejects DPS",
                RoleplayPromptContract.ViolatesRoleplayVoice("your dps is fine", "Phanty", true, out reason));
            Add(r, "autonomous RP rejects NPC",
                RoleplayPromptContract.ViolatesRoleplayVoice("that npc looked odd", "Phanty", true, out reason));
            Add(r, "autonomous RP rejects wiki",
                RoleplayPromptContract.ViolatesRoleplayVoice("the wiki says otherwise", "Phanty", true, out reason));

            // A direct mechanics question is an explicitly out-of-character turn; the answer needs the
            // real words. Only stage direction/self-narration stay blocked there.
            Add(r, "direct mechanics answer may use game terms",
                !RoleplayPromptContract.ViolatesRoleplayVoice("you get more xp from those", "Phanty", false, out reason));
            Add(r, "direct answer still rejects stage direction",
                RoleplayPromptContract.ViolatesRoleplayVoice("*shrugs* more xp there", "Phanty", false, out reason));

            // Ordinary in-world lines must survive both guards.
            Add(r, "plain RP line passes",
                !RoleplayPromptContract.ViolatesRoleplayVoice("I don't like the feel of this place.", "Phanty", true, out reason));
            Add(r, "curiosity line passes",
                !RoleplayPromptContract.ViolatesRoleplayVoice("Wonder who built this.", "Phanty", true, out reason));
            // "sim" as a substring of an ordinary word must not trip the guard.
            Add(r, "substring match does not false-positive",
                !RoleplayPromptContract.ViolatesRoleplayVoice("This is similar to the last valley.", "Phanty", true, out reason));
            // Another Sim's name plus a verb is dialogue about someone, not self-narration.
            Add(r, "other-name narration is not self narration",
                !RoleplayPromptContract.ViolatesRoleplayVoice("Dancer smiles too much.", "Phanty", true, out reason));
        }

        // ---- mode matrix: all eight combinations -----------------------------------------------
        private static void ModeMatrixTests(List<string> r)
        {
            // MMO
            Add(r, "matrix MMO+Auto healthy => LLM",
                RoleplayExpressionRouter.Resolve(SocialPerspectiveMode.Mmo, SocialExpressionMode.Auto, true) == ExpressionBackend.Llm);
            Add(r, "matrix MMO+Auto down => MMO templates",
                RoleplayExpressionRouter.Resolve(SocialPerspectiveMode.Mmo, SocialExpressionMode.Auto, false) == ExpressionBackend.MmoTemplates);
            Add(r, "matrix MMO+LLM => LLM",
                RoleplayExpressionRouter.Resolve(SocialPerspectiveMode.Mmo, SocialExpressionMode.Llm, true) == ExpressionBackend.Llm);
            Add(r, "matrix MMO+Templates => MMO templates",
                RoleplayExpressionRouter.Resolve(SocialPerspectiveMode.Mmo, SocialExpressionMode.Templates, true) == ExpressionBackend.MmoTemplates);
            Add(r, "matrix MMO+Off => none",
                RoleplayExpressionRouter.Resolve(SocialPerspectiveMode.Mmo, SocialExpressionMode.Off, true) == ExpressionBackend.None);

            // Roleplay
            Add(r, "matrix RP+Auto healthy => LLM",
                RoleplayExpressionRouter.Resolve(SocialPerspectiveMode.Roleplay, SocialExpressionMode.Auto, true) == ExpressionBackend.Llm);
            Add(r, "matrix RP+Auto down => RP templates",
                RoleplayExpressionRouter.Resolve(SocialPerspectiveMode.Roleplay, SocialExpressionMode.Auto, false) == ExpressionBackend.RoleplayTemplates);
            Add(r, "matrix RP+LLM => LLM",
                RoleplayExpressionRouter.Resolve(SocialPerspectiveMode.Roleplay, SocialExpressionMode.Llm, true) == ExpressionBackend.Llm);
            Add(r, "matrix RP+Templates => RP templates",
                RoleplayExpressionRouter.Resolve(SocialPerspectiveMode.Roleplay, SocialExpressionMode.Templates, true) == ExpressionBackend.RoleplayTemplates);
            Add(r, "matrix RP+Off => none",
                RoleplayExpressionRouter.Resolve(SocialPerspectiveMode.Roleplay, SocialExpressionMode.Off, true) == ExpressionBackend.None);

            // Templates must be a first-class RP backend, identical whether reached explicitly or by
            // Auto degrading. This is the "no LLM" guarantee expressed as a routing fact.
            Add(r, "RP Templates and RP Auto-down resolve to the same backend",
                RoleplayExpressionRouter.Resolve(SocialPerspectiveMode.Roleplay, SocialExpressionMode.Templates, false) ==
                RoleplayExpressionRouter.Resolve(SocialPerspectiveMode.Roleplay, SocialExpressionMode.Auto, false));
        }

        // ---- router: real rendered lines, not just enums ---------------------------------------
        private static void RouterTests(List<string> r)
        {
            SimSnapshot sim = new SimSnapshot();
            sim.Name = "Phanty";
            sim.Patience = 50;

            // Perspective=Roleplay + no LLM must yield an actual in-world line with no MMO vocabulary.
            SocialPerspectiveState.Current = SocialPerspectiveMode.Roleplay;
            string rpLine;
            bool rendered = RoleplayExpressionRouter.TryRenderAmbientSeed("rp_place", null, 7, sim, out rpLine);
            Add(r, "no-LLM RP router renders a line", rendered && rpLine.Length > 0);
            string reason;
            Add(r, "no-LLM RP line has no MMO meta language",
                rendered && !RoleplayPromptContract.ViolatesRoleplayVoice(rpLine, sim.Name, true, out reason));
            Add(r, "no-LLM RP line is not an MMO template",
                rendered && rpLine.IndexOf("reroll", StringComparison.OrdinalIgnoreCase) < 0
                         && rpLine.IndexOf("grind", StringComparison.OrdinalIgnoreCase) < 0
                         && rpLine.IndexOf("lol", StringComparison.OrdinalIgnoreCase) < 0);

            // An MMO subject offered while in Roleplay must NOT fall through to the MMO pool.
            string leaked;
            Add(r, "RP router refuses MMO subject rather than falling through",
                !RoleplayExpressionRouter.TryRenderAmbientSeed("zone_preference", null, 7, sim, out leaked));

            // RP event vocabulary, not MMO shorthand.
            string evt;
            bool evtOk = RoleplayExpressionRouter.TryRenderEvent("duel_completed", sim, 3, out evt);
            Add(r, "RP event line renders", evtOk && evt.Length > 0);
            Add(r, "RP event line is not MMO shorthand", evtOk && evt.IndexOf("grats", StringComparison.OrdinalIgnoreCase) < 0);

            // MMO perspective must still reach the untouched MMO templates.
            SocialPerspectiveState.Current = SocialPerspectiveMode.Mmo;
            string mmoLine;
            bool mmoOk = RoleplayExpressionRouter.TryRenderAmbientSeed("zone_preference", null, 7, sim, out mmoLine);
            string direct;
            SocialTemplates.TryRenderAmbientSeed("zone_preference", null, 7, sim, out direct);
            Add(r, "MMO templates unchanged through the router", mmoOk && mmoLine == direct && mmoLine.Length > 0);
            Add(r, "MMO perspective does not render RP subjects",
                !RoleplayExpressionRouter.TryRenderAmbientSeed("rp_place", null, 7, sim, out mmoLine));

            // Guard is enforced by the router, not merely available.
            SocialPerspectiveState.Current = SocialPerspectiveMode.Roleplay;
            Add(r, "router guard rejects a leaked line", !RoleplayExpressionRouter.PassesAutonomousGuard("that npc gave good xp", "Phanty"));
            Add(r, "router guard rejects stage direction", !RoleplayExpressionRouter.PassesAutonomousGuard("*looks around*", "Phanty"));
            Add(r, "router guard accepts a clean RP line", RoleplayExpressionRouter.PassesAutonomousGuard("Quiet here.", "Phanty"));

            // One deterministic rescue for a bad autonomous LLM line, then silence -- never a retry loop.
            string salvaged;
            Add(r, "bad LLM line is salvaged once by an RP template",
                RoleplayExpressionRouter.TrySalvageAutonomousLine("nice xp in this zone", "rp_place", 7, sim, out salvaged)
                && salvaged.Length > 0 && salvaged.IndexOf("xp", StringComparison.OrdinalIgnoreCase) < 0);
            Add(r, "good LLM line passes through unchanged",
                RoleplayExpressionRouter.TrySalvageAutonomousLine("Quiet here.", "rp_place", 7, sim, out salvaged) && salvaged == "Quiet here.");
            Add(r, "unsalvageable subject yields no message",
                !RoleplayExpressionRouter.TrySalvageAutonomousLine("nice xp", "memory:outing_1", 7, sim, out salvaged));
        }

        // ---- affinity / knowledge interpretation ------------------------------------------------
        private static void AffinityTests(List<string> r)
        {
            // Exposure is proven by movement away from the default, not by the faction existing.
            Add(r, "value == default is not exposure", !RoleplayAffinity.IsExposed(100f, 100f));
            Add(r, "value != default is exposure", RoleplayAffinity.IsExposed(120f, 100f));
            Add(r, "unexposed faction is Unknown",
                RoleplayAffinity.AttitudeFor(true, 100f, 100f) == RoleplayFactionAttitude.Unknown);
            Add(r, "unknown faction is Unknown",
                RoleplayAffinity.AttitudeFor(false, 500f, 100f) == RoleplayFactionAttitude.Unknown);
            Add(r, "negative movement is Wary",
                RoleplayAffinity.AttitudeFor(true, 60f, 100f) == RoleplayFactionAttitude.Wary);
            Add(r, "positive movement is Sympathetic",
                RoleplayAffinity.AttitudeFor(true, 140f, 100f) == RoleplayFactionAttitude.Sympathetic);
            // Loyal is deliberately unreachable in V1: nothing distinguishes sustained history from
            // one large turn-in.
            Add(r, "large positive movement still caps at Sympathetic",
                RoleplayAffinity.AttitudeFor(true, 9999f, 100f) == RoleplayFactionAttitude.Sympathetic);
            Add(r, "no attitude ever claims membership",
                !RoleplayAffinity.ClaimsMembership(RoleplayFactionAttitude.Sympathetic) &&
                !RoleplayAffinity.ClaimsMembership(RoleplayFactionAttitude.Loyal));
            // Class may carry a cultural affinity, but it must never manufacture affiliation or
            // religion. Membership stays impossible from class alone (see CulturalAffinityTests).
            Add(r, "class affinity never grants membership",
                !RoleplayAffinity.AffinityClaimsMembership("Paladin") &&
                !RoleplayAffinity.AffinityClaimsMembership("Arcanist") &&
                !RoleplayAffinity.AffinityClaimsMembership("Druid") &&
                !RoleplayAffinity.AffinityClaimsMembership("Windblade"));

            // Faction subjects require verified exposure and never appear without it.
            SocialPerspectiveState.Current = SocialPerspectiveMode.Roleplay;
            RoleplayFactionContext.Clear();
            string m;
            Add(r, "faction subject refuses without verified exposure",
                !RoleplayTemplates.TryRenderAmbient("rp_faction_opinion", null, 1, "Phanty", 50, false, false, out m));
            Add(r, "Unknown attitude does not count as exposure",
                !SetAndCheck("Azure Guard", RoleplayFactionAttitude.Unknown));

            RoleplayFactionContext.Set("Azure Guard", RoleplayFactionAttitude.Wary);
            Add(r, "verified exposure enables a faction subject",
                RoleplayTemplates.TryRenderAmbient("rp_faction_opinion", null, 1, "Phanty", 50, false, false, out m) && m.Length > 0);
            // The stance must not assert membership, motive, or history.
            Add(r, "faction line asserts no membership",
                m.IndexOf("member", StringComparison.OrdinalIgnoreCase) < 0 &&
                m.IndexOf("joined", StringComparison.OrdinalIgnoreCase) < 0 &&
                m.IndexOf("my order", StringComparison.OrdinalIgnoreCase) < 0);
            Add(r, "faction line invents no history",
                m.IndexOf("years", StringComparison.OrdinalIgnoreCase) < 0 &&
                m.IndexOf("since", StringComparison.OrdinalIgnoreCase) < 0 &&
                m.IndexOf("used to", StringComparison.OrdinalIgnoreCase) < 0);
            RoleplayFactionContext.Clear();
        }

        // ---- generated-output boundary (the function DeepSimsPlugin calls after grounding) -------
        private static void LlmBoundaryTests(List<string> r)
        {
            SimSnapshot sim = new SimSnapshot();
            sim.Name = "Phanty";
            sim.Patience = 50;

            // MMO perspective must pass generated output through byte-identically.
            string mmoIn = "good xp here, might reroll lol";
            Add(r, "MMO LLM output passes through unchanged",
                RoleplayExpressionRouter.GuardGeneratedAutonomousLine(mmoIn, "zone_preference", 4, sim, false) == mmoIn);

            // Roleplay: a clean line survives untouched.
            Add(r, "clean RP LLM line survives the boundary",
                RoleplayExpressionRouter.GuardGeneratedAutonomousLine("Quiet here.", "rp_place", 4, sim, true) == "Quiet here.");

            // Roleplay: a leaking line is salvaged exactly once to an RP template on the same subject.
            string salvaged = RoleplayExpressionRouter.GuardGeneratedAutonomousLine("nice xp in this zone", "rp_place", 4, sim, true);
            Add(r, "leaking RP LLM line is salvaged to a template",
                salvaged != RoleplayExpressionRouter.NoMessage && salvaged.IndexOf("xp", StringComparison.OrdinalIgnoreCase) < 0);
            string reason;
            Add(r, "salvaged line itself passes the voice guard",
                !RoleplayPromptContract.ViolatesRoleplayVoice(salvaged, sim.Name, true, out reason));
            // Deterministic: the salvage is stable, so it cannot flicker between runs.
            Add(r, "salvage is deterministic",
                RoleplayExpressionRouter.GuardGeneratedAutonomousLine("nice xp in this zone", "rp_place", 4, sim, true) == salvaged);

            // Stage direction / self narration are caught at the same boundary.
            Add(r, "stage direction is rejected at the boundary",
                RoleplayExpressionRouter.GuardGeneratedAutonomousLine("*looks around* Quiet.", "rp_place", 4, sim, true) != "*looks around* Quiet.");
            Add(r, "self narration is rejected at the boundary",
                RoleplayExpressionRouter.GuardGeneratedAutonomousLine("Phanty smiles.", "rp_place", 4, sim, true) != "Phanty smiles.");

            // No safe template for the subject => silence, not a wrong-subject template.
            Add(r, "leaking line with no safe template becomes NO_MESSAGE",
                RoleplayExpressionRouter.GuardGeneratedAutonomousLine("nice xp", "memory:outing_1", 4, sim, true) == RoleplayExpressionRouter.NoMessage);
            Add(r, "leaking line on an MMO subject becomes NO_MESSAGE in RP",
                RoleplayExpressionRouter.GuardGeneratedAutonomousLine("reroll time", "zone_preference", 4, sim, true) == RoleplayExpressionRouter.NoMessage);

            // An existing NO_MESSAGE is left alone rather than being salvaged into speech.
            Add(r, "NO_MESSAGE is not turned into speech",
                RoleplayExpressionRouter.GuardGeneratedAutonomousLine("NO_MESSAGE", "rp_place", 4, sim, true) == "NO_MESSAGE");
        }

        // ---- Sim-to-Sim thread rules -----------------------------------------------------------
        private static void ThreadRuleTests(List<string> r)
        {
            SimSnapshot sim = new SimSnapshot();
            sim.Name = "Phanty";
            sim.ClassName = "Druid";
            List<ConversationLine> thread = new List<ConversationLine>();
            thread.Add(new ConversationLine("Dancer", "Quiet here."));

            // Roleplay continuation prompt must not carry the MMO-player thread contract.
            SocialPerspectiveState.Current = SocialPerspectiveMode.Roleplay;
            string rp = Flatten(PromptBuilder.BuildPartyThreadReply(sim, new SimMemory(), new WorldSnapshot(), thread, 1, null));
            string rpLower = rp.ToLowerInvariant();
            Add(r, "RP thread prompt has no 'one MMO player' rule", rpLower.IndexOf("one mmo player", StringComparison.Ordinal) < 0);
            Add(r, "RP thread prompt does not say party chat framing", rpLower.IndexOf("mmo player replying", StringComparison.Ordinal) < 0);
            Add(r, "RP thread prompt frames speaker as a companion", rpLower.IndexOf("companions travelling together", StringComparison.Ordinal) >= 0);
            // Behaviour parity with the MMO contract.
            Add(r, "RP thread keeps NO_MESSAGE", rp.IndexOf("NO_MESSAGE", StringComparison.Ordinal) >= 0);
            Add(r, "RP thread keeps newest-line focus", rpLower.IndexOf("most recent", StringComparison.Ordinal) >= 0);
            Add(r, "RP thread forbids invented history", rpLower.IndexOf("do not invent shared history", StringComparison.Ordinal) >= 0);
            Add(r, "RP thread forbids narration", rpLower.IndexOf("no stage directions", StringComparison.Ordinal) >= 0);
            Add(r, "RP thread forbids archaic phrasing", rpLower.IndexOf("archaic", StringComparison.Ordinal) >= 0);
            Add(r, "RP thread keeps replies short", rpLower.IndexOf("one short reply", StringComparison.Ordinal) >= 0);

            // MMO continuation must retain its existing framing.
            SocialPerspectiveState.Current = SocialPerspectiveMode.Mmo;
            string mmo = Flatten(PromptBuilder.BuildPartyThreadReply(sim, new SimMemory(), new WorldSnapshot(), thread, 1, null));
            Add(r, "MMO thread prompt retains 'one MMO player'", mmo.IndexOf("one MMO player", StringComparison.Ordinal) >= 0);
            Add(r, "MMO thread prompt still forbids invented history",
                mmo.ToLowerInvariant().IndexOf("do not invent shared history", StringComparison.Ordinal) >= 0);
        }

        private static string Flatten(List<ChatMessage> messages)
        {
            if (messages == null) return string.Empty;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < messages.Count; i++)
                if (messages[i] != null && messages[i].content != null) sb.AppendLine(messages[i].content);
            return sb.ToString();
        }

        // ---- class cultural affinity ------------------------------------------------------------
        private static void CulturalAffinityTests(List<string> r)
        {
            Add(r, "Arcanist cultural affinity is Brax",
                RoleplayAffinity.CulturalAffinityFor("Arcanist") == RoleplayAffinity.AffinityBrax);
            Add(r, "Druid cultural affinity is Fernalla",
                RoleplayAffinity.CulturalAffinityFor("Druid") == RoleplayAffinity.AffinityFernalla);
            Add(r, "Paladin cultural affinity is Soluna",
                RoleplayAffinity.CulturalAffinityFor("Paladin") == RoleplayAffinity.AffinitySoluna);
            Add(r, "Windblade cultural affinity is Vitheo",
                RoleplayAffinity.CulturalAffinityFor("Windblade") == RoleplayAffinity.AffinityVitheo);
            Add(r, "legacy Duelist maps to the Windblade affinity",
                RoleplayAffinity.CulturalAffinityFor("Duelist") == RoleplayAffinity.AffinityVitheo);
            Add(r, "Windblade affinity is marked weaker", RoleplayAffinity.IsWeakAffinity("Windblade"));
            Add(r, "Paladin affinity is not marked weak", !RoleplayAffinity.IsWeakAffinity("Paladin"));

            Add(r, "Reaver has no automatic affinity",
                RoleplayAffinity.CulturalAffinityFor("Reaver") == string.Empty && !RoleplayAffinity.HasCulturalAffinity("Reaver"));
            Add(r, "Stormcaller has no automatic affinity",
                RoleplayAffinity.CulturalAffinityFor("Stormcaller") == string.Empty && !RoleplayAffinity.HasCulturalAffinity("Stormcaller"));
            Add(r, "unknown class has no affinity", RoleplayAffinity.CulturalAffinityFor("Bard") == string.Empty);

            // Affinity may never become affiliation.
            Add(r, "no class affinity claims membership",
                !RoleplayAffinity.AffinityClaimsMembership("Paladin") &&
                !RoleplayAffinity.AffinityClaimsMembership("Arcanist") &&
                !RoleplayAffinity.AffinityClaimsMembership("Druid") &&
                !RoleplayAffinity.AffinityClaimsMembership("Windblade"));

            // Authored interest lines must not assert membership, office, upbringing, or family.
            string line;
            bool anyMembershipClaim = false;
            string[] classes = new string[] { "Arcanist", "Druid", "Paladin", "Windblade" };
            for (int i = 0; i < classes.Length; i++)
            {
                for (long op = 0; op < 6; op++)
                {
                    if (!RoleplayAffinity.TryRenderCulturalInterest(classes[i], "Phanty", op, out line)) continue;
                    string l = line.ToLowerInvariant();
                    if (l.IndexOf("torchbearer", StringComparison.Ordinal) >= 0 ||
                        l.IndexOf("brotherhood", StringComparison.Ordinal) >= 0 ||
                        l.IndexOf("children of", StringComparison.Ordinal) >= 0 ||
                        l.IndexOf("blade of", StringComparison.Ordinal) >= 0 ||
                        l.IndexOf("i serve", StringComparison.Ordinal) >= 0 ||
                        l.IndexOf("i'm one of", StringComparison.Ordinal) >= 0 ||
                        l.IndexOf("grew up", StringComparison.Ordinal) >= 0 ||
                        l.IndexOf("my family", StringComparison.Ordinal) >= 0 ||
                        l.IndexOf("worship", StringComparison.Ordinal) >= 0) anyMembershipClaim = true;
                }
            }
            Add(r, "cultural interest lines never claim membership or upbringing", !anyMembershipClaim);
            Add(r, "unaffiliated class renders no cultural interest",
                !RoleplayAffinity.TryRenderCulturalInterest("Reaver", "Phanty", 1, out line));

            // Affinity and live standing are independent axes and are allowed to disagree.
            RoleplayFactionAttitude wary = RoleplayAffinity.AttitudeFor(true, 40f, 100f);
            Add(r, "Paladin can be culturally Solunarian yet currently wary",
                RoleplayAffinity.CulturalAffinityFor("Paladin") == RoleplayAffinity.AffinitySoluna &&
                wary == RoleplayFactionAttitude.Wary);
            Add(r, "affinity does not force faction attitude",
                RoleplayAffinity.AttitudeFor(true, 40f, 100f) != RoleplayFactionAttitude.Sympathetic);
        }

        // ---- rp_class_interest subject + spoken-style filter ------------------------------------
        private static void ClassInterestTests(List<string> r)
        {
            SocialPerspectiveState.Current = SocialPerspectiveMode.Roleplay;
            string m;

            // Each affinity class can actually render the subject through the runtime router.
            Add(r, "Arcanist renders rp_class_interest", RenderClassInterest("Arcanist", out m) && m.Length > 0);
            Add(r, "Druid renders rp_class_interest", RenderClassInterest("Druid", out m) && m.Length > 0);
            Add(r, "Paladin renders rp_class_interest", RenderClassInterest("Paladin", out m) && m.Length > 0);
            Add(r, "Windblade renders rp_class_interest", RenderClassInterest("Windblade", out m) && m.Length > 0);

            // Unaffiliated classes cannot select it from class affinity.
            Add(r, "Reaver cannot render rp_class_interest", !RenderClassInterest("Reaver", out m));
            Add(r, "Stormcaller cannot render rp_class_interest", !RenderClassInterest("Stormcaller", out m));
            Add(r, "missing class cannot render rp_class_interest", !RenderClassInterest(null, out m));

            // Roleplay-only: MMO perspective must not render it at all.
            SocialPerspectiveState.Current = SocialPerspectiveMode.Mmo;
            Add(r, "rp_class_interest is Roleplay-only", !RenderClassInterest("Paladin", out m));
            // MMO topic table gains exactly the three fact-free social seeds; RP-only subjects
            // remain excluded and the pre-existing topics are preserved.
            Add(r, "MMO topic pool adds three bounded free-social subjects", AmbientTopics.Downtime.Length == 13);
            bool mmoHasRp = false;
            for (int i = 0; i < AmbientTopics.Downtime.Length; i++)
                if (AmbientTopics.Downtime[i].TopicKey.StartsWith("rp_", StringComparison.Ordinal)) mmoHasRp = true;
            Add(r, "MMO topic pool contains no RP subject", !mmoHasRp);

            SocialPerspectiveState.Current = SocialPerspectiveMode.Roleplay;
            // Ordinary candidate: it carries a normal cooldown group and a low weight, so it competes
            // through the existing fatigue/duplicate/silence machinery rather than a private cadence.
            Add(r, "class interest uses an ordinary cooldown group",
                !string.IsNullOrEmpty(AmbientTopics.RoleplayClassInterest.CooldownGroup));
            Add(r, "class interest is weighted below the RP conversation topics",
                AmbientTopics.RoleplayClassInterest.BaseScore < 30.0);

            // No membership from the rendered line.
            RenderClassInterest("Paladin", out m);
            string low = m.ToLowerInvariant();
            Add(r, "class interest line claims no membership",
                low.IndexOf("brotherhood", StringComparison.Ordinal) < 0 &&
                low.IndexOf("i serve", StringComparison.Ordinal) < 0 &&
                low.IndexOf("worship", StringComparison.Ordinal) < 0 &&
                low.IndexOf("my order", StringComparison.Ordinal) < 0);

            // No-LLM guarantee: Templates and Auto-with-Ollama-down both reach the same backend, and
            // the rendered line is produced without any model.
            Add(r, "class interest works with no LLM (Templates)",
                RoleplayExpressionRouter.Resolve(SocialPerspectiveMode.Roleplay, SocialExpressionMode.Templates, false) == ExpressionBackend.RoleplayTemplates
                && RenderClassInterest("Arcanist", out m) && m.Length > 0);
            Add(r, "class interest works with no LLM (Auto, Ollama down)",
                RoleplayExpressionRouter.Resolve(SocialPerspectiveMode.Roleplay, SocialExpressionMode.Auto, false) == ExpressionBackend.RoleplayTemplates);

            // Determinism and guard compliance.
            string a1, a2;
            RenderClassInterest("Druid", out a1);
            RenderClassInterest("Druid", out a2);
            Add(r, "class interest is deterministic", a1 == a2 && a1.Length > 0);
            string reason;
            Add(r, "class interest line passes the RP voice guard",
                !RoleplayPromptContract.ViolatesRoleplayVoice(a1, "Phanty", true, out reason));
        }

        private static bool RenderClassInterest(string className, out string message)
        {
            SimSnapshot sim = new SimSnapshot();
            sim.Name = "Phanty";
            sim.ClassName = className;
            sim.Patience = 50;
            return RoleplayExpressionRouter.TryRenderAmbientSeed("rp_class_interest", null, 5, sim, out message);
        }

        // ---- post-personalization spoken-style filter -------------------------------------------
        private static void SpokenStyleTests(List<string> r)
        {
            // PersonalizeString runs AFTER the RP guard and owns the game's emoticon/slang logic, so
            // the filter must catch texture that appears only in the styled text.
            Add(r, "styled text gaining lol reverts to the accepted line",
                RoleplayPromptContract.KeepSpokenStyle("quiet here lol", "Quiet here.") == "Quiet here.");
            Add(r, "styled text gaining :D reverts",
                RoleplayPromptContract.KeepSpokenStyle("Quiet here :D", "Quiet here.") == "Quiet here.");
            Add(r, "styled text gaining XD reverts",
                RoleplayPromptContract.KeepSpokenStyle("Quiet here XD", "Quiet here.") == "Quiet here.");
            Add(r, "styled text gaining :P reverts",
                RoleplayPromptContract.KeepSpokenStyle("Quiet here :P", "Quiet here.") == "Quiet here.");
            Add(r, "styled text gaining o7 reverts",
                RoleplayPromptContract.KeepSpokenStyle("well fought o7", "Well fought.") == "Well fought.");

            // Harmless native traits survive: casing, typos, punctuation shape are all preserved.
            Add(r, "lowercasing quirk is preserved",
                RoleplayPromptContract.KeepSpokenStyle("quiet here.", "Quiet here.") == "quiet here.");
            Add(r, "typo quirk is preserved",
                RoleplayPromptContract.KeepSpokenStyle("Queit here.", "Quiet here.") == "Queit here.");
            Add(r, "punctuation quirk is preserved",
                RoleplayPromptContract.KeepSpokenStyle("Quiet here!!", "Quiet here.") == "Quiet here!!");

            // Detector correctness.
            Add(r, "detects lol", RoleplayPromptContract.ContainsChatTexture("that was close lol"));
            Add(r, "detects emoticon", RoleplayPromptContract.ContainsChatTexture("nice :)"));
            Add(r, "clean spoken line has no texture", !RoleplayPromptContract.ContainsChatTexture("I don't like this place."));
            // Must not false-positive on ordinary words containing the letters.
            Add(r, "does not false-positive on 'colossal'", !RoleplayPromptContract.ContainsChatTexture("a colossal ruin"));
            Add(r, "does not false-positive on 'typo'", !RoleplayPromptContract.ContainsChatTexture("Wonder who built this."));

            // MMO perspective must never be filtered: an already-textured accepted line is left alone.
            Add(r, "pre-existing texture is not punished",
                RoleplayPromptContract.KeepSpokenStyle("ok lol", "ok lol") == "ok lol");
            Add(r, "empty styled text falls back to accepted",
                RoleplayPromptContract.KeepSpokenStyle("", "Quiet here.") == "Quiet here.");
        }

        // ---- affinity in the LLM prompt ----------------------------------------------------------
        private static void AffinityPromptTests(List<string> r)
        {
            SimSnapshot sim = new SimSnapshot();
            sim.Name = "Phanty";
            sim.ClassName = "Paladin";
            SocialPerspectiveState.Current = SocialPerspectiveMode.Roleplay;
            string rp = Flatten(PromptBuilder.BuildAutonomous(sim, new SimMemory(), new WorldSnapshot(), "quiet moment", null, null, false));
            string low = rp.ToLowerInvariant();

            Add(r, "RP prompt supplies cultural affinity", low.IndexOf("cultural affinity", StringComparison.Ordinal) >= 0);
            Add(r, "RP prompt names the tradition", low.IndexOf("soluna", StringComparison.Ordinal) >= 0);
            Add(r, "RP prompt marks affinity as interest only", low.IndexOf("not membership", StringComparison.Ordinal) >= 0);
            Add(r, "RP prompt forbids claiming an order", low.IndexOf("does not mean you belong", StringComparison.Ordinal) >= 0);
            Add(r, "RP prompt forbids worship claim", low.IndexOf("that you worship anyone", StringComparison.Ordinal) >= 0);
            Add(r, "RP prompt forbids invented upbringing", low.IndexOf("upbringing", StringComparison.Ordinal) >= 0);

            // Weak affinity is flagged as loose.
            sim.ClassName = "Windblade";
            string weak = Flatten(PromptBuilder.BuildAutonomous(sim, new SimMemory(), new WorldSnapshot(), "quiet moment", null, null, false)).ToLowerInvariant();
            Add(r, "weak affinity is marked loose", weak.IndexOf("loose association", StringComparison.Ordinal) >= 0);

            // Unaffiliated class gets no affinity line at all.
            sim.ClassName = "Reaver";
            string none = Flatten(PromptBuilder.BuildAutonomous(sim, new SimMemory(), new WorldSnapshot(), "quiet moment", null, null, false)).ToLowerInvariant();
            Add(r, "unaffiliated class gets no affinity line", none.IndexOf("cultural affinity", StringComparison.Ordinal) < 0);

            // MMO perspective must not receive the affinity line.
            SocialPerspectiveState.Current = SocialPerspectiveMode.Mmo;
            sim.ClassName = "Paladin";
            string mmo = Flatten(PromptBuilder.BuildAutonomous(sim, new SimMemory(), new WorldSnapshot(), "quiet moment", null, null, false)).ToLowerInvariant();
            Add(r, "MMO prompt has no cultural affinity line", mmo.IndexOf("cultural affinity", StringComparison.Ordinal) < 0);
        }

        // ---- direct-reply fallback (the group/whisper "no grounded reply survived" boundary) -----
        // Regression coverage for the bug where an addressed question that failed grounding twice
        // (very common for a subjective "what do you think about X" turn, since GroundingGuard
        // treats an uncertain-sounding answer as a rejected deflection) fell back to SocialTemplates'
        // MMO-flavored filler ("i don't know that one", "beats me on that one") regardless of
        // /dsroleplay, silently defeating the perspective toggle on exactly the turns most likely to
        // be live-tested.
        private static void DirectReplyFallbackTests(List<string> r)
        {
            SimSnapshot sim = new SimSnapshot();
            sim.Name = "Dancer";
            sim.ClassName = "Windblade";

            string rpUnknown = RoleplayFallback.RenderUnknownFact("heard any news?", sim);
            Add(r, "RP unknown-fact fallback is not empty", !string.IsNullOrWhiteSpace(rpUnknown));
            Add(r, "RP unknown-fact fallback avoids the MMO filler wording",
                rpUnknown.IndexOf("that one", StringComparison.OrdinalIgnoreCase) < 0);
            Add(r, "RP unknown-fact fallback carries no chat texture", !RoleplayPromptContract.ContainsChatTexture(rpUnknown));

            string subjective;
            bool got = RoleplayFallback.TryRenderSubjective("dancer what do you think about being a windblade?", sim, out subjective);
            Add(r, "RP subjective fallback always produces a line", got && !string.IsNullOrWhiteSpace(subjective));
            // Windblade maps to the Vitheo cultural affinity (RoleplayAffinity.CulturalAffinityFor),
            // so a question that reads as being about the speaker's own class should surface one of
            // its affinity lines rather than a generic MMO deflection.
            Add(r, "RP subjective fallback is not MMO-player phrasing",
                subjective.IndexOf("not sure on that", StringComparison.OrdinalIgnoreCase) < 0 &&
                subjective.IndexOf("beats me", StringComparison.OrdinalIgnoreCase) < 0);

            // A speaker with no cultural affinity still gets a fact-free, perspective-correct line
            // rather than silence or an MMO template.
            SimSnapshot plainSim = new SimSnapshot();
            plainSim.Name = "Phanty";
            plainSim.ClassName = "Reaver";
            string plainSubjective;
            bool gotPlain = RoleplayFallback.TryRenderSubjective("what do you think about all this?", plainSim, out plainSubjective);
            Add(r, "RP subjective fallback works without cultural affinity", gotPlain && !string.IsNullOrWhiteSpace(plainSubjective));

            SimMemory identityMemory = new SimMemory();
            identityMemory.Normalize();
            identityMemory.Name = "Dancer";
            identityMemory.AuthoredIdentity.ErenshorPersona = "an initiate of the Order of Dawn";
            identityMemory.AuthoredIdentity.CorePersonality = "patient, curious, and quietly competitive";
            string identityWho = RoleplayFallback.RenderIdentityFact("who are you?", sim, identityMemory);
            Add(r, "REQUIRED: Roleplay identity fallback uses authored Erenshor persona",
                identityWho.IndexOf("Order of Dawn", StringComparison.OrdinalIgnoreCase) >= 0 &&
                identityWho.IndexOf("Erenshor character", StringComparison.OrdinalIgnoreCase) < 0);
            string identityOrigin = RoleplayFallback.RenderIdentityFact("where are you from?", sim, identityMemory);
            Add(r, "REQUIRED: unknown origin fallback does not invent a birthplace",
                identityOrigin.IndexOf("where I came from", StringComparison.OrdinalIgnoreCase) >= 0 &&
                identityOrigin.IndexOf("Port Azure", StringComparison.OrdinalIgnoreCase) < 0);
            string identityLike = RoleplayFallback.RenderIdentityFact("what are you like?", sim, identityMemory);
            Add(r, "Roleplay personality fallback uses authored personality safely",
                identityLike.IndexOf("patient", StringComparison.OrdinalIgnoreCase) >= 0);

            // The ChatTexture detector (used by KeepSpokenStyle to strip newly-injected native
            // typing texture) must catch "heh"/"haha", the exact vanilla-personalization suffix
            // observed in the live-test regression, not just "lol"/"lmao".
            Add(r, "detects heh", RoleplayPromptContract.ContainsChatTexture("i don't know that one heh"));
            Add(r, "detects haha", RoleplayPromptContract.ContainsChatTexture("that's funny haha"));
            Add(r, "KeepSpokenStyle strips newly-added heh",
                RoleplayPromptContract.KeepSpokenStyle("I don't know that. heh", "I don't know that.") == "I don't know that.");

            // MMO perspective is untouched by this fix: SocialTemplates' fillers are unchanged.
            string mmoUnknown = SocialTemplates.RenderUnknownFactReply("heard any news?", sim);
            Add(r, "MMO unknown-fact fallback is unchanged", !string.IsNullOrWhiteSpace(mmoUnknown));
        }

        // ---- central RoleplayOutputGuard: exact live-log regression lines ------------------------
        // Every one of these was displayed to the player live with roleplayGuardApplied=False before
        // the central guard (RoleplayOutputGuard.Enforce, wired into QueueGroupMessage/whisper/final
        // display in DeepSimsPlugin) existed. None may survive unchanged now, whether by stripping
        // texture (chat abbreviations, emoticons) or by full rejection of un-fixable core content
        // (e.g. "online" is the claim itself, not decoration on it).
        private static void OutputGuardTests(List<string> r)
        {
            string[] liveRegressionLines = new string[]
            {
                "nice to see you online again",
                "are your eyes painted on or playing NES? lmao",
                "heh yooo Brinon! aloha",
                "lmao maybe we're just too quiet to hear our own footsteps? :D",
                "Hey pal, nice to see you online again! Hit me up if you wanna hang.",
                "It's quiet in Hidden... heh :D just peace for now lol"
            };
            for (int i = 0; i < liveRegressionLines.Length; i++)
            {
                bool changed, rejected;
                string result = RoleplayOutputGuard.Enforce(liveRegressionLines[i], "Dancer", out changed, out rejected);
                bool survivedUnchanged = string.Equals(result, liveRegressionLines[i], StringComparison.Ordinal);
                Add(r, "live regression line is not shown unchanged: \"" + liveRegressionLines[i] + "\"", !survivedUnchanged);
                Add(r, "live regression line reports ran/changed-or-rejected: \"" + liveRegressionLines[i] + "\"", changed || rejected);
            }

            // Core-content vocabulary (online/offline/server/NES and structural meta phrases) is unfixable by
            // stripping a single token, so it must fully reject rather than leave a mangled sentence.
            bool onlineChanged, onlineRejected;
            string onlineResult = RoleplayOutputGuard.Enforce("nice to see you online again", "Dancer", out onlineChanged, out onlineRejected);
            Add(r, "core out-of-world vocabulary is rejected, not partially stripped", onlineRejected && onlineResult == "NO_MESSAGE");

            // Plain typed-chat texture (lol/heh/:D) is stripped in place; the sentence survives.
            bool textureChanged, textureRejected;
            string textureResult = RoleplayOutputGuard.Enforce("Quiet here lol", "Dancer", out textureChanged, out textureRejected);
            Add(r, "plain chat texture is stripped, not rejected", !textureRejected && textureChanged && textureResult.IndexOf("lol", StringComparison.OrdinalIgnoreCase) < 0);

            // A clean line survives byte-identical with ran/changed both false.
            bool cleanChanged, cleanRejected;
            string cleanResult = RoleplayOutputGuard.Enforce("Stay sharp.", "Dancer", out cleanChanged, out cleanRejected);
            Add(r, "clean RP line survives the central guard untouched", cleanResult == "Stay sharp." && !cleanChanged && !cleanRejected);

            // Do not over-filter ordinary in-world senses of words that also have a software/MMO
            // meaning. Structural meta phrases are rejected, not these harmless uses.
            bool diceChanged, diceRejected;
            string dice = RoleplayOutputGuard.Enforce("A game of dice sounds fine.", "Dancer", out diceChanged, out diceRejected);
            Add(r, "ordinary in-world use of game is allowed", !diceRejected && dice == "A game of dice sounds fine.");
            bool trainingChanged, trainingRejected;
            string training = RoleplayOutputGuard.Enforce("That training session helped.", "Dancer", out trainingChanged, out trainingRejected);
            Add(r, "ordinary in-world use of session is allowed", !trainingRejected && training == "That training session helped.");
            bool musicianChanged, musicianRejected;
            string musician = RoleplayOutputGuard.Enforce("That lute player is good.", "Dancer", out musicianChanged, out musicianRejected);
            Add(r, "ordinary in-world use of player is allowed", !musicianRejected && musician == "That lute player is good.");

            bool steamChanged, steamRejected;
            string steam = RoleplayOutputGuard.Enforce("Steam rises from the forge.", "Dancer", out steamChanged, out steamRejected);
            Add(r, "ordinary in-world use of steam is allowed", !steamRejected && steam == "Steam rises from the forge.");

            bool discordChanged, discordRejected;
            string discord = RoleplayOutputGuard.Enforce("There is discord among them.", "Dancer", out discordChanged, out discordRejected);
            Add(r, "ordinary in-world use of discord is allowed", !discordRejected && discord == "There is discord among them.");

            bool platformChanged, platformRejected;
            RoleplayOutputGuard.Enforce("I saw that on Steam.", "Dancer", out platformChanged, out platformRejected);
            Add(r, "platform phrase on Steam is rejected", platformRejected);

            bool discordServerChanged, discordServerRejected;
            RoleplayOutputGuard.Enforce("Check the Discord server.", "Dancer", out discordServerChanged, out discordServerRejected);
            Add(r, "Discord server phrase is rejected", discordServerRejected);

            bool metalChanged, metalRejected;
            string metal = RoleplayOutputGuard.Enforce("Add metal to the pile.", "Dancer", out metalChanged, out metalRejected);
            Add(r, "phrase boundary does not reject add metal as add me", !metalRejected && metal == "Add metal to the pile.");

            bool gamekeeperChanged, gamekeeperRejected;
            string gamekeeper = RoleplayOutputGuard.Enforce("The gamekeeper knows the trail.", "Dancer", out gamekeeperChanged, out gamekeeperRejected);
            Add(r, "phrase boundary does not reject gamekeeper as the game", !gamekeeperRejected && gamekeeper == "The gamekeeper knows the trail.");

            bool ordinaryChatChanged, ordinaryChatRejected;
            string ordinaryChat = RoleplayOutputGuard.Enforce("We can chat later.", "Dancer", out ordinaryChatChanged, out ordinaryChatRejected);
            Add(r, "ordinary conversational use of chat is allowed", !ordinaryChatRejected && ordinaryChat == "We can chat later.");

            bool chatUiChanged, chatUiRejected;
            RoleplayOutputGuard.Enforce("My chat ate that.", "Dancer", out chatUiChanged, out chatUiRejected);
            Add(r, "chat UI failure framing is rejected", chatUiRejected);

            bool thisGameChanged, thisGameRejected;
            RoleplayOutputGuard.Enforce("This game has good combat.", "Dancer", out thisGameChanged, out thisGameRejected);
            Add(r, "structural meta phrase this game is rejected", thisGameRejected);
            Add(r, "Roleplay guard rejects structural the-game meta phrase",
                RoleplayOutputGuard.ContainsRejectableCore("The game has good combat."));

            string[] identityMetaLeaks = new string[]
            {
                "I am Cyndara, the adventurer this Erenshor character is.",
                "I'm an NPC.",
                "You're the player.",
                "In this game, I heal.",
                "Those are just game mechanics.",
                "The devs changed that.",
                "The UI shows it."
            };
            for (int i = 0; i < identityMetaLeaks.Length; i++)
            {
                bool metaChanged, metaRejected;
                string guarded = RoleplayOutputGuard.Enforce(identityMetaLeaks[i], "Cyndara", out metaChanged, out metaRejected);
                Add(r, "REQUIRED: Roleplay rejects meta identity leak: " + identityMetaLeaks[i], metaRejected && guarded == "NO_MESSAGE");
            }
            Add(r, "REQUIRED: MMO context does not apply the Roleplay meta guard",
                RoleplayExpressionRouter.GuardGeneratedAutonomousLine("In this game, I heal.", "idle", 77, new SimSnapshot { Name = "Cyndara" }, false) == "In this game, I heal.");

            // NO_MESSAGE is left alone, never turned into a rejection or new speech.
            bool noMsgChanged, noMsgRejected;
            string noMsgResult = RoleplayOutputGuard.Enforce("NO_MESSAGE", "Dancer", out noMsgChanged, out noMsgRejected);
            Add(r, "NO_MESSAGE passes through the central guard untouched", noMsgResult == "NO_MESSAGE" && !noMsgChanged && !noMsgRejected);

            // Third-person self-narration is caught by the central guard too (not only the older
            // autonomous-only ViolatesRoleplayVoice boundary).
            bool narrationChanged, narrationRejected;
            RoleplayOutputGuard.Enforce("Dancer smiles.", "Dancer", out narrationChanged, out narrationRejected);
            Add(r, "central guard rejects self-narration", narrationRejected);

            // ---- gaming-meta vocabulary regression (live test: "dancer what do you think of being a
            // windblade?" produced "...I'm totally fine sticking with my own playstyle" -- structurally
            // clean but "playstyle" is still a player-at-a-keyboard word an in-world adventurer would
            // never use about themselves). Required regression per the follow-up briefing:
            //   Roleplay: "I prefer fighting up close." => PASS (unaffected)
            //   Roleplay: "It fits my playstyle."       => REJECT
            //   MMO:      "It fits my playstyle."       => ALLOWED (perspective-gated, unaffected)
            bool closeChanged, closeRejected;
            string closeResult = RoleplayOutputGuard.Enforce("I prefer fighting up close.", "Dancer", out closeChanged, out closeRejected);
            Add(r, "REQUIRED: ordinary in-character combat preference is not flagged",
                !closeRejected && !closeChanged && closeResult == "I prefer fighting up close.");

            bool playstyleChanged, playstyleRejected;
            string playstyleResult = RoleplayOutputGuard.Enforce("It fits my playstyle.", "Dancer", out playstyleChanged, out playstyleRejected);
            Add(r, "REQUIRED: Roleplay rejects gaming-meta 'playstyle'", playstyleRejected && playstyleResult == "NO_MESSAGE");

            // MMO perspective must never run this guard at all. GuardGeneratedAutonomousLine's
            // roleplayActive flag is exactly the boundary every DeepSimsPlugin call site gates on
            // (SocialPerspectiveState.RoleplayActive), so exercising it here with roleplayActive=false
            // proves the guard is perspective-scoped rather than merely re-testing string matching.
            SimSnapshot metaSim = new SimSnapshot();
            metaSim.Name = "Dancer";
            Add(r, "REQUIRED: MMO perspective allows 'playstyle' through untouched",
                RoleplayExpressionRouter.GuardGeneratedAutonomousLine("It fits my playstyle.", "rp_place", 1, metaSim, false) == "It fits my playstyle.");

            // Related-family terms, same tier (unambiguous by themselves; rejected outright).
            bool minmaxChanged, minmaxRejected;
            RoleplayOutputGuard.Enforce("I like to minmax everything.", "Dancer", out minmaxChanged, out minmaxRejected);
            Add(r, "gaming-meta 'minmax' is rejected", minmaxRejected);
            bool metaBuildChanged, metaBuildRejected;
            RoleplayOutputGuard.Enforce("That's the meta build right now.", "Dancer", out metaBuildChanged, out metaBuildRejected);
            Add(r, "structural phrase 'meta build' is rejected", metaBuildRejected);
            bool dpsRotationChanged, dpsRotationRejected;
            RoleplayOutputGuard.Enforce("Just follow the dps rotation.", "Dancer", out dpsRotationChanged, out dpsRotationRejected);
            Add(r, "structural phrase 'dps rotation' is rejected", dpsRotationRejected);
            bool altChanged, altRejected;
            RoleplayOutputGuard.Enforce("I'm on my alt right now.", "Dancer", out altChanged, out altRejected);
            Add(r, "structural phrase 'on my alt' is rejected", altRejected);
            bool toonChanged, toonRejected;
            RoleplayOutputGuard.Enforce("My toon looks great today.", "Dancer", out toonChanged, out toonRejected);
            Add(r, "structural phrase 'my toon' is rejected", toonRejected);

            // Ambiguity safety net: the individual words that make up the family above have ordinary
            // in-world senses and must NOT be flagged on their own -- only the unambiguous structural
            // combinations or standalone gaming-only words (checked above) are.
            bool buildChanged, buildRejected;
            string buildResult = RoleplayOutputGuard.Enforce("I'll build a fire before dark.", "Dancer", out buildChanged, out buildRejected);
            Add(r, "ordinary in-world 'build' (construct) is allowed", !buildRejected && buildResult == "I'll build a fire before dark.");
            bool mainChanged, mainRejected;
            string mainResult = RoleplayOutputGuard.Enforce("The main road leads north from here.", "Dancer", out mainChanged, out mainRejected);
            Add(r, "ordinary in-world 'main' (adjective) is allowed", !mainRejected && mainResult == "The main road leads north from here.");
            bool rotationChanged, rotationRejected;
            string rotationResult = RoleplayOutputGuard.Enforce("The wheel's rotation slowed down.", "Dancer", out rotationChanged, out rotationRejected);
            Add(r, "ordinary physical 'rotation' is allowed", !rotationRejected && rotationResult == "The wheel's rotation slowed down.");
            bool specChanged, specRejected;
            string specResult = RoleplayOutputGuard.Enforce("Let me spec out the plan first.", "Dancer", out specChanged, out specRejected);
            Add(r, "ordinary in-world 'spec' (verb) is allowed", !specRejected && specResult == "Let me spec out the plan first.");

            // Prompt-level coverage: the identity block itself must teach the model to avoid this
            // vocabulary family, since the final guard is deliberately narrow and cannot catch every
            // paraphrase (e.g. "I'd rather fight my own way" leaking as "I'm a melee spec").
            string metaGuidance = RoleplayPromptContract.BuildIdentityBlock(SocialPerspectiveMode.Roleplay, "Phanty").ToLowerInvariant();
            Add(r, "RP identity block warns against gaming-meta vocabulary",
                metaGuidance.IndexOf("playstyle", StringComparison.Ordinal) >= 0 &&
                metaGuidance.IndexOf("'build'", StringComparison.Ordinal) >= 0 &&
                metaGuidance.IndexOf("'spec'", StringComparison.Ordinal) >= 0);
        }

        // ---- identity-aware routing: verified class fact vs. subjective opinion question ----------
        private static void IdentityRoutingTests(List<string> r)
        {
            // "what do you think about being a windblade?" is a subjective opinion question about the
            // speaker's own (potentially verified) identity, not an ungroundable factual claim. It must
            // classify as subjective so the caller-side routing (DeepSimsPlugin.GroundPartyLineAsync's
            // skipKnowledgeGroundingForSubjectiveOpinion) can exempt it from wiki-relationship grounding.
            PartyReplyIntent classified = PartyReplyIntentClassifier.Classify("dancer what do you think about being a windblade?");
            Add(r, "'what do you think about being X' classifies as Opinion", classified == PartyReplyIntent.Opinion);
            Add(r, "'what do you think about being X' is subjective", PartyReplyIntentClassifier.IsSubjective(classified));
            Add(r, "'are you a Windblade' classifies as IdentityFact",
                PartyReplyIntentClassifier.Classify("dancer are you a windblade?") == PartyReplyIntent.IdentityFact);
            Add(r, "'what is a Windblade' remains factual",
                PartyReplyIntentClassifier.Classify("what is a windblade?") == PartyReplyIntent.FactualGameQuestion);

            // PromptBuilder's identity-vs-asked-class cross reference: when the wiki lookup target is a
            // known class name, the system prompt must state plainly whether the speaker's OWN verified
            // class matches it, rather than leaving the model to guess between falsely claiming
            // membership and falsely denying knowledge of its own class.
            SimSnapshot windbladeSim = new SimSnapshot();
            windbladeSim.Name = "Dancer";
            windbladeSim.ClassName = "Windblade";
            WikiResult windbladeWiki = new WikiResult { Query = "windblade", Title = "Windblade", Extract = "Windblades are agile melee duelists.", Found = true, SourceLabel = "Erenshor community wiki" };
            List<ConversationLine> thread = new List<ConversationLine>();
            thread.Add(new ConversationLine("Player", "dancer what do you think about being a windblade?"));
            string matchPrompt = Flatten(PromptBuilder.BuildPartyThreadReply(windbladeSim, new SimMemory(), new WorldSnapshot(), thread, 1, windbladeWiki));
            Add(r, "identity cross-reference fires for a known class lookup", matchPrompt.IndexOf("VERIFIED IDENTITY VS ASKED CLASS", StringComparison.Ordinal) >= 0);
            Add(r, "matching verified class tells the model it may answer as itself",
                matchPrompt.IndexOf("you may answer as yourself about being one", StringComparison.OrdinalIgnoreCase) >= 0);

            SimSnapshot druidSim = new SimSnapshot();
            druidSim.Name = "Dancer";
            druidSim.ClassName = "Druid";
            string mismatchPrompt = Flatten(PromptBuilder.BuildPartyThreadReply(druidSim, new SimMemory(), new WorldSnapshot(), thread, 1, windbladeWiki));
            Add(r, "mismatched verified class tells the model to correct the premise",
                mismatchPrompt.IndexOf("correct that premise naturally", StringComparison.OrdinalIgnoreCase) >= 0);
            Add(r, "mismatched verified class forbids claiming membership",
                mismatchPrompt.IndexOf("do not claim to be a windblade", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool SetAndCheck(string name, RoleplayFactionAttitude attitude)
        {
            RoleplayFactionContext.Set(name, attitude);
            bool has = RoleplayFactionContext.HasExposedFaction;
            RoleplayFactionContext.Clear();
            return has;
        }

        private static void Add(List<string> r, string name, bool ok)
        {
            r.Add("[DeepSims Roleplay] " + name + ": " + (ok ? "PASS" : "FAIL"));
        }
    }
}
