using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace ErenshorDeepSims
{
    // Deterministic, no-Ollama regression coverage for the fact/memory boundary. The same source is
    // compiled by tests/RUN_DETERMINISTIC_TESTS.ps1 with small Unity/framework stubs.
    internal static class DeterministicRegressionTests
    {
        private sealed class Clock
        {
            internal DateTime Now;
            internal Clock(DateTime now) { Now = now; }
            internal void Advance(double seconds) { Now = Now.AddSeconds(seconds); }
        }

        private static readonly List<string> Results = new List<string>();
        private static int _passed;
        private static int _failed;

        internal static List<string> Run()
        {
            Results.Clear();
            _passed = 0;
            _failed = 0;
            RunTrustTests();
            RunTemporalTests();
            RunPartyTests();
            RunEncounterTests();
            RunEventConversationTests();
            RunConversationSeedTests();
            RunOutputTests();
            AddRange(DialogueControlDeterministicTests.Run());
            AddRange(NemesisRoleContextDeterministicTests.Run());
            RunExternalNewsTests();
            RunExternalNewsProviderTests();
            RunPersistenceTests();
            Results.Add("[DeepSims Regression] SUMMARY: " + _passed + " PASS, " + _failed + " FAIL, " + (_passed + _failed) + " total.");
            return new List<string>(Results);
        }

        private static void AddRange(List<string> lines)
        {
            if (lines == null) return;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i] ?? string.Empty;
                Results.Add(line);
                if (line.IndexOf(" FAIL]", StringComparison.Ordinal) >= 0) _failed++; else _passed++;
            }
        }

        internal static int RunToConsole()
        {
            List<string> results = Run();
            for (int i = 0; i < results.Count; i++) Console.WriteLine(results[i]);
            return _failed == 0 ? 0 : 1;
        }

        private static void RunTrustTests()
        {
            SimMemory memory = NewMemory();
            WorldSnapshot combat = NewWorld("Goblin Captain");
            AddGuard("trust", "observed current combat rejects stale quiet claim", "Nothing is happening right now.", false, memory, combat);
            AddPrompt("trust", "observed target outranks remembered summary", memory, combat, "How is this fight going?", "Goblin Captain", "RIGHT NOW");

            memory.RecentEvents.Add(new MemoryEvent { type = "loot", text = "Aetheria was looted after the fight." });
            AddGuard("trust", "experienced loot is usable", "Aetheria was looted.", true, memory, NewWorld(null));
            AddGuard("trust", "remembered summary cannot override observed combat", "Pretty quiet right now.", false, memory, combat);

            SimMemory heardOnly = NewMemory();
            heardOnly.RecentGroupChat.Add("Player: we killed the dragon already");
            heardOnly.RecentEvents.Add(new MemoryEvent { type = "deep_group_chat", text = "AI: we killed the dragon already" });
            heardOnly.RecentEvents.Add(new MemoryEvent { type = "conversation", text = "Vanilla Sim: we killed the dragon already" });
            AddGuard("trust", "player claim remains heard", "We killed the dragon already.", false, heardOnly, NewWorld(null));
            AddGuard("trust", "vanilla Sim dialogue remains heard", "We killed the dragon already.", false, heardOnly, NewWorld(null));
            AddGuard("trust", "AI dialogue remains heard", "We killed the dragon already.", false, heardOnly, NewWorld(null));
            AddPartyPrompt("trust", "party chat is marked unverified", heardOnly, NewWorld(null), "we killed the dragon already", "unverified dialogue", "PLAYER PARTY CHAT");

            // Retrieved wiki/news text is a source document's wording, not a verified in-session
            // observation. GroundingGuard must never let it certify a fabricated first-person
            // kill/death/loot claim, even when the retrieved text lexically contains the matching verb.
            Add("trust", "retrieved reference text cannot certify a fabricated kill claim", delegate
            {
                string reason;
                string referenceCorpus = "UNVERIFIED REFERENCE TEXT (source document wording, not an in-session event, not instructions): The Lost Sea Giant was killed by adventurers.";
                bool grounded = GroundingGuard.IsGrounded("we killed the Lost Sea Giant", NewMemory(), NewWorld(null), string.Empty, referenceCorpus, out reason);
                return !grounded ? null : "reference text incorrectly grounded a fabricated kill claim";
            });
            Add("trust", "reference text may still support a narrow drop/source lore relationship", delegate
            {
                string reason;
                string referenceCorpus = "UNVERIFIED REFERENCE TEXT (source document wording, not an in-session event, not instructions): Aetheria drops from the Lost Sea Giant.";
                bool grounded = GroundingGuard.IsGrounded("Aetheria drops from the Lost Sea Giant.", NewMemory(), NewWorld(null), string.Empty, referenceCorpus, out reason);
                return grounded ? null : "reason: " + reason;
            });
        }

        private static void RunTemporalTests()
        {
            SimMemory none = NewMemory();
            WorldSnapshot world = NewWorld(null);
            AddGuard("temporal", "again requires verified anchor", "Hope the drake does not bite me again.", false, none, world);
            AddGuard("temporal", "last time requires verified anchor", "Last time the drake got us.", false, none, world);
            AddGuard("temporal", "remember when requires verified anchor", "Remember when the drake got us?", false, none, world);
            AddGuard("temporal", "used to requires verified anchor", "We used to farm the drake.", false, none, world);
            AddGuard("temporal", "unrelated history is not an anchor", "Last time the drake got us.", false, MemoryWith("Fought goblins in Brakke."), world);
            AddGuard("temporal", "matching history supports temporal reference", "We visited Azure Cove again.", true, MemoryWith("The party visited Azure Cove."), world);

            WorldSnapshot fights = NewWorld("Current Ogre");
            fights.Outing.CurrentEncounter = "Fight currently in progress against Current Ogre.";
            fights.Outing.LastEncounter = "Completed fight in Brakke: 1 kill, mainly Old Goblin.";
            AddPrompt("temporal", "current fight does not use previous fight", none, fights, "How is this fight going?", "Current Ogre", "RIGHT NOW");
            AddPrompt("temporal", "previous fight chooses last completed", none, fights, "How was the last fight?", "Old Goblin", "MOST RECENT COMPLETED");
        }

        private static void RunPartyTests()
        {
            SimMemory memory = NewMemory();
            WorldSnapshot party = NewWorld(null);
            party.Party.Add(new SimSnapshot { Key = "cyndara", Name = "Cyndara", ClassName = "Druid", CombatRole = "healer", GuildName = "Lantern" });
            AddGuard("party", "current class is authoritative", "Phanty is a Paladin.", false, memory, party);
            AddGuard("party", "current role capability is authoritative", "Cyndara is a plate tank.", false, memory, party);
            AddGuard("party", "current guild is authoritative", "Cyndara is in the Thunder guild.", false, memory, party);
            Add("party", "departed Sim excluded from current party", delegate
            {
                SimMemory departed = MemoryWith("Cyndara grouped in an older outing.");
                List<ChatMessage> messages = PromptBuilder.Build(NewSim(), departed, NewWorld(null), "Who is here?", 2, null);
                string prompt = messages[0].content;
                return prompt.IndexOf("Cyndara (", StringComparison.OrdinalIgnoreCase) < 0 ? null : "departed Sim appeared in live party";
            });

            SimMemory newJoiner = NewMemory();
            AddGuard("party", "newly joined Sim cannot claim earlier kill", "I killed the goblin earlier.", false, newJoiner, party);
            AddMemory("party", "duplicate participant keys do not multiply shared outings", TestDuplicateParticipants);
            AddMemory("party", "brief leave and rejoin does not create an outing", TestBriefRejoin);
            AddMemory("party", "conversation topics remain non-factual", TestConversationDoesNotCreateFact);
            AddMemory("party", "familiarity does not establish friendship history", TestFamiliarityDoesNotAnchorHistory);
            AddMemory("party", "relationship counters do not establish history", TestRelationshipDoesNotAnchorHistory);
            AddMemory("party", "rejoins do not increase relationship familiarity", TestJoinDoesNotIncreaseFamiliarity);
            AddMemory("party", "shared relationship time requires actual overlap", TestRelationshipOverlap);
        }

        private static void RunEncounterTests()
        {
            AddMemory("encounter", "attributed kill and loot are recorded", TestKillAndLoot);
            AddMemory("encounter", "local You-have-slain log attributes the kill", TestLocalPlayerKillLog);
            AddMemory("encounter", "unattributed kill is rejected", TestUnattributedKill);
            AddMemory("encounter", "death is attached to active encounter", TestEncounterDeath);
            AddMemory("encounter", "current target is exposed while fresh", TestCurrentTarget);
            AddMemory("encounter", "quiet period does not finalize too early", TestQuietPeriod);
            AddMemory("encounter", "previous fight is most recently completed", TestSequentialEncounters);
            AddMemory("encounter", "encounter retains start zone through zone transition", TestZoneTransition);
            AddMemory("encounter", "menu/loading cannot start a durable outing", TestMenuCannotStartOuting);
            AddMemory("encounter", "real zone is admitted only after gameplay readiness", TestGameplayReadinessBoundary);
            AddMemory("encounter", "multi-zone outing does not attribute whole duration to one zone", TestMultiZoneSummary);
            AddMemory("encounter", "legacy pseudo-location is removed without guessing a zone", TestLegacyOutingSanitization);
            AddMemory("encounter", "PvP semantic ownership suppresses generic close-call and kill proxies", TestCompetitiveCombatIsolation);
            AddMemory("encounter", "Practice Duel ownership suppresses generic lethal proxies", TestPracticeDuelIsolation);
            AddMemory("encounter", "ordinary native low-health state remains a close call", TestOrdinaryCloseCall);
            AddMemory("encounter", "party growth does not split active outing", TestPartyGrowth);
        }

        private static void RunEventConversationTests()
        {
            List<string> tests = EventConversationDirector.RunDeterministicSelfTests();
            for (int i = 0; i < tests.Count; i++)
            {
                string item = tests[i] ?? string.Empty;
                bool pass = item.EndsWith("PASS", StringComparison.Ordinal);
                if (pass) _passed++; else _failed++;
                Results.Add("[DeepSims Regression] events/" + item);
            }
        }

        private static void RunConversationSeedTests()
        {
            List<string> tests = ConversationSeedTests.Run();
            for (int i = 0; i < tests.Count; i++)
            {
                string item = tests[i] ?? string.Empty;
                bool pass = item.EndsWith("PASS", StringComparison.Ordinal);
                if (pass) _passed++; else _failed++;
                Results.Add("[DeepSims Regression] " + item);
            }
        }

        private static void RunOutputTests()
        {
            Add("output", "empty output is safe", delegate { return TextSanitizer.CleanReply(null, "Phanty", "Brinon", 80) == string.Empty ? null : "not empty"; });
            Add("output", "rich text is stripped", delegate { return TextSanitizer.CleanReply("<color=red>hello</color>", "Phanty", "Brinon", 80) == "hello" ? null : "markup survived"; });
            Add("output", "newlines are flattened", delegate { return TextSanitizer.CleanReply("hello\nthere", "Phanty", "Brinon", 80) == "hello there" ? null : "newline survived"; });
            Add("output", "expected speaker prefix is stripped", delegate { return TextSanitizer.CleanReply("Phanty: hi", "Phanty", "Brinon", 80) == "hi" ? null : "speaker prefix remained"; });
            Add("output", "wrong speaker prefix is retained for rejection", delegate { WorldSnapshot world = NewWorld(null); world.Party.Add(new SimSnapshot { Key = "cyndara", Name = "Cyndara", ClassName = "Druid" }); string reason; return !GroundingGuard.IsGrounded(TextSanitizer.CleanReply("Cyndara: hi", "Phanty", "Brinon", 80), NewMemory(), world, string.Empty, out reason) && reason.Contains("speaker prefix") ? null : "wrong speaker was not rejected"; });
            Add("output", "self class claim using like me is rejected", delegate { WorldSnapshot world = NewWorld(null); string reason; return !GroundingGuard.IsGrounded("what's it like for a Stormcaller like me?", NewMemory(), world, string.Empty, out reason) && reason.Contains("self class") ? null : "indirect wrong self class accepted: " + reason; });
            Add("output", "unrelated question cannot replace a direct player question", delegate { string reason; return !GroundingGuard.IsDirectReplyRelevant("could you imagine getting sucked into this game?", "what's the worst part of being a Reaver?", out reason) && reason.Contains("disconnected") ? null : "topic drift accepted"; });
            Add("output", "natural hypothetical follow-up remains relevant", delegate { string reason; return GroundingGuard.IsDirectReplyRelevant("could you imagine getting sucked into this game?", "what class would you be?", out reason) ? null : reason; });
            Add("output", "generic what-about-me deflection is rejected", delegate { string reason; return !GroundingGuard.IsDirectReplyRelevant("what is everyone's favorite music?", "what about me, then!", out reason) && reason.Contains("counter-question") ? null : "preference deflection accepted"; });
            Add("intent", "music preference fallback gives an actual taste", delegate { string reply; return SocialTemplates.TryRenderSubjectiveReply("what is everyone's favorite music?", NewSim(), PartyReplyIntent.Opinion, out reply) && (reply.IndexOf("instrumental", StringComparison.OrdinalIgnoreCase) >= 0 || reply.IndexOf("soundtrack", StringComparison.OrdinalIgnoreCase) >= 0 || reply.IndexOf("metal", StringComparison.OrdinalIgnoreCase) >= 0 || reply.IndexOf("loud", StringComparison.OrdinalIgnoreCase) >= 0) ? null : "generic music fallback: " + reply; });
            Add("intent", "casual uncertainty can answer a subjective question", delegate { return !GroundingGuard.IsSubjectiveDeflection(PartyReplyIntent.Opinion, "not too sure about that") ? null : "normal uncertainty rejected"; });
            Add("intent", "uncertainty remains valid for an unknown fact", delegate { return !GroundingGuard.IsSubjectiveDeflection(PartyReplyIntent.FactualGameQuestion, "not too sure about that") ? null : "factual uncertainty blocked"; });
            Add("intent", "unknown news fallback is specific and grounded", delegate { string reply = SocialTemplates.RenderUnknownFactReply("heard any news?", NewSim()); return GroundingGuard.HasUncertaintyLanguage(reply) && reply.IndexOf("that one", StringComparison.OrdinalIgnoreCase) < 0 ? null : "weak news fallback: " + reply; });
            Add("intent", "immersion hypothetical fallback takes a stance", delegate { string reply; return SocialTemplates.TryRenderSubjectiveReply("could you imagine getting sucked into this game?", NewSim(), PartyReplyIntent.SocialBanter, out reply) && !GroundingGuard.HasUncertaintyLanguage(reply) ? null : "immersion deflection: " + reply; });
            Add("output", "ordinary colon phrasing is not impersonation", delegate { string reason; return GroundingGuard.IsGrounded("Honestly: Paladins are sturdy.", NewMemory(), NewWorld(null), string.Empty, out reason) ? null : "ordinary label rejected: " + reason; });
            Add("output", "unknown colon label is not treated as verified speaker", delegate { string reason; return GroundingGuard.IsGrounded("Wanderer: hi", NewMemory(), NewWorld(null), string.Empty, out reason) ? null : "unknown label rejected: " + reason; });
            Add("output", "maximum reply length is bounded", delegate { string clean = TextSanitizer.CleanReply(new string('x', 100), "Phanty", "Brinon", 20); return clean.Length <= 23 && clean.EndsWith("...") ? null : "length=" + clean.Length; });
            Add("output", "player placeholder is resolved", delegate { return TextSanitizer.CleanReply("hey PLAYER", "Phanty", "Brinon", 80) == "hey Brinon" ? null : "placeholder remained"; });
            Add("output", "item placeholder token is resolved", delegate { return TextSanitizer.CleanReply("you should grab ITEM", "Phanty", "Brinon", 80) == "you should grab that item" ? null : "ITEM placeholder remained"; });
            Add("output", "leaked bare II template placeholder is resolved", delegate { return TextSanitizer.CleanReply("you should grab II", "Phanty", "Brinon", 80) == "you should grab that item" ? null : "leaked II placeholder remained"; });
            // Regression for the Artemis II bug: a literal Roman numeral proper noun must survive the
            // sanitizer unchanged, not be corrupted into "Artemis that item".
            Add("output", "Artemis II survives the sanitizer unchanged", delegate { return TextSanitizer.CleanReply("Artemis II was pretty interesting.", "Phanty", "Brinon", 80) == "Artemis II was pretty interesting." ? null : "Artemis II was corrupted: " + TextSanitizer.CleanReply("Artemis II was pretty interesting.", "Phanty", "Brinon", 80); });
            Add("output", "World War II survives the sanitizer unchanged", delegate { return TextSanitizer.CleanReply("World War II history is neat.", "Phanty", "Brinon", 80) == "World War II history is neat." ? null : "World War II was corrupted"; });
            Add("output", "emoji glyphs are removed", delegate { return TextSanitizer.CleanReply("nice 😀", "Phanty", "Brinon", 80).IndexOf("😀", StringComparison.Ordinal) < 0 ? null : "emoji survived"; });
            Add("output", "modern smile converts to ascii", delegate { string clean = TextSanitizer.CleanReply("nice \uD83D\uDE00", "Phanty", "Brinon", 80); return clean.IndexOf("\uD83D\uDE00", StringComparison.Ordinal) < 0 && clean.IndexOf(":D", StringComparison.Ordinal) >= 0 ? null : "smile was not safely converted: " + clean; });
            Add("output", "joined family emoji is removed", delegate { string clean = TextSanitizer.CleanReply("party \uD83D\uDC68\u200D\uD83D\uDC69\u200D\uD83D\uDC67\u200D\uD83D\uDC66 time", "Phanty", "Brinon", 80); return clean == "party time" ? null : "joined emoji residue: " + clean; });
            Add("output", "flag and skin tone emoji are removed", delegate { string clean = TextSanitizer.CleanReply("go \uD83C\uDDFA\uD83C\uDDF8 \uD83D\uDC4D\uD83C\uDFFD", "Phanty", "Brinon", 80); return clean.IndexOf("\uD83C", StringComparison.Ordinal) < 0 && clean.IndexOf("nice", StringComparison.Ordinal) >= 0 ? null : "flag/modifier residue: " + clean; });
            Add("output", "keycap combining mark is removed", delegate { string clean = TextSanitizer.CleanReply("press 1\uFE0F\u20E3", "Phanty", "Brinon", 80); return clean == "press 1" ? null : "keycap residue: " + clean; });
            Add("output", "ascii expressions remain supported", delegate { string clean = TextSanitizer.CleanReply("Hi!Brinon :P lol o7", "Dancer", "Brinon", 80); return clean == "Hi!Brinon :P lol o7" ? null : "ascii style changed: " + clean; });
            Add("output", "native dialogue template retains text-face spacing", delegate { string clean = PromptBuilder.ResolveDialogueTemplate("Hi!NN :P", "Brinon"); return clean == "Hi!Brinon :P" ? null : "template spacing changed: " + clean; });
            Add("output", "duplicate line is detected", delegate { return GroundingGuard.IsTooSimilar("that pull was clean", "that pull was clean") ? null : "duplicate accepted"; });
            Add("output", "overlong reply is deterministically shortened to a whole-sentence prefix, no LLM call", delegate
            {
                string shortened;
                bool ok = ReplyCompletenessGuard.TryDeterministicallyShorten(
                    "That pull was clean. We should keep pace like that all night if we can manage it.", 6, 200, out shortened);
                return ok && shortened == "That pull was clean." ? null : "shorten result=" + (ok ? shortened : "(failed)");
            });
            Add("output", "reply with no sentence fitting the budget has no safe deterministic shorten", delegate
            {
                string shortened;
                bool ok = ReplyCompletenessGuard.TryDeterministicallyShorten("this single unbroken thought just keeps going without a period anywhere in it at all", 4, 200, out shortened);
                return !ok ? null : "unexpectedly produced a shortened result: " + shortened;
            });
            AddGuard("output", "fabricated loot rejected", "We looted Moonblade.", false, NewMemory(), NewWorld(null));
            AddGuard("output", "unsupported history rejected", "We cleared this before.", false, NewMemory(), NewWorld(null));
            AddGuard("output", "speaker narration rejected", "Phanty says this is easy.", false, NewMemory(), NewWorld(null));
            AddGuard("output", "assistant sympathy phrasing rejected", "actually that's nice to hear you're having some quiet time", false, NewMemory(), NewWorld(null));
            AddGuard("output", "ordinary mmo reaction remains allowed", "quiet camp for once lol", true, NewMemory(), NewWorld(null));
            AddGuard("output", "unsupported yesterday qualifier rejected", "i'm still on cooldown from yesterday's practice duel", false, NewMemory(), NewWorld(null));
            AddGuard("output", "finished combat rejects phantom battle wait", "just waiting for the battle to finish so we can chat again", false, NewMemory(), NewWorld(null));
            Add("intent", "class seed rejects expedition drift", delegate { SocialIntent intent = new SocialIntent("seed", "class_role_preferences", 1, 1, "", "", "Phanty"); return !SocialIntentGuard.Matches(intent, "that expedition in Azure ended early") ? null : "expedition drift accepted"; });
            Add("intent", "class seed accepts healer preference", delegate { SocialIntent intent = new SocialIntent("seed", "class_role_preferences", 1, 1, "", "", "Phanty"); return SocialIntentGuard.Matches(intent, "I'd probably try healing") ? null : "class preference rejected"; });
            Add("intent", "outing seed rejects recent news drift", delegate { SocialIntent intent = new SocialIntent("seed", "verified_outing", 1, 1, "", "The party completed a fight against Molorai militia in Brake.", "Phanty"); return !SocialIntentGuard.Matches(intent, "did someone mention the news in Brake?") ? null : "news drift accepted"; });
            Add("intent", "outing seed accepts grounded subject", delegate { SocialIntent intent = new SocialIntent("seed", "verified_outing", 1, 1, "", "The party completed a fight against Molorai militia in Brake.", "Phanty"); return SocialIntentGuard.Matches(intent, "those militia were rough lol") ? null : "grounded outing line rejected"; });
            AddGuard("grounding", "hypothetical healing preference allowed", "I'd try healing if I rerolled", true, NewMemory(), NewWorld(null));
            AddGuard("grounding", "concrete earlier heal remains rejected", "I healed you earlier", false, NewMemory(), NewWorld(null));
            Add("intent", "camp versus dungeon is a preference", delegate { return PartyReplyIntentClassifier.Classify("phanty i still think camping one good spot is better than running a dungeon") == PartyReplyIntent.Preference ? null : "wrong intent"; });
            Add("intent", "reroll class question is hypothetical", delegate { return PartyReplyIntentClassifier.Classify("dancer if you rerolled what class would you try?") == PartyReplyIntent.Hypothetical ? null : "wrong intent"; });
            Add("intent", "what-drops query remains factual", delegate { return PartyReplyIntentClassifier.Classify("what drops Aetheria?") == PartyReplyIntent.FactualGameQuestion ? null : "factual query became social banter"; });
            Add("intent", "game-location query remains factual", delegate { return PartyReplyIntentClassifier.Classify("where is Krakengard?") == PartyReplyIntent.FactualGameQuestion ? null : "location query became social banter"; });
            Add("reasoning", "Selective stays off for casual banter", delegate { return !PromptBuilder.ShouldUseReasoning("Selective", new List<ChatMessage> { new ChatMessage("user", "lol nice pull") }) ? null : "casual banter enabled thinking"; });
            Add("reasoning", "Selective enables factual lookup", delegate { return PromptBuilder.ShouldUseReasoning("Selective", new List<ChatMessage> { new ChatMessage("system", "STRICT GAME-KNOWLEDGE MODE"), new ChatMessage("user", "what drops Aetheria?") }) ? null : "factual lookup did not enable thinking"; });
            Add("reasoning", "Selective enables grounding correction", delegate { return PromptBuilder.ShouldUseReasoning("Selective", new List<ChatMessage> { new ChatMessage("user", "Your previous draft was rejected. Rewrite the whole thought.") }) ? null : "correction did not enable thinking"; });
            Add("reasoning", "Off and Always are hard overrides", delegate { bool off = PromptBuilder.ShouldUseReasoning("Off", new List<ChatMessage> { new ChatMessage("user", "where is Krakengard?") }); bool always = PromptBuilder.ShouldUseReasoning("Always", new List<ChatMessage> { new ChatMessage("user", "gg") }); return !off && always ? null : "reasoning override failed"; });
            Add("intent", "explicit opinion remains subjective", delegate { return PartyReplyIntentClassifier.Classify("what do you think about Druids?") == PartyReplyIntent.Opinion ? null : "opinion was not recognized"; });
            Add("intent", "subjective fallback is opinionated", delegate { string reply; return SocialTemplates.TryRenderSubjectiveReply("phanty i still think camping one good spot is better than running a dungeon", NewSim(), PartyReplyIntent.Preference, out reply) && reply.IndexOf("not sure enough", StringComparison.OrdinalIgnoreCase) < 0 ? null : "no preference fallback"; });
            Add("intent", "favorite camp fallback gives a concrete preference", delegate { string reply; return SocialTemplates.TryRenderSubjectiveReply("what is your favorite place to camp?", NewSim(), PartyReplyIntent.Preference, out reply) && (reply.IndexOf("camp", StringComparison.OrdinalIgnoreCase) >= 0 || reply.IndexOf("respawn", StringComparison.OrdinalIgnoreCase) >= 0 || reply.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0) ? null : "generic camp fallback: " + reply; });
            Add("output", "truncated controller conjunction rejected", delegate { string reason; return ReplyCompletenessGuard.IsIncomplete("are you trying to play on a nes controller or", out reason) ? null : "malformed line accepted"; });
            Add("output", "raw paragraph is overlong before native styling", delegate { string reason; return ReplyCompletenessGuard.IsOverlong("i think tanking is definitely the best class because you can control every pull and keep the whole party safe while everyone else just follows along", 18, 180, out reason) ? null : "paragraph accepted"; });
            Add("output", "serious factual line gets no random emote", delegate { SimSnapshot sim = NewSim(); string line = "nasa announced a new mission"; return SocialTemplates.ApplyOccasionalMmoTexture(sim, line, true) == line ? null : "factual line was decorated"; });
            Add("output", "existing mmo texture is never stacked", delegate { SimSnapshot sim = NewSim(); return SocialTemplates.ApplyOccasionalMmoTexture(sim, "that was fun lol", true) == "that was fun lol" ? null : "texture stacked"; });
            Add("output", "unprofiled Sim may only gain rare universal lol", delegate { SimSnapshot sim = NewSim(); bool sawLol = false; for (int i = 0; i < 500; i++) { string raw = "cozy camp vibes " + i; string styled = SocialTemplates.ApplyOccasionalMmoTexture(sim, raw, false); if (styled == raw) continue; if (!styled.EndsWith(" lol", StringComparison.Ordinal)) return "shaped personality leaked: " + styled; sawLol = true; } return sawLol ? null : "rare lol never appeared"; });
            Add("output", "profiled Sim can use its observed expression", delegate { SimSnapshot sim = NewSim(); sim.DialogueExamples = new List<string> { "Hi!NN :P", "Hey!NN :P" }; for (int i = 0; i < 200; i++) { string raw = "cozy camp vibes " + i; string styled = SocialTemplates.ApplyOccasionalMmoTexture(sim, raw, true); if (styled != raw) return styled.EndsWith(":P", StringComparison.Ordinal) ? null : "unobserved marker: " + styled; } return "observed :P was never used"; });
            Add("output", "Dancer native greeting shape is retained", delegate { SimSnapshot sim = NewSim(); sim.Name = "Dancer"; sim.DialogueExamples = new List<string> { "Hi!NN :P", "Hey!NN :P", "Hello!NN :P" }; string styled = NativeDialogueStyle.ApplyGreetingShape(sim, "hi Brinon"); return styled.StartsWith("Hi!Brinon", StringComparison.Ordinal) ? null : "greeting flattened: " + styled; });
            Add("output", "Dancer fingerprint exposes only observed emote", delegate { SimSnapshot sim = NewSim(); sim.Name = "Dancer"; sim.DialogueExamples = new List<string> { "Hi!NN :P", "Hey!NN :P" }; string fingerprint = NativeDialogueStyle.Describe(sim); return fingerprint.IndexOf(":P", StringComparison.Ordinal) >= 0 && fingerprint.IndexOf(":D", StringComparison.Ordinal) < 0 ? null : "wrong fingerprint: " + fingerprint; });
            Add("social", "Lively scales ambient window below Normal", delegate { double lively = SocialPolicy.ScaleAmbientSeconds(SocialActivityPreset.Lively, 90); double normal = SocialPolicy.ScaleAmbientSeconds(SocialActivityPreset.Normal, 90); double quiet = SocialPolicy.ScaleAmbientSeconds(SocialActivityPreset.Quiet, 90); return lively < normal && normal < quiet && Math.Abs(lively - 49.5) < 0.01 ? null : "scaled=" + lively + "/" + normal + "/" + quiet; });
        }

        private static void RunExternalNewsTests()
        {
            // Routing: ordinary Erenshor gameplay/lore lines must never trigger a real-world search.
            Add("externalnews", "erenshor wiki question does not trigger external news", delegate
            {
                return !ExternalNewsQueryClassifier.ShouldLookup("where does Aetheria drop?") ? null : "false positive";
            });
            Add("externalnews", "erenshor patch question does not trigger external news", delegate
            {
                return !ExternalNewsQueryClassifier.ShouldLookup("what changed in the latest patch notes?") ? null : "false positive";
            });
            string[] shouldNotTrigger = new string[]
            {
                "do you like space?", "NASA is cool", "what class should I play?",
                "remember that fight?", "tell me about Krakengard",
                // A bare "news" substring is not real-world-news intent; these are ordinary
                // gameplay/lore lines that happen to contain the word.
                "good news everyone", "any news on Krakengard?"
                ,"what changed in the latest patch notes?",
                "anyone hear any news on the Bonepits quest?", "anyone hear any news on the new item?",
                "that NPC gave me some news", "what's new with my class?"
            };
            for (int i = 0; i < shouldNotTrigger.Length; i++)
            {
                string message = shouldNotTrigger[i];
                Add("externalnews", "ordinary conversation does not search: " + message, delegate
                {
                    return !ExternalNewsQueryClassifier.ShouldLookup(message) ? null : "false positive";
                });
            }
            string[] shouldTrigger = new string[]
            {
                "anything interesting happen in the news today?", "what's going on with NASA?",
                "any recent Star Citizen news?", "did anything happen with OpenAI today?",
                "what's happening in Ukraine?", "what's the latest news?",
                "anyone hear any news?", "heard any news?",
                "anyone hear any news on nasa?", "anyone hear any news about nasa?",
                "heard anything about nasa?"
            };
            for (int i = 0; i < shouldTrigger.Length; i++)
            {
                string message = shouldTrigger[i];
                Add("externalnews", "current external news question routes to ExternalNewsClient: " + message, delegate
                {
                    return ExternalNewsQueryClassifier.ShouldLookup(message) ? null : "did not trigger";
                });
            }
            Add("externalnews", "query extraction pulls the topic, not the whole sentence", delegate
            {
                string q = ExternalNewsQueryClassifier.ExtractQuery("what's going on with NASA?");
                return string.Equals(q.Trim(), "NASA", StringComparison.OrdinalIgnoreCase) ? null : "got '" + q + "'";
            });
            Add("externalnews", "empty topic falls back to a generic query", delegate
            {
                string q = ExternalNewsQueryClassifier.ExtractQuery("any news today?");
                return !string.IsNullOrWhiteSpace(q) ? null : "empty query";
            });
            Add("externalnews", "bare group news request uses top world news", delegate
            {
                string message = "anyone hear any news?";
                string q = ExternalNewsQueryClassifier.ExtractQuery(message);
                return ExternalNewsQueryClassifier.ShouldLookup(message) &&
                    !KnowledgeQueryClassifier.ShouldLookup(message) &&
                    string.Equals(q, "top world news", StringComparison.OrdinalIgnoreCase) ? null : "route/query=" + q;
            });

            // Grounding: retrieved headlines are real-world data, never Erenshor lore or personal history,
            // and cannot be embellished with unsupported historical claims.
            WikiResult found = new WikiResult { Query = "NASA", SourceLabel = "external real-world news search", Extract = "[Reuters - 2h ago] NASA delays Artemis launch", Found = true };
            WikiResult missing = new WikiResult { Query = "NASA", SourceLabel = "external real-world news search", Extract = string.Empty, Found = false };
            AddKnowledgeGuard("externalnews", "uncertainty is grounded when lookup failed", "Not sure, I didn't see anything on that.", true, missing);
            AddKnowledgeGuard("externalnews", "factual claim despite failed lookup is rejected", "NASA cancelled the whole program.", false, missing);
            AddKnowledgeGuard("externalnews", "casual reaction supported by headline is grounded", "Looks like NASA pushed the Artemis launch back.", true, found);
            AddKnowledgeGuard("externalnews", "unsupported repeated-history claim is rejected", "NASA delayed the launch again, typical.", false, found);
            AddKnowledgeGuard("externalnews", "meta reference to the lookup itself is rejected", "According to my wiki lookup, NASA delayed it.", false, found);
        }

        // Fake INewsTransport so external-news provider fallback/caching/timeout-budget behavior is
        // deterministic and never touches real internet access. Handler decides success/failure per
        // call based on the URL (Google News RSS vs GDELT) so tests can script each provider leg.
        private sealed class FakeNewsTransport : INewsTransport
        {
            internal int CallCount;
            internal readonly List<int> TimeoutsMs = new List<int>();
            internal readonly List<string> Urls = new List<string>();
            internal Func<string, int, string, string> Handler;

            public string Get(string url, int timeoutMs, string accept, string userAgent)
            {
                CallCount++;
                TimeoutsMs.Add(timeoutMs);
                Urls.Add(url);
                return Handler(url, timeoutMs, accept);
            }
        }

        private const string SampleRssXml =
            "<rss><channel><item><title>NASA delays Artemis launch</title>" +
            "<link>https://example.com/rss-article</link><source url=\"https://reuters.com\">Reuters</source>" +
            "<pubDate>Mon, 01 Jan 2024 12:00:00 GMT</pubDate></item></channel></rss>";

        private const string SampleGdeltJson =
            "{\"articles\":[{\"url\":\"https://example.com/gdelt-article\",\"title\":\"NASA delays Artemis launch\"," +
            "\"seendate\":\"20240101T120000Z\",\"domain\":\"reuters.com\"}]}";

        private static bool IsRss(string url) { return url.IndexOf("news.google.com", StringComparison.OrdinalIgnoreCase) >= 0; }

        private static ExternalNewsBundle RunSearch(ExternalNewsClient client, string query)
        {
            return client.SearchAsync(null, string.Empty, query, 4, 6, 900, 6).GetAwaiter().GetResult();
        }

        private static void RunExternalNewsProviderTests()
        {
            Add("externalnews-provider", "primary provider (Google RSS) succeeds", delegate
            {
                FakeNewsTransport transport = new FakeNewsTransport { Handler = (url, timeout, accept) => IsRss(url) ? SampleRssXml : SampleGdeltJson };
                ExternalNewsClient client = new ExternalNewsClient(null, transport);
                ExternalNewsBundle bundle = RunSearch(client, "NASA");
                if (!bundle.Combined.Found) return "expected found result";
                return transport.CallCount == 1 ? null : "expected exactly 1 provider call, got " + transport.CallCount;
            });

            Add("externalnews-provider", "primary returns zero results -> fallback succeeds", delegate
            {
                FakeNewsTransport transport = new FakeNewsTransport { Handler = (url, timeout, accept) => IsRss(url) ? "<rss><channel></channel></rss>" : SampleGdeltJson };
                ExternalNewsClient client = new ExternalNewsClient(null, transport);
                ExternalNewsBundle bundle = RunSearch(client, "NASA");
                if (!bundle.Combined.Found) return "expected fallback to find results";
                return transport.CallCount == 2 ? null : "expected 2 provider calls, got " + transport.CallCount;
            });

            Add("externalnews-provider", "primary times out -> fallback succeeds", delegate
            {
                FakeNewsTransport transport = new FakeNewsTransport { Handler = delegate(string url, int timeout, string accept)
                {
                    if (IsRss(url)) throw new TimeoutException("Request exceeded hard timeout of " + timeout + "ms.");
                    return SampleGdeltJson;
                } };
                ExternalNewsClient client = new ExternalNewsClient(null, transport);
                ExternalNewsBundle bundle = RunSearch(client, "NASA");
                if (!bundle.Combined.Found) return "expected fallback to find results after timeout";
                if (bundle.Diagnostics == null || bundle.Diagnostics.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) < 0)
                    return "diagnostics did not record the timeout: " + bundle.Diagnostics;
                return null;
            });

            Add("externalnews-provider", "primary returns HTTP 429 -> fallback succeeds", delegate
            {
                FakeNewsTransport transport = new FakeNewsTransport
                {
                    Handler = delegate(string url, int timeout, string accept)
                    {
                        if (IsRss(url)) throw new WebException("429 rate limited", null, WebExceptionStatus.ProtocolError, null);
                        return SampleGdeltJson;
                    }
                };
                ExternalNewsClient client = new ExternalNewsClient(null, transport);
                ExternalNewsBundle bundle = RunSearch(client, "NASA");
                return bundle.Combined.Found ? null : "expected fallback to find results after HTTP 429";
            });

            Add("externalnews-provider", "primary returns HTTP 5xx -> fallback succeeds", delegate
            {
                FakeNewsTransport transport = new FakeNewsTransport
                {
                    Handler = delegate(string url, int timeout, string accept)
                    {
                        if (IsRss(url)) throw new WebException("503 service unavailable", null, WebExceptionStatus.ProtocolError, null);
                        return SampleGdeltJson;
                    }
                };
                ExternalNewsClient client = new ExternalNewsClient(null, transport);
                ExternalNewsBundle bundle = RunSearch(client, "NASA");
                return bundle.Combined.Found ? null : "expected fallback to find results after HTTP 5xx";
            });

            Add("externalnews-provider", "primary returns malformed data -> fallback succeeds", delegate
            {
                FakeNewsTransport transport = new FakeNewsTransport { Handler = (url, timeout, accept) => IsRss(url) ? "not xml at all {{{" : SampleGdeltJson };
                ExternalNewsClient client = new ExternalNewsClient(null, transport);
                ExternalNewsBundle bundle = RunSearch(client, "NASA");
                return bundle.Combined.Found ? null : "expected fallback to find results after malformed primary response";
            });

            Add("externalnews-provider", "primary fails, fallback also fails -> clean no-results state", delegate
            {
                FakeNewsTransport transport = new FakeNewsTransport { Handler = delegate(string url, int timeout, string accept) { throw new WebException("down", null, WebExceptionStatus.ConnectFailure, null); } };
                ExternalNewsClient client = new ExternalNewsClient(null, transport);
                ExternalNewsBundle bundle = RunSearch(client, "both-fail");
                if (bundle.Combined.Found) return "expected no-results state";
                if (bundle.Combined.Extract != string.Empty) return "expected empty extract on total failure";
                return null;
            });

            Add("externalnews-provider", "RSS parses headline/publisher/date correctly", delegate
            {
                FakeNewsTransport transport = new FakeNewsTransport { Handler = (url, timeout, accept) => SampleRssXml };
                ExternalNewsClient client = new ExternalNewsClient(null, transport);
                ExternalNewsBundle bundle = RunSearch(client, "NASA");
                if (bundle.Items.Count != 1) return "expected 1 item, got " + bundle.Items.Count;
                ExternalNewsItem item = bundle.Items[0];
                if (item.Headline != "NASA delays Artemis launch") return "headline='" + item.Headline + "'";
                if (item.Publisher != "Reuters") return "publisher='" + item.Publisher + "'";
                if (!item.PublishedUtc.HasValue) return "publish date not parsed";
                return null;
            });

            Add("externalnews-provider", "GDELT parses headline/domain/date correctly", delegate
            {
                FakeNewsTransport transport = new FakeNewsTransport { Handler = (url, timeout, accept) => IsRss(url) ? "<rss><channel></channel></rss>" : SampleGdeltJson };
                ExternalNewsClient client = new ExternalNewsClient(null, transport);
                ExternalNewsBundle bundle = RunSearch(client, "NASA");
                if (bundle.Items.Count != 1) return "expected 1 item, got " + bundle.Items.Count;
                ExternalNewsItem item = bundle.Items[0];
                if (item.Headline != "NASA delays Artemis launch") return "headline='" + item.Headline + "'";
                if (item.Publisher != "reuters.com") return "publisher='" + item.Publisher + "'";
                if (!item.PublishedUtc.HasValue || item.PublishedUtc.Value.Year != 2024) return "publish date not parsed";
                return null;
            });

            Add("externalnews-provider", "negative cache suppresses immediate repeated failed lookup", delegate
            {
                FakeNewsTransport transport = new FakeNewsTransport { Handler = delegate(string url, int timeout, string accept) { throw new WebException("down", null, WebExceptionStatus.ConnectFailure, null); } };
                ExternalNewsClient client = new ExternalNewsClient(null, transport);
                RunSearch(client, "repeat-fail-query");
                int callsAfterFirst = transport.CallCount;
                ExternalNewsBundle second = RunSearch(client, "repeat-fail-query");
                if (transport.CallCount != callsAfterFirst) return "negative cache did not suppress the repeated lookup (calls went from " + callsAfterFirst + " to " + transport.CallCount + ")";
                if (second.Diagnostics == null || second.Diagnostics.IndexOf("negative-cache-hit", StringComparison.OrdinalIgnoreCase) < 0)
                    return "expected negative-cache-hit diagnostics, got: " + second.Diagnostics;
                return null;
            });

            Add("externalnews-provider", "successful cache still works", delegate
            {
                FakeNewsTransport transport = new FakeNewsTransport { Handler = (url, timeout, accept) => IsRss(url) ? SampleRssXml : SampleGdeltJson };
                ExternalNewsClient client = new ExternalNewsClient(null, transport);
                RunSearch(client, "repeat-success-query");
                int callsAfterFirst = transport.CallCount;
                RunSearch(client, "repeat-success-query");
                return transport.CallCount == callsAfterFirst ? null : "success cache did not suppress the repeated lookup";
            });

            Add("externalnews-provider", "total provider-chain timeout remains bounded", delegate
            {
                FakeNewsTransport transport = new FakeNewsTransport { Handler = delegate(string url, int timeout, string accept) { throw new TimeoutException("timed out after " + timeout + "ms"); } };
                ExternalNewsClient client = new ExternalNewsClient(null, transport);
                // 6-second overall budget: neither provider attempt should be handed the full
                // 6000ms independently - that would let a hung primary plus a hung fallback sum to
                // 12s, exactly the regression this test exists to catch.
                RunSearch(client, "bounded-budget-query");
                if (transport.TimeoutsMs.Count == 0) return "no provider attempted";
                foreach (int budget in transport.TimeoutsMs)
                    if (budget > 6000) return "a single provider attempt received a budget of " + budget + "ms, exceeding the 6000ms whole-lookup ceiling";
                return null;
            });
        }

        private static void AddKnowledgeGuard(string category, string name, string reply, bool expected, WikiResult externalFacts)
        {
            Add(category, name, delegate
            {
                string reason;
                bool actual = GroundingGuard.IsKnowledgeModeGrounded(reply, NewMemory(), NewWorld(null), externalFacts, out reason);
                return actual == expected ? null : (string.IsNullOrWhiteSpace(reason) ? "expected " + expected + ", got " + actual : reason);
            });
        }

        private static void RunPersistenceTests()
        {
            AddMemory("persistence", "missing memory file safely creates normalized memory", TestMissingMemory);
            AddMemory("persistence", "malformed memory file safely falls back", TestMalformedMemory);
            AddMemory("persistence", "optional fields from older file are normalized", TestOlderMemory);
            AddMemory("persistence", "legacy dialogue events are discarded on load", TestLegacyDialogueMigration);
            AddMemory("persistence", "flushed memory survives reload", TestSessionReload);
            AddMemory("persistence", "transient write failure is retried", TestTransientWriteRetry);
        }

        private static void AddGuard(string category, string name, string reply, bool expected, SimMemory memory, WorldSnapshot world)
        {
            Add(category, name, delegate
            {
                string reason;
                bool actual = GroundingGuard.IsGrounded(reply, memory, world, string.Empty, out reason);
                return actual == expected ? null : (string.IsNullOrWhiteSpace(reason) ? "expected " + expected + ", got " + actual : reason);
            });
        }

        private static void AddPrompt(string category, string name, SimMemory memory, WorldSnapshot world, string question, params string[] required)
        {
            Add(category, name, delegate
            {
                List<ChatMessage> messages = PromptBuilder.Build(NewSim(), memory, world, question, 2, null);
                string prompt = messages.Count == 0 ? string.Empty : messages[0].content;
                for (int i = 0; i < required.Length; i++)
                    if (prompt.IndexOf(required[i], StringComparison.OrdinalIgnoreCase) < 0) return "missing " + required[i];
                return null;
            });
        }

        private static void AddPartyPrompt(string category, string name, SimMemory memory, WorldSnapshot world, string question, params string[] required)
        {
            Add(category, name, delegate
            {
                List<ChatMessage> messages = PromptBuilder.BuildPartyReply(NewSim(), memory, world, question, 2, null);
                string combined = string.Empty;
                for (int i = 0; i < messages.Count; i++) combined += "\n" + messages[i].content;
                for (int i = 0; i < required.Length; i++)
                    if (combined.IndexOf(required[i], StringComparison.OrdinalIgnoreCase) < 0) return "missing " + required[i];
                return null;
            });
        }

        private static void AddMemory(string category, string name, Func<string> test) { Add(category, name, test); }
        private static void Add(string category, string name, Func<string> test)
        {
            try
            {
                string reason = test();
                if (reason == null) { _passed++; Results.Add("[DeepSims Regression] " + category + "/" + name + ": PASS"); }
                else { _failed++; Results.Add("[DeepSims Regression] " + category + "/" + name + ": FAIL (" + reason + ")"); }
            }
            catch (Exception ex)
            {
                _failed++;
                Results.Add("[DeepSims Regression] " + category + "/" + name + ": FAIL (" + ex.GetType().Name + ")");
            }
        }

        private static SimSnapshot NewSim()
        {
            return new SimSnapshot { Key = "phanty", Name = "Phanty", ClassName = "Arcanist", GuildName = "Lantern", Scene = "Brakke", Level = 12 };
        }

        private static SimMemory NewMemory()
        {
            SimMemory memory = new SimMemory { SimKey = "phanty", Name = "Phanty" };
            memory.Normalize();
            return memory;
        }

        private static SimMemory MemoryWith(string text)
        {
            SimMemory memory = NewMemory();
            memory.RecentEvents.Add(new MemoryEvent { type = "observed", text = text });
            return memory;
        }

        private static WorldSnapshot NewWorld(string target)
        {
            WorldSnapshot world = new WorldSnapshot();
            world.Scene = "Brakke";
            world.Player = new PlayerSnapshot { Name = "Brinon", HpPercent = 100f };
            world.Party = new List<SimSnapshot> { NewSim() };
            world.Outing = new OutingSnapshot { Active = true, Activity = string.IsNullOrWhiteSpace(target) ? "adventuring/downtime" : "combat/recent combat", CurrentCombatTarget = target, Facts = new List<string>(), RecentEncounters = new List<string>(), RecentCompletedEncounters = new List<EncounterSnapshot>() };
            return world;
        }

        private static SessionTelemetry NewTelemetry(Clock clock)
        {
            return new SessionTelemetry(null, null, delegate { return clock.Now; });
        }

        private static List<SimSnapshot> Party()
        {
            return new List<SimSnapshot> { NewSim() };
        }

        private static string TestKillAndLoot()
        {
            Clock clock = new Clock(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            SessionTelemetry telemetry = NewTelemetry(clock);
            telemetry.Observe(NewWorld(null), Party());
            telemetry.RecordKill("Goblin", "Phanty");
            telemetry.RecordLoot("Bronze Sword", 1);
            OutingSnapshot snap = telemetry.Snapshot();
            return snap.TotalKills == 1 && snap.TotalLootItems == 1 && Contains(snap.Facts, "Goblin") && Contains(snap.Facts, "Bronze Sword") ? null : "kill/loot missing";
        }

        private static string TestUnattributedKill()
        {
            Clock clock = new Clock(DateTime.UtcNow);
            SessionTelemetry telemetry = NewTelemetry(clock);
            telemetry.Observe(NewWorld(null), Party());
            telemetry.RecordKill("Goblin");
            return telemetry.Snapshot().TotalKills == 0 ? null : "unattributed kill counted";
        }

        private static string TestEncounterDeath()
        {
            Clock clock = new Clock(DateTime.UtcNow);
            SessionTelemetry telemetry = NewTelemetry(clock);
            telemetry.Observe(NewWorld(null), Party());
            telemetry.RecordKill("Goblin", "Phanty");
            telemetry.RecordObservedEvent("player_death", "The player died.");
            clock.Advance(15.1);
            EncounterSnapshot encounter = telemetry.Snapshot().LastCompletedEncounter;
            return encounter != null && encounter.Deaths == 1 ? null : "death missing";
        }

        private static string TestCurrentTarget()
        {
            Clock clock = new Clock(DateTime.UtcNow);
            SessionTelemetry telemetry = NewTelemetry(clock);
            telemetry.Observe(NewWorld(null), Party());
            telemetry.RecordKill("Goblin Captain", "Phanty");
            return telemetry.Snapshot().CurrentCombatTarget == "Goblin Captain" ? null : "target missing";
        }

        private static string TestQuietPeriod()
        {
            Clock clock = new Clock(DateTime.UtcNow);
            SessionTelemetry telemetry = NewTelemetry(clock);
            telemetry.Observe(NewWorld(null), Party());
            telemetry.RecordKill("Goblin", "Phanty");
            clock.Advance(15.0);
            if (telemetry.Snapshot().LastCompletedEncounter != null) return "finalized at boundary";
            clock.Advance(0.1);
            return telemetry.Snapshot().LastCompletedEncounter != null ? null : "did not finalize after quiet period";
        }

        private static string TestSequentialEncounters()
        {
            Clock clock = new Clock(DateTime.UtcNow);
            SessionTelemetry telemetry = NewTelemetry(clock);
            telemetry.Observe(NewWorld(null), Party());
            telemetry.RecordKill("Goblin", "Phanty");
            clock.Advance(16);
            telemetry.Snapshot();
            telemetry.RecordKill("Ogre", "Phanty");
            clock.Advance(16);
            OutingSnapshot snap = telemetry.Snapshot();
            return snap.RecentCompletedEncounters.Count == 2 && snap.LastCompletedEncounter.PrimaryEnemy == "Ogre" ? null : "latest completed encounter incorrect";
        }

        private static string TestZoneTransition()
        {
            Clock clock = new Clock(DateTime.UtcNow);
            SessionTelemetry telemetry = NewTelemetry(clock);
            WorldSnapshot world = NewWorld(null);
            telemetry.Observe(world, Party());
            telemetry.RecordKill("Goblin", "Phanty");
            clock.Advance(2);
            world.Scene = "Azure Cove";
            telemetry.Observe(world, Party());
            clock.Advance(16);
            EncounterSnapshot encounter = telemetry.Snapshot().LastCompletedEncounter;
            return encounter != null && encounter.Zone == "Brakke" ? null : "zone was not frozen at encounter start";
        }

        private static string TestMenuCannotStartOuting()
        {
            Clock clock = new Clock(DateTime.UtcNow);
            SessionTelemetry telemetry = NewTelemetry(clock);
            WorldSnapshot world = NewWorld(null);
            world.Scene = "Menu";
            telemetry.Observe(world, Party(), true);
            if (telemetry.Snapshot().Active) return "Menu started an outing";
            world.Scene = "Hidden Hills";
            telemetry.Observe(world, Party(), true);
            OutingSnapshot snap = telemetry.Snapshot();
            return snap.Active && Contains(snap.Facts, "Hidden Hills") && !Contains(snap.Facts, "Menu")
                ? null : "first verified gameplay location was not retained cleanly";
        }

        private static string TestMultiZoneSummary()
        {
            string dir = NewTempDirectory();
            try
            {
                Clock clock = new Clock(new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc));
                MemoryStore store = new MemoryStore(dir, null);
                SessionTelemetry telemetry = new SessionTelemetry(null, store, delegate { return clock.Now; });
                SimSnapshot sim = NewSim();
                List<SimSnapshot> party = new List<SimSnapshot> { sim };
                WorldSnapshot world = NewWorld(null);
                world.Scene = "Hidden Hills";
                telemetry.Observe(world, party, true);
                for (int i = 0; i < 7; i++) { clock.Advance(10); telemetry.Observe(world, party, true); }
                world.Scene = "Azure Cove";
                for (int i = 0; i < 7; i++) { clock.Advance(10); telemetry.Observe(world, party, true); }
                clock.Advance(1);
                telemetry.Observe(world, new List<SimSnapshot>(), true);
                SimMemory memory = store.LoadForPrompt(sim);
                store.Shutdown();
                if (memory.OutingSummaries.Count != 1) return "outing summary missing";
                string summary = memory.OutingSummaries[0];
                return summary.IndexOf("across multiple verified areas", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    summary.IndexOf("minutes in Hidden Hills", StringComparison.OrdinalIgnoreCase) < 0 &&
                    summary.IndexOf("minutes in Azure Cove", StringComparison.OrdinalIgnoreCase) < 0
                    ? null : "whole duration received a stale/final zone: " + summary;
            }
            finally { DeleteTempDirectory(dir); }
        }

        private static string TestGameplayReadinessBoundary()
        {
            Clock clock = new Clock(DateTime.UtcNow);
            SessionTelemetry telemetry = NewTelemetry(clock);
            WorldSnapshot world = NewWorld(null);
            world.Scene = "Hidden Hills";
            telemetry.Observe(world, Party(), false);
            if (telemetry.Snapshot().Active) return "real scene started before character readiness";
            telemetry.Observe(world, Party(), true);
            return telemetry.Snapshot().Active && telemetry.Snapshot().CurrentZone == "Hidden Hills"
                ? null : "ready real scene did not start outing";
        }

        private static string TestLegacyOutingSanitization()
        {
            string legacy = "Grouped for about 37 minutes in Menu. The group had 1 close call.";
            string safe = VerifiedOutingHistoryPolicy.SanitizeForPrompt(legacy);
            return safe == "Grouped for about 37 minutes. The group had 1 close call." &&
                safe.IndexOf("Hidden Hills", StringComparison.OrdinalIgnoreCase) < 0
                ? null : "legacy summary was not conservatively sanitized: " + safe;
        }

        private static string TestCompetitiveCombatIsolation()
        {
            Clock clock = new Clock(DateTime.UtcNow);
            SessionTelemetry telemetry = NewTelemetry(clock);
            WorldSnapshot world = NewWorld(null);
            telemetry.Observe(world, Party(), true);
            telemetry.ObserveCompetitiveCombatEvent("pvp", "pvp_accepted", string.Empty);
            if (!telemetry.ShouldSuppressGenericCombatEvent("player_death") ||
                telemetry.ShouldSuppressGenericCombatEvent("pvp_match_completed")) return "generic/semantic event classification is wrong";
            world.Player.HpPercent = 10f;
            clock.Advance(1);
            telemetry.Observe(world, Party(), true);
            telemetry.RecordKill("PvP Proxy", "Phanty");
            telemetry.RecordObservedEvent("player_death", "The player died.");
            OutingSnapshot snap = telemetry.Snapshot();
            if (snap.TotalKills != 0 || !string.Equals(snap.Mood, "normal", StringComparison.OrdinalIgnoreCase) ||
                Contains(snap.Facts, "close call") || Contains(snap.Facts, "died")) return "competitive proxies leaked into generic telemetry";
            telemetry.ObserveCompetitiveCombatEvent("pvp", "pvp_match_completed", string.Empty);
            clock.Advance(13);
            return !telemetry.ShouldSuppressGenericCombatEvent("player_death") ? null : "terminal suppression did not expire";
        }

        private static string TestPracticeDuelIsolation()
        {
            Clock clock = new Clock(DateTime.UtcNow);
            SessionTelemetry telemetry = NewTelemetry(clock);
            WorldSnapshot world = NewWorld(null);
            telemetry.Observe(world, Party(), true);
            telemetry.ObserveCompetitiveCombatEvent("duel", "duel_started", string.Empty);
            world.Player.HpPercent = 10f;
            clock.Advance(1);
            telemetry.Observe(world, Party(), true);
            telemetry.RecordKill("Practice Proxy", "Phanty");
            OutingSnapshot snap = telemetry.Snapshot();
            return snap.TotalKills == 0 && !Contains(snap.Facts, "close call") && !Contains(snap.Facts, "Practice Proxy")
                ? null : "Practice Duel leaked into generic lethal telemetry";
        }

        private static string TestOrdinaryCloseCall()
        {
            Clock clock = new Clock(DateTime.UtcNow);
            SessionTelemetry telemetry = NewTelemetry(clock);
            WorldSnapshot world = NewWorld(null);
            telemetry.Observe(world, Party(), true);
            world.Player.HpPercent = 10f;
            clock.Advance(1);
            telemetry.Observe(world, Party(), true);
            return Contains(telemetry.Snapshot().Facts, "low-health close call")
                ? null : "verified ordinary HP threshold no longer records a close call";
        }

        private static string TestPartyGrowth()
        {
            Clock clock = new Clock(DateTime.UtcNow);
            SessionTelemetry telemetry = NewTelemetry(clock);
            WorldSnapshot world = NewWorld(null);
            List<SimSnapshot> party = Party();
            telemetry.Observe(world, party);
            party.Add(new SimSnapshot { Key = "cyndara", Name = "Cyndara", ClassName = "Druid", Scene = "Brakke" });
            clock.Advance(1);
            telemetry.Observe(world, party);
            return telemetry.Snapshot().Active ? null : "party growth ended outing";
        }

        private static string TestDuplicateParticipants()
        {
            string dir = NewTempDirectory();
            try
            {
                MemoryStore store = new MemoryStore(dir, null);
                SimSnapshot phanty = NewSim();
                store.RecordSharedOuting(new List<SimSnapshot> { phanty, phanty }, 10);
                SimMemory memory = store.LoadForPrompt(phanty);
                store.FlushPending(true);
                return memory.SimRelationships.Count == 0 ? null : "duplicate key created a self relationship";
            }
            finally { DeleteTempDirectory(dir); }
        }

        private static string TestBriefRejoin()
        {
            string dir = NewTempDirectory();
            try
            {
                Clock clock = new Clock(DateTime.UtcNow);
                MemoryStore store = new MemoryStore(dir, null);
                SessionTelemetry telemetry = new SessionTelemetry(null, store, delegate { return clock.Now; });
                SimSnapshot phanty = NewSim();
                SimSnapshot cyndara = new SimSnapshot { Key = "cyndara", Name = "Cyndara", ClassName = "Druid", Scene = "Brakke" };
                List<SimSnapshot> party = new List<SimSnapshot> { phanty, cyndara };
                telemetry.Observe(NewWorld(null), party);
                clock.Advance(30);
                telemetry.Observe(NewWorld(null), party);
                party.RemoveAt(1);
                clock.Advance(30);
                telemetry.Observe(NewWorld(null), party);
                party.Add(cyndara);
                clock.Advance(30);
                telemetry.Observe(NewWorld(null), party);
                SimMemory memory = store.LoadForPrompt(phanty);
                store.FlushPending(true);
                return memory.SimRelationships.Count == 0 && memory.OutingSummaries.Count == 0 ? null : "brief reshuffle created persisted outing history";
            }
            finally { DeleteTempDirectory(dir); }
        }

        private static string TestConversationDoesNotCreateFact()
        {
            string dir = NewTempDirectory();
            try
            {
                MemoryStore store = new MemoryStore(dir, null);
                SimSnapshot phanty = NewSim();
                store.RecordConversationThread(new List<SimSnapshot> { phanty }, new List<ConversationLine> { new ConversationLine("Phanty", "we killed the dragon"), new ConversationLine("Brinon", "nice") }, "Brakke");
                SimMemory memory = store.LoadForPrompt(phanty);
                string reason;
                bool grounded = GroundingGuard.IsGrounded("We killed the dragon.", memory, NewWorld(null), string.Empty, out reason);
                store.FlushPending(true);
                return !grounded ? null : "conversation became fact";
            }
            finally { DeleteTempDirectory(dir); }
        }

        private static string TestFamiliarityDoesNotAnchorHistory()
        {
            SimMemory memory = NewMemory();
            memory.Familiarity = 1f;
            string reason;
            return !GroundingGuard.IsGrounded("Remember when the drake got us?", memory, NewWorld(null), string.Empty, out reason) ? null : "familiarity established history";
        }

        private static string TestRelationshipDoesNotAnchorHistory()
        {
            SimMemory memory = NewMemory();
            memory.Familiarity = 1f;
            memory.Rapport = 1f;
            memory.Rivalry = 1f;
            memory.SimRelationships.Add(new SimRelationshipMemory { OtherSimKey = "cyndara", OtherName = "Cyndara", SharedOutings = 50, SharedMinutes = 5000, SharedConversationThreads = 30, Familiarity = 1f, Rapport = 1f, Rivalry = 1f });
            string reason;
            return !GroundingGuard.IsGrounded("We used to farm the drake together.", memory, NewWorld(null), string.Empty, out reason) ? null : "relationship counters established history";
        }

        private static string TestJoinDoesNotIncreaseFamiliarity()
        {
            string dir = NewTempDirectory();
            try
            {
                MemoryStore store = new MemoryStore(dir, null);
                SimSnapshot sim = NewSim();
                store.RecordGroupJoin(sim);
                store.RecordGroupLeave(sim);
                store.RecordGroupJoin(sim);
                SimMemory memory = store.LoadForPrompt(sim);
                store.FlushPending(true);
                return memory.GroupSessions == 2 && memory.Familiarity == 0f ? null : "join changed familiarity=" + memory.Familiarity;
            }
            finally { DeleteTempDirectory(dir); }
        }

        private static string TestRelationshipOverlap()
        {
            string dir = NewTempDirectory();
            try
            {
                MemoryStore store = new MemoryStore(dir, null);
                Clock clock = new Clock(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                SessionTelemetry telemetry = new SessionTelemetry(null, store, delegate { return clock.Now; });
                SimSnapshot phanty = NewSim();
                SimSnapshot cyndara = new SimSnapshot { Key = "cyndara", Name = "Cyndara", ClassName = "Druid", Scene = "Brakke" };
                WorldSnapshot world = NewWorld(null);
                world.Party = new List<SimSnapshot> { phanty, cyndara };
                telemetry.Observe(world, world.Party);
                for (int i = 0; i < 41; i++)
                {
                    clock.Advance(3);
                    telemetry.Observe(world, world.Party);
                }
                clock.Advance(1);
                telemetry.Observe(world, new List<SimSnapshot>());
                SimMemory memory = store.LoadForPrompt(phanty);
                store.FlushPending(true);
                return memory.SimRelationships.Count == 1 && memory.SimRelationships[0].SharedOutings == 1 && memory.SimRelationships[0].SharedMinutes >= 2
                    ? null : "overlap was not credited exactly once";
            }
            finally { DeleteTempDirectory(dir); }
        }

        private static string TestMissingMemory()
        {
            string dir = NewTempDirectory();
            try
            {
                MemoryStore store = new MemoryStore(dir, null);
                SimMemory memory = store.LoadForPrompt(NewSim());
                store.FlushPending(true);
                return memory != null && memory.RecentEvents != null && memory.ConversationSummaries != null ? null : "not normalized";
            }
            finally { DeleteTempDirectory(dir); }
        }

        private static string TestMalformedMemory()
        {
            string dir = NewTempDirectory();
            try
            {
                File.WriteAllText(Path.Combine(dir, "phanty.json"), "{ definitely not json");
                MemoryStore store = new MemoryStore(dir, null);
                SimMemory memory = store.LoadForPrompt(NewSim());
                store.FlushPending(true);
                return memory != null && memory.SimKey == "phanty" ? null : "fallback failed";
            }
            finally { DeleteTempDirectory(dir); }
        }

        private static string TestOlderMemory()
        {
            string dir = NewTempDirectory();
            try
            {
                File.WriteAllText(Path.Combine(dir, "phanty.json"), "{\"SimKey\":\"phanty\",\"Name\":\"Phanty\",\"RecentEvents\":[]}");
                MemoryStore store = new MemoryStore(dir, null);
                SimMemory memory = store.LoadForPrompt(NewSim());
                store.FlushPending(true);
                return memory.Conversation != null && memory.OutingSummaries != null && memory.SimRelationships != null && memory.Preferences != null ? null : "optional fields missing";
            }
            finally { DeleteTempDirectory(dir); }
        }

        private static string TestLegacyDialogueMigration()
        {
            string dir = NewTempDirectory();
            try
            {
                File.WriteAllText(Path.Combine(dir, "phanty.json"), "{\"SimKey\":\"phanty\",\"Name\":\"Phanty\",\"RecentEvents\":[{\"type\":\"deep_group_chat\",\"text\":\"we killed a dragon\"},{\"type\":\"loot\",\"text\":\"Looted Aetheria\"}]}");
                MemoryStore store = new MemoryStore(dir, null);
                SimMemory memory = store.LoadForPrompt(NewSim());
                store.FlushPending(true);
                return memory.RecentEvents.Count == 1 && memory.RecentEvents[0].type == "loot" ? null : "legacy dialogue was retained";
            }
            finally { DeleteTempDirectory(dir); }
        }

        private static string TestSessionReload()
        {
            string dir = NewTempDirectory();
            try
            {
                SimSnapshot sim = NewSim();
                MemoryStore first = new MemoryStore(dir, null);
                first.RecordObservedEvent(sim, "loot", "Looted Aetheria.", 80, true);
                first.RecordExpressedPreference(sim, "zone_preference", "gloomy zones have the best vibe");
                first.FlushPending(true);
                MemoryStore second = new MemoryStore(dir, null);
                SimMemory loaded = second.LoadForPrompt(sim);
                second.FlushPending(true);
                return ContainsEvent(loaded.RecentEvents, "Looted Aetheria.") && loaded.Preferences.Count == 1 &&
                    loaded.Preferences[0].Statement.IndexOf("gloomy", StringComparison.OrdinalIgnoreCase) >= 0
                    ? null : "persisted event or flavor preference missing";
            }
            finally { DeleteTempDirectory(dir); }
        }

        private static string TestLocalPlayerKillLog()
        {
            Clock clock = new Clock(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            SessionTelemetry telemetry = NewTelemetry(clock);
            telemetry.Observe(NewWorld(null), Party());
            telemetry.ObserveLogLine("You have slain A Young Wolf!");
            OutingSnapshot snap = telemetry.Snapshot();
            return snap.TotalKills == 1 && !string.IsNullOrWhiteSpace(snap.CurrentEncounter) &&
                snap.CurrentEncounter.IndexOf("Young Wolf", StringComparison.OrdinalIgnoreCase) >= 0
                ? null : "visible local kill was not attributed";
        }

        private static string TestTransientWriteRetry()
        {
            string dir = NewTempDirectory();
            try
            {
                int attempts = 0;
                MemoryStore store = new MemoryStore(dir, null, delegate
                {
                    attempts++;
                    return attempts > 1;
                });
                SimSnapshot sim = NewSim();
                store.RecordObservedEvent(sim, "loot", "Looted Aetheria.", 80, true);
                store.FlushPending(true);
                MemoryStore reload = new MemoryStore(dir, null);
                SimMemory loaded = reload.LoadForPrompt(sim);
                reload.Shutdown();
                store.Shutdown();
                if (attempts < 2) return "failed snapshot was not retried";
                return ContainsEvent(loaded.RecentEvents, "Looted Aetheria.") ? null : "retried snapshot was not persisted";
            }
            finally { DeleteTempDirectory(dir); }
        }

        private static bool Contains(IList<string> values, string text)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Count; i++) if (!string.IsNullOrWhiteSpace(values[i]) && values[i].IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool ContainsEvent(IList<MemoryEvent> values, string text)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Count; i++) if (values[i] != null && string.Equals(values[i].text, text, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string NewTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "DeepSimsRegression-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteTempDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            Directory.Delete(path, true);
        }
    }
}
