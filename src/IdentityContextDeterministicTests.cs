using System;
using System.Collections.Generic;
using System.IO;

namespace ErenshorDeepSims
{
    internal static class IdentityContextDeterministicTests
    {
        internal static List<string> Run()
        {
            List<string> lines = new List<string>();
            SimSnapshot dancer = Sim("dancer-key", "Dancer", "Windblade");
            SimSnapshot cyndara = Sim("cyndara-key", "Cyndara", "Arcanist");

            SimMemory identity = NewMemory(dancer);
            identity.AuthoredIdentity.CorePersonality = "patient, curious, and quietly competitive";
            identity.AuthoredIdentity.PersonalBackground = "a history student worried about finding work after school";
            identity.AuthoredIdentity.ErenshorPersona = "a junior member of the Order of Dawn";
            identity.AuthoredIdentity.RelationshipToPlayer = "an old friend and trusted adventuring partner";
            StructuredMemoryRecord archive = IdentitySchema.Create("authored_shared_history",
                "Dancer and the player entered the sealed academy archive together.", 95, true, false,
                dancer.Key, dancer.Name, "test_authored");
            identity.AuthoredIdentity.SharedHistory.Add(archive);
            StructuredMemoryRecord learnedConflict = IdentitySchema.Create("episodic",
                "Dancer believes she never entered the sealed academy archive with the player.", 100, false, false,
                dancer.Key, dancer.Name, "verified_event:test");
            identity.StructuredMemories.Add(learnedConflict);
            identity.Normalize();

            SimMemory cloned = SimMemoryPersistence.Clone(identity);
            cloned.Normalize();
            lines.Add("[DeepSims Identity] layered identity serializes and reloads: " + Pass(
                cloned.AuthoredIdentity != null && cloned.AuthoredIdentity.ErenshorPersona == identity.AuthoredIdentity.ErenshorPersona &&
                cloned.AuthoredIdentity.SharedHistory.Count == 1 && cloned.AuthoredIdentity.SharedHistory[0].KnownBy.Count >= 2));

            AuthoredIdentityProfile noPrune = new AuthoredIdentityProfile();
            noPrune.Normalize();
            for (int i = 0; i < 30; i++) noPrune.PinnedMemories.Add(IdentitySchema.Create("authored_pinned", "authored memory " + i, 100, true, true, dancer.Key, dancer.Name, "test"));
            noPrune.Normalize();
            lines.Add("[DeepSims Identity] normalization never silently prunes authored memories: " + Pass(noPrune.PinnedMemories.Count == 30));

            List<RelevantMemory> precedence = StructuredMemoryRetrieval.Select(identity, dancer, "sealed academy archive", 2);
            lines.Add("[DeepSims Identity] authored shared history outranks conflicting learned memory: " + Pass(
                precedence.Count > 0 && precedence[0].Source == "authored-shared-history" && precedence[0].Text.IndexOf("entered the sealed academy", StringComparison.OrdinalIgnoreCase) >= 0));

            StructuredMemoryRecord privateFact = IdentitySchema.Create("authored_shared_history", "Dancer hid a silver key beneath the academy stairs.", 90, true, false,
                dancer.Key, dancer.Name, "player_authored");
            privateFact.KnownBy = new List<string> { dancer.Key, dancer.Name, "player" };
            SimMemory boundary = NewMemory(cyndara);
            boundary.AuthoredIdentity.RelationshipToPlayer = "regards the player as a sibling-like old friend";
            boundary.SimRelationships.Add(new SimRelationshipMemory
            {
                OtherSimKey = dancer.Key, OtherName = dancer.Name, Familiarity = 65f, Rapport = 55f, Rivalry = 15f, LastSharedUtc = DateTime.UtcNow.ToString("o")
            });
            boundary.AuthoredIdentity.SharedHistory.Add(privateFact);
            boundary.Normalize();
            List<RelevantMemory> denied = StructuredMemoryRetrieval.Select(boundary, cyndara, "silver key academy stairs", 3);
            lines.Add("[DeepSims Identity] authored shared-history known-by ownership blocks an uninformed Sim: " + Pass(denied.Count == 0));

            StructuredMemoryRecord pinned = IdentitySchema.Create("authored_pinned", "The player promised Dancer they would never abandon the old village shrine.", 100, true, true,
                dancer.Key, dancer.Name, "test_pinned");
            identity.AuthoredIdentity.PinnedMemories.Add(pinned);
            identity.Normalize();
            List<RelevantMemory> pinnedResult = StructuredMemoryRetrieval.Select(identity, dancer, "village shrine promise", 1);
            lines.Add("[DeepSims Identity] pinned authored memory receives deterministic retrieval priority: " + Pass(
                pinnedResult.Count == 1 && pinnedResult[0].Source == "authored-pinned"));

            List<RelevantMemory> unrelatedPinned = StructuredMemoryRetrieval.Select(identity, dancer, "weather over the ridge", 2);
            lines.Add("[DeepSims Identity] pinned memory is not blindly injected into an unrelated turn: " + Pass(
                unrelatedPinned.Count == 0));

            SimMemory emotionalMemory = NewMemory(dancer);
            StructuredMemoryRecord emotional = IdentitySchema.Create("episodic", "archive bell memory alpha", 70, false, false, dancer.Key, dancer.Name, "verified_event:test");
            StructuredMemoryRecord neutral = IdentitySchema.Create("episodic", "archive bell memory beta", 70, false, false, dancer.Key, dancer.Name, "verified_event:test");
            emotional.Utc = string.Empty; neutral.Utc = string.Empty;
            emotional.EmotionalTags = new List<string> { "trust" };
            neutral.EmotionalTags = new List<string>();
            emotionalMemory.StructuredMemories.Add(emotional);
            emotionalMemory.StructuredMemories.Add(neutral);
            List<RelevantMemory> emotionalResult = StructuredMemoryRetrieval.Select(emotionalMemory, dancer, "archive bell", 1);
            lines.Add("[DeepSims Identity] emotional significance contributes to deterministic memory ranking: " + Pass(
                emotionalResult.Count == 1 && emotionalResult[0].Text.IndexOf("alpha", StringComparison.OrdinalIgnoreCase) >= 0));

            SocialPerspectiveState.Current = SocialPerspectiveMode.Roleplay;
            IdentityPromptContext roleplayUnrelated = IdentityContextPolicy.Select(dancer, identity, "do you like being a Windblade?", true);
            IdentityPromptContext roleplayBackground = IdentityContextPolicy.Select(dancer, identity, "what did you study in school?", true);
            IdentityPromptContext mmoBackground = IdentityContextPolicy.Select(dancer, identity, "what did you study in school?", false);
            SocialEventCandidate roleplayEvent = new SocialEventCandidate("encounter_complete", new DateTime(2026, 1, 1),
                new[] { dancer.Name }, new[] { dancer.Name }, new[] { "goblin" }, SocialEventTrust.Experienced, 70, 1.0,
                "encounter", "Completed fight: 2 goblin kills.", 0.8);
            List<ChatMessage> roleplayEventPrompt = PromptBuilder.BuildVerifiedEventThread(dancer, new WorldSnapshot { Scene = "Test Zone" }, roleplayEvent, null, 1, identity);
            string roleplayEventText = JoinMessages(roleplayEventPrompt);
            SocialPerspectiveState.ResetForTests();
            lines.Add("[DeepSims Identity] unrelated modern background is excluded from roleplay: " + Pass(
                string.IsNullOrWhiteSpace(roleplayUnrelated.PersonalBackground) && !string.IsNullOrWhiteSpace(roleplayUnrelated.ErenshorPersona)));
            lines.Add("[DeepSims Identity] modern personal background remains hidden even when directly asked in roleplay: " + Pass(
                string.IsNullOrWhiteSpace(roleplayBackground.PersonalBackground) &&
                !string.IsNullOrWhiteSpace(roleplayBackground.ErenshorPersona)));
            lines.Add("[DeepSims Identity] relevant personal background remains available in MMO perspective: " + Pass(
                mmoBackground.PersonalBackground.IndexOf("history student", StringComparison.OrdinalIgnoreCase) >= 0));
            lines.Add("[DeepSims Identity] verified-event roleplay carries authored persona without leaking unrelated modern background: " + Pass(
                roleplayEventText.IndexOf("Order of Dawn", StringComparison.OrdinalIgnoreCase) >= 0 &&
                roleplayEventText.IndexOf("history student", StringComparison.OrdinalIgnoreCase) < 0));

            SemanticTurnRoute opinionRoute = SemanticTurnRouter.Fallback("Do you like being a Windblade?");
            SemanticTurnRoute backgroundRoute = SemanticTurnRouter.Fallback("What did you study before this?");
            SemanticTurnRoute factualRoute = new SemanticTurnRoute { TurnType = SemanticTurnType.DirectQuestion, KnowledgeNeed = KnowledgeNeed.GameWiki, SearchQuery = "Iron Ore" };
            SemanticTurnRouter.ApplyMeaningOverride(factualRoute, "Where does iron ore drop?");
            lines.Add("[DeepSims Identity] personal taste/background questions never trigger lookup: " + Pass(
                opinionRoute.KnowledgeNeed == KnowledgeNeed.None && backgroundRoute.KnowledgeNeed == KnowledgeNeed.None));
            lines.Add("[DeepSims Identity] explicit external game-fact question keeps factual lookup: " + Pass(factualRoute.KnowledgeNeed == KnowledgeNeed.GameWiki));

            List<ChatMessage> budgetMessages = new List<ChatMessage>
            {
                new ChatMessage("system", "RULES " + Repeat('r', 420)),
                new ChatMessage("system", "CURRENT AUTHORITATIVE STATE: " + Repeat('c', 420)),
                new ChatMessage("system", "AUTHOR-DEFINED CORE IDENTITY: " + Repeat('i', 320)),
                new ChatMessage("system", "OPTIONAL EXTERNAL REFERENCE: " + Repeat('o', 700)),
                new ChatMessage("system", "RELEVANT PERSONAL BACKGROUND: " + Repeat('b', 700)),
                new ChatMessage("system", "RELEVANT VERIFIED HISTORY ONLY: " + Repeat('m', 650)),
                new ChatMessage("system", "AUTHOR-DEFINED RELATIONSHIP: " + Repeat('x', 500)),
                new ChatMessage("user", "VISIBLE PARTY CHAT Dancer: " + Repeat('v', 500)),
                new ChatMessage("system", "RETRIEVED EVIDENCE [wiki]: " + Repeat('e', 320)),
                new ChatMessage("user", "PLAYER'S CURRENT MESSAGE: where does it drop?")
            };
            ContextBudgetResult budget = PromptContextBudget.Apply(budgetMessages, 1024, 64);
            string budgetText = JoinMessages(budgetMessages);
            lines.Add("[DeepSims Identity] context budget trims low-priority context first: " + Pass(
                budget.Trimmed && budget.DroppedKinds.Contains("optional-search") && budget.DroppedKinds.Contains("background")));
            lines.Add("[DeepSims Identity] context budget protects current state/core identity/factual evidence/newest turn: " + Pass(
                budgetText.IndexOf("CURRENT AUTHORITATIVE STATE", StringComparison.Ordinal) >= 0 &&
                budgetText.IndexOf("AUTHOR-DEFINED CORE IDENTITY", StringComparison.Ordinal) >= 0 &&
                budgetText.IndexOf("RETRIEVED EVIDENCE", StringComparison.Ordinal) >= 0 &&
                budgetText.IndexOf("PLAYER'S CURRENT MESSAGE", StringComparison.Ordinal) >= 0));

            List<ChatMessage> monolithicBudget = new List<ChatMessage>
            {
                new ChatMessage("system", "RULES " + Repeat('r', 500) + "\nSTYLE-ONLY examples sampled\n" + Repeat('s', 1500) +
                    "\nVERIFIED CURRENT FACTS:\nZone: Test Zone\nPersonal background: " + Repeat('b', 700) +
                    "\nTOPIC-RELEVANT MEMORIES\n- " + Repeat('m', 900) + "\nReply only with one short group-chat message"),
                new ChatMessage("user", "current question")
            };
            ContextBudgetResult monolithic = PromptContextBudget.Apply(monolithicBudget, 1024, 64);
            string monolithicText = JoinMessages(monolithicBudget);
            lines.Add("[DeepSims Identity] context budget trims embedded legacy/system sections without deleting safety/current anchors: " + Pass(
                monolithic.DroppedSections > 0 && monolithicText.IndexOf("RULES", StringComparison.Ordinal) >= 0 &&
                monolithicText.IndexOf("VERIFIED CURRENT FACTS", StringComparison.Ordinal) >= 0 &&
                monolithicText.IndexOf("current question", StringComparison.Ordinal) >= 0));

            List<ConversationLine> simThread = new List<ConversationLine> { new ConversationLine("Dancer", "do you remember the silver key?") };
            WorldSnapshot world = new WorldSnapshot { Scene = "Test Zone", Player = new PlayerSnapshot { Name = "Player" }, Party = new List<SimSnapshot> { dancer, cyndara } };
            List<ChatMessage> simToSim = PromptBuilder.BuildPartyThreadReply(cyndara, boundary, world, simThread, 1, null, null, null, PartyReplyIntent.VerifiedHistoryQuestion);
            string simToSimText = JoinMessages(simToSim);
            lines.Add("[DeepSims Identity] Sim-to-Sim prompt uses the responder's relevant pair relationship without exposing unknown memory/player relationship: " + Pass(
                simToSimText.IndexOf("Cyndara and Dancer", StringComparison.OrdinalIgnoreCase) >= 0 &&
                simToSimText.IndexOf("hid a silver key", StringComparison.OrdinalIgnoreCase) < 0 &&
                simToSimText.IndexOf("sibling-like old friend", StringComparison.OrdinalIgnoreCase) < 0));

            string identityArgument;
            lines.Add("[DeepSims Identity] authored editor command grammar is deterministic: " + Pass(
                ChatCommandParser.TryParseIdentity("/dsidentity Dancer set persona junior member of the Order of Dawn", out identityArgument) &&
                identityArgument.IndexOf("Dancer set persona", StringComparison.OrdinalIgnoreCase) == 0));

            lines.AddRange(RunPersistenceTests(dancer));
            lines.Add("[DeepSims Identity] connected Sim reply loop retains hard cap: " + Pass(SimResponseDecision.MaxResponsesPerLine == 3));
            SocialPerspectiveState.ResetForTests();
            return lines;
        }

        private static List<string> RunPersistenceTests(SimSnapshot sim)
        {
            List<string> lines = new List<string>();
            string root = Path.Combine(Path.GetTempPath(), "DeepSimsIdentityTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                string scopeA = Path.Combine(root, "character-a");
                MemoryStore first = new MemoryStore(scopeA, NullDeepSimsLog.Instance);
                string result;
                first.TrySetAuthoredField(sim, "background", "studied history and is unsure about a future career", out result);
                first.TryAddAuthoredMemory(sim, "pinned", "The player is trusted with a private family keepsake.", out result);
                first.FlushPending(true);
                first.Shutdown();

                MemoryStore reload = new MemoryStore(scopeA, NullDeepSimsLog.Instance);
                SimMemory reloaded = reload.LoadForPrompt(sim);
                lines.Add("[DeepSims Identity] authored identity persists and reloads per character scope: " + Pass(
                    reloaded.AuthoredIdentity.PersonalBackground.IndexOf("studied history", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    reloaded.AuthoredIdentity.PinnedMemories.Count == 1));
                reload.Shutdown();

                string scopeB = Path.Combine(root, "character-b");
                MemoryStore otherCharacter = new MemoryStore(scopeB, NullDeepSimsLog.Instance);
                SimMemory isolated = otherCharacter.LoadForPrompt(sim);
                lines.Add("[DeepSims Identity] character switching does not leak authored identity between scopes: " + Pass(
                    string.IsNullOrWhiteSpace(isolated.AuthoredIdentity.PersonalBackground) && isolated.AuthoredIdentity.PinnedMemories.Count == 0));
                otherCharacter.Shutdown();

                string legacyDir = Path.Combine(root, "legacy");
                Directory.CreateDirectory(legacyDir);
                File.WriteAllText(Path.Combine(legacyDir, "legacy-key.json"),
                    "{\"SimKey\":\"legacy-key\",\"Name\":\"Legacy\",\"ImportantMemories\":[\"Legacy important memory remains\"],\"RecentEvents\":[]}");
                MemoryStore legacyStore = new MemoryStore(legacyDir, NullDeepSimsLog.Instance);
                SimMemory legacy = legacyStore.LoadForPrompt(Sim("legacy-key", "Legacy", "Druid"));
                lines.Add("[DeepSims Identity] old saved data migrates without deleting legacy memory: " + Pass(
                    legacy.IdentityDataVersion == IdentitySchema.CurrentVersion && legacy.AuthoredIdentity != null &&
                    legacy.ImportantMemories.Contains("Legacy important memory remains")));
                legacyStore.Shutdown();

                string malformedDir = Path.Combine(root, "malformed");
                Directory.CreateDirectory(malformedDir);
                File.WriteAllText(Path.Combine(malformedDir, "bad-key.json"), "{ definitely not valid json");
                MemoryStore malformedStore = new MemoryStore(malformedDir, NullDeepSimsLog.Instance);
                SimMemory recovered = malformedStore.LoadForPrompt(Sim("bad-key", "Recovered", "Paladin"));
                lines.Add("[DeepSims Identity] malformed saved data fails closed to a fresh bounded record: " + Pass(
                    recovered != null && recovered.AuthoredIdentity != null && recovered.IdentityDataVersion == IdentitySchema.CurrentVersion));
                malformedStore.Shutdown();
            }
            catch (Exception ex)
            {
                lines.Add("[DeepSims Identity] persistence/migration test harness: FAIL (" + ex.GetType().Name + ")");
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }
            return lines;
        }

        private static SimSnapshot Sim(string key, string name, string className)
        {
            return new SimSnapshot
            {
                Key = key, Name = name, ClassName = className, Scene = "Test Zone", Personality = "Friendly",
                Level = 20, AssignedRoles = new List<string>(), DialogueExamples = new List<string>()
            };
        }

        private static SimMemory NewMemory(SimSnapshot sim)
        {
            SimMemory memory = new SimMemory { SimKey = sim.Key, Name = sim.Name, RecentEvents = new List<MemoryEvent>(), ImportantMemories = new List<string>(), Conversation = new List<ChatMessage>() };
            memory.Normalize();
            return memory;
        }

        private static string Repeat(char c, int count) { return new string(c, count); }
        private static string JoinMessages(IList<ChatMessage> messages)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (messages != null) for (int i = 0; i < messages.Count; i++) if (messages[i] != null) sb.AppendLine(messages[i].content ?? string.Empty);
            return sb.ToString();
        }
        private static string Pass(bool value) { return value ? "PASS" : "FAIL"; }
    }
}
