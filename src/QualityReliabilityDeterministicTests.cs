using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    internal static class QualityReliabilityDeterministicTests
    {
        internal static List<string> Run()
        {
            List<string> results = new List<string>();
            TestExternalNews(results);
            TestGrounding(results);
            TestWikiHygiene(results);
            TestExpeditionAndCamp(results);
            TestRelax(results);
            TestPromptRobustness(results);
            TestMemorySalience(results);
            TestReflectionProvenance(results);
            TestNewsProvenanceAndOpinionGuards(results);
            TestVoiceQuality(results);
            return results;
        }

        private static void TestVoiceQuality(List<string> results)
        {
            WorldSnapshot world = new WorldSnapshot();
            world.Player = new PlayerSnapshot { Name = "Berad" };
            world.Party = new List<SimSnapshot>
            {
                new SimSnapshot { Name = "Aldric", PersonalityCode = 0 },
                new SimSnapshot { Name = "Nyxe", PersonalityCode = -1 }
            };
            string reason;
            Add(results, "voice/near-miss verified party name rejected",
                !ReplyVoiceGuard.IsAcceptable("tanking feels safer for aldsic", world, out reason) && reason.IndexOf("Aldric", StringComparison.OrdinalIgnoreCase) >= 0);
            Add(results, "voice/trailing greeting filler rejected",
                !ReplyVoiceGuard.IsAcceptable("best-looking zone? lol heyo", world, out reason) && reason == "trailing_greeting_filler");
            Add(results, "voice/ordinary short MMO line accepted",
                ReplyVoiceGuard.IsAcceptable("paladin tanking feels heavy lol but fun", world, out reason));
            Add(results, "voice/friendly contract is warm without fabricated biography",
                NativeDialogueStyle.DescribeVoiceContract(world.Party[0]).IndexOf("Warm", StringComparison.OrdinalIgnoreCase) >= 0);
            Add(results, "voice/unmapped contract forbids invented catchphrase",
                NativeDialogueStyle.DescribeVoiceContract(world.Party[1]).IndexOf("catchphrases", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // Covers the live-bug fix pass: EXTERNAL_NEWS provenance-aware grounding (never validated
        // against Erenshor game facts), cross-headline relationship fabrication, retry corpus reuse,
        // honest-failure fallback wording, opinion/generalization vs concrete action-fact assertions,
        // shared-history "again" guarding, and stale/generation-based discard vs grounding rejection.
        private static void TestNewsProvenanceAndOpinionGuards(List<string> results)
        {
            SimSnapshot dancer = new SimSnapshot { Name = "Dancer", ClassName = "Windblade", Level = 20 };
            WorldSnapshot world = new WorldSnapshot();
            world.Party = new List<SimSnapshot> { dancer };
            world.Outing = new OutingSnapshot { Activity = "adventuring/downtime" };
            SimMemory memory = new SimMemory();
            memory.Normalize();
            memory.Name = "Dancer";

            // 1. A supported NASA statement is permitted from a successful external-news bundle even
            // though NASA is absent from any Erenshor game fact.
            WikiResult nasaNews = new WikiResult();
            nasaNews.Found = true;
            nasaNews.Query = "nasa";
            nasaNews.SourceLabel = "external real-world news search";
            nasaNews.Title = "NASA captures rare eclipse data";
            nasaNews.Extract = "[Space News - 2h ago] NASA captures rare eclipse data during its latest mission.";
            NewsGroundCase(results, "news/1 supported nasa statement not compared against game facts",
                "NASA captured some rare eclipse data recently.", true, memory, world, nasaNews);

            // 2. Two independent headlines cannot be merged into an unsupported causal relationship.
            WikiResult twoHeadlines = new WikiResult();
            twoHeadlines.Found = true;
            twoHeadlines.Query = "nasa";
            twoHeadlines.SourceLabel = "external real-world news search";
            twoHeadlines.Title = "NASA eclipse mission; NASA fire storm study";
            twoHeadlines.Extract = "[Space News - 2h ago] NASA's mission captures rare eclipse data. | [Weather Wire - 1h ago] NASA studies fire storms from orbit.";
            NewsGroundCase(results, "news/2 cross-headline relationship rejected",
                "NASA's eclipse mission is threatened by fire storms.", false, memory, world, twoHeadlines);
            NewsGroundCase(results, "news/2b safe side-by-side headline mention allowed",
                "NASA's covering eclipse data and fire storm news too.", true, memory, world, twoHeadlines);

            // 3. "again" in an external-news answer is rejected unless the headlines themselves support
            // repetition/history.
            NewsGroundCase(results, "news/3 unsupported again in news answer rejected",
                "NASA is stuck in the shadow again.", false, memory, world, nasaNews);

            // 4/5. Retry keeps the same news reference corpus and a successful bundle never falls back to
            // generic "not sure" wording; ExternalNewsCorrectionPrompt must reference the rejected draft's
            // reason and stay news-specific rather than becoming generic party chatter.
            string correction = GroundingGuard.ExternalNewsCorrectionPrompt("bad draft", "combined two headlines");
            Add(results, "news/4 correction prompt stays news-scoped and keeps rejected draft",
                correction.IndexOf("one concrete supplied headline", StringComparison.OrdinalIgnoreCase) >= 0 &&
                correction.IndexOf("bad draft", StringComparison.OrdinalIgnoreCase) >= 0);
            Add(results, "news/5 knowledge correction prompt is distinct from generic correction prompt",
                !string.Equals(GroundingGuard.KnowledgeCorrectionPrompt("x", "y"), GroundingGuard.CorrectionPrompt("x", "y"), StringComparison.Ordinal));

            // 6. External news never becomes verified Erenshor game memory: BuildVerifiedCorpus-driven
            // IsGrounded must not treat a news-sourced WikiResult as VERIFIED game state anywhere.
            NewsGroundCase(results, "news/6 external news is not folded into verified game facts",
                "NASA killed the boss.", false, memory, world, nasaNews);

            // 7. General opinion/generalization about healing is allowed even though it mentions "heal".
            GroundCaseLocal(results, "news/7 general healing opinion allowed", "the RNG is usually bad at healing tanks lately", true, memory, world, string.Empty);
            GroundCaseLocal(results, "news/7b general mmo joke allowed", "tanks always get blamed", true, memory, world, string.Empty);

            // 8. A concrete first-person action-fact claim still requires evidence.
            GroundCaseLocal(results, "news/8 concrete heal action claim still rejected without evidence", "I healed you earlier with Regrowth", false, memory, world, string.Empty);

            // 9. Hypothetical intent is allowed.
            GroundCaseLocal(results, "news/9 hypothetical frost build allowed", "I might try a frost build sometime", true, memory, world, string.Empty);

            // 10. Shared/personal history phrased with "again" is rejected without verified prior history.
            GroundCaseLocal(results, "news/10 frost combo again rejected without history", "you wanna try that frost combo again?", false, memory, world, string.Empty);

            // 11. Player topic change during an in-flight news lookup: the generation guard already used
            // for party-thread continuation (ConversationTurnGuard.IsStale) must mark the news work item
            // stale once the live generation has advanced, so it never displays.
            long newsWorkGeneration = 4;
            long liveGenerationAfterTopicChange = 5;
            Add(results, "news/11 topic change during lookup marks news result stale",
                ConversationTurnGuard.IsStale(newsWorkGeneration, liveGenerationAfterTopicChange));
            Add(results, "news/11b unchanged generation is not stale", !ConversationTurnGuard.IsStale(newsWorkGeneration, newsWorkGeneration));

            // 12. Diagnostics distinguish a grounding rejection (has a specific reason string) from a
            // stale/generation discard (identified purely by generation mismatch, no grounding reason).
            string groundingRejectReason;
            GroundingGuard.IsKnowledgeModeGrounded("NASA's eclipse mission is threatened by fire storms.", memory, world, twoHeadlines, out groundingRejectReason);
            bool staleDiscard = ConversationTurnGuard.IsStale(newsWorkGeneration, liveGenerationAfterTopicChange);
            Add(results, "news/12 grounding-reject carries a reason while stale-discard does not",
                !string.IsNullOrWhiteSpace(groundingRejectReason) && staleDiscard);
        }

        private static void NewsGroundCase(List<string> results, string name, string reply, bool expected, SimMemory memory, WorldSnapshot world, WikiResult facts)
        {
            string gameReason;
            bool gameGrounded = GroundingGuard.IsGrounded(reply, memory, world, string.Empty, out gameReason);
            string knowledgeReason;
            bool knowledgeGrounded = !gameGrounded ? false : GroundingGuard.IsKnowledgeModeGrounded(reply, memory, world, facts, out knowledgeReason);
            bool grounded = gameGrounded && knowledgeGrounded;
            Add(results, name, grounded == expected);
        }

        private static void GroundCaseLocal(List<string> results, string name, string reply, bool expected, SimMemory memory, WorldSnapshot world, string situation)
        {
            string reason;
            bool grounded = GroundingGuard.IsGrounded(reply, memory, world, situation, out reason);
            Add(results, name, grounded == expected);
        }

        private static void TestExternalNews(List<string> results)
        {
            NewsCase(results, "natural NASA heard news", "anyone heard any news about NASA lately?", true, "NASA");
            NewsCase(results, "natural OpenAI heard anything", "heard anything about OpenAI lately?", true, "OpenAI");
            NewsCase(results, "natural NASA short news", "any NASA news lately?", true, "NASA");
            NewsCase(results, "natural NASA happening", "anything happening with NASA lately?", true, "NASA");
            NewsCase(results, "natural NASA up to", "what has NASA been up to lately?", true, "NASA");
            NewsCase(results, "natural NASA this week", "NASA news this week?", true, "NASA");
            NewsCase(results, "natural OpenAI today", "heard any OpenAI news today?", true, "OpenAI");
            NewsCase(results, "generic party news request", "anyone hear any news?", true, "top world news");
            NewsCase(results, "generic heard-news request", "heard any news?", true, "top world news");
            NewsCase(results, "good news is not external", "good news everyone", false, null);
            NewsCase(results, "quest news is not external", "I have news about our quest", false, null);
            NewsCase(results, "untimed anyone hear news on", "anyone hear any news on nasa?", true, "nasa");
            NewsCase(results, "untimed anyone hear news about", "anyone hear any news about nasa?", true, "nasa");
            NewsCase(results, "untimed heard anything about", "heard anything about nasa?", true, "nasa");
            NewsCase(results, "untimed quest news stays game route", "anyone hear any news on the Bonepits quest?", false, null);
            NewsCase(results, "untimed item news stays game route", "anyone hear any news on the new item?", false, null);
            // Bare "any news on X" (no hear/heard) deliberately stays on the wiki route so it does not
            // regress the pre-existing Krakengard game-knowledge routing case below.
            NewsCase(results, "bare any news on stays wiki route", "any news on nasa?", false, null);

            string kraken = "any news on Krakengard?";
            bool gameRoute = !ExternalNewsQueryClassifier.ShouldLookup(kraken) &&
                KnowledgeQueryClassifier.ShouldLookup(kraken) &&
                string.Equals(KnowledgeQueryClassifier.ExtractSearchQuery(kraken, "Vitheo"), "Krakengard", StringComparison.OrdinalIgnoreCase);
            Add(results, "external/Krakengard stays on game-knowledge route", gameRoute);

            List<ExternalNewsItem> raw = new List<ExternalNewsItem>
            {
                new ExternalNewsItem { Headline = "NASA delays Artemis launch", Publisher = "Reuters", Url = "https://example.com/nasa" },
                new ExternalNewsItem { Headline = "Local football club names new coach", Publisher = "AP", Url = "https://example.com/sport" }
            };
            List<ExternalNewsItem> relevant = ExternalNewsRelevance.Filter("NASA today", raw);
            Add(results, "external/raw irrelevant results do not become usable evidence", relevant.Count == 1 && relevant[0].Headline.IndexOf("NASA", StringComparison.Ordinal) >= 0);
            Add(results, "external/arbitrary current-events subject uses same relevance path",
                ExternalNewsRelevance.Filter("OpenAI today", new List<ExternalNewsItem> { new ExternalNewsItem { Headline = "OpenAI announces research update", Url = "https://example.com/openai" } }).Count == 1);

            WikiResult evidence = new WikiResult { Found = true, SourceLabel = "external real-world news search", Title = "NASA delays Artemis launch", Extract = "[Reuters - 1h ago] NASA delays Artemis launch" };
            SimMemory memory = new SimMemory(); memory.Normalize();
            string reason;
            Add(results, "external/retrieved evidence admits consistent answer",
                GroundingGuard.IsKnowledgeModeGrounded("NASA delayed the Artemis launch.", memory, new WorldSnapshot(), evidence, out reason));
            Add(results, "external/no-news contradiction is rejected for retry",
                !GroundingGuard.IsKnowledgeModeGrounded("No new NASA news today.", memory, new WorldSnapshot(), evidence, out reason) && reason == "contradicted retrieved evidence");
            Add(results, "external/second contradiction has deterministic evidence fallback",
                GroundingGuard.ExternalNewsEvidenceFallback(evidence).IndexOf("NASA delays Artemis launch", StringComparison.Ordinal) >= 0);
            WikiResult miss = new WikiResult { Found = false, SourceLabel = "external real-world news search", Extract = string.Empty };
            Add(results, "external/zero relevant evidence permits bounded no-result response",
                GroundingGuard.IsKnowledgeModeGrounded("couldn't find anything relevant right now", memory, new WorldSnapshot(), miss, out reason));
        }

        private static void NewsCase(List<string> results, string name, string message, bool expectedLookup, string expectedQuery)
        {
            bool lookup = ExternalNewsQueryClassifier.ShouldLookup(message);
            bool queryOk = !expectedLookup || string.Equals(ExternalNewsQueryClassifier.ExtractQuery(message), expectedQuery, StringComparison.Ordinal);
            Add(results, "external/" + name, lookup == expectedLookup && queryOk);
        }

        private static void TestGrounding(List<string> results)
        {
            SimSnapshot dancer = new SimSnapshot { Name = "Dancer", ClassName = "Windblade", Level = 20 };
            SimSnapshot brinon = new SimSnapshot { Name = "Brinon", ClassName = "Paladin", Level = 20 };
            WorldSnapshot world = new WorldSnapshot();
            world.Party = new List<SimSnapshot> { dancer, brinon };
            world.Outing = new OutingSnapshot { Activity = "adventuring/downtime" };
            SimMemory memory = new SimMemory();
            memory.Normalize();
            memory.Name = "Dancer";

            GroundCase(results, "class/wrong self class rejected", "I'm a druid.", false, memory, world, string.Empty);
            GroundCase(results, "class/wrong self role-name rejected", "as a paladin, I'd tank it", false, memory, world, string.Empty);
            GroundCase(results, "class/verified self class allowed", "I'm the Windblade here.", true, memory, world, string.Empty);
            GroundCase(results, "class/general class discussion allowed", "paladins are sturdy", true, memory, world, string.Empty);

            GroundCase(results, "acquisition/got it acknowledgement", "got it", true, memory, world, string.Empty);
            GroundCase(results, "acquisition/gotcha acknowledgement", "gotcha", true, memory, world, string.Empty);
            GroundCase(results, "acquisition/I've got you acknowledgement", "I've got you", true, memory, world, string.Empty);
            GroundCase(results, "acquisition/I got it wait acknowledgement", "I got it, wait here", true, memory, world, string.Empty);
            GroundCase(results, "acquisition/okay got it acknowledgement", "okay, got it", true, memory, world, string.Empty);
            GroundCase(results, "acquisition/I got sword requires evidence", "I got the sword", false, memory, world, string.Empty);
            GroundCase(results, "acquisition/I got helmet requires evidence", "I got a new helmet", false, memory, world, string.Empty);
            GroundCase(results, "acquisition/looted ring requires evidence", "I looted the ring", false, memory, world, string.Empty);
            GroundCase(results, "acquisition/boss gave staff requires evidence", "the boss gave me the staff", false, memory, world, string.Empty);

            string questOnly = "Verified current-session observation: The player completed quest X.";
            GroundCase(results, "event/quest cannot imply party recovered", "the party is fully recovered now", false, memory, world, questOnly);
            GroundCase(results, "event/quest reaction itself allowed", "nice job finishing it", true, memory, world, questOnly);

            GroundCase(results, "idle/unverified damage and repeated history rejected", "that hurt Brinon hard, just like usual", false, memory, world, string.Empty);
        }

        private static void GroundCase(List<string> results, string name, string reply, bool expected, SimMemory memory, WorldSnapshot world, string situation)
        {
            string reason;
            bool grounded = GroundingGuard.IsGrounded(reply, memory, world, situation, out reason);
            Add(results, name, grounded == expected);
        }

        private static void TestWikiHygiene(List<string> results)
        {
            string fixture = "<div class='mw-parser-output'><p>Vitheo's Watch has a path toward Duskenlight.</p>" +
                "<div class='navbox'>Zone Navigation: Port Azure Brakke Vitheo's Watch Duskenlight Stowaway Steppe Lost Basin</div>" +
                "<footer>Categories Zones Navigation Main Page</footer></div>";
            string clean = WikiClient.StripBoilerplateForTests(fixture);
            bool stripped = clean.IndexOf("Zone Navigation", StringComparison.OrdinalIgnoreCase) < 0 &&
                clean.IndexOf("Categories Zones", StringComparison.OrdinalIgnoreCase) < 0 &&
                clean.IndexOf("Vitheo's Watch has a path", StringComparison.OrdinalIgnoreCase) >= 0;
            Add(results, "wiki/navigation and footer boilerplate stripped", stripped);

            int exact = WikiClient.RelevanceScoreForTests("Vitheo's Watch", "Vitheo's Watch is a zone connected by local exits.", "Vitheo's Watch");
            int generic = WikiClient.RelevanceScoreForTests("Zones", "Navigation list: Vitheo's Watch, Duskenlight, Port Azure, Brakke.", "Vitheo's Watch Duskenlight");
            Add(results, "wiki/exact entity page strongly preferred", exact > generic + 40);

            string navOnly = "Navigation: Zones Port Azure Brakke Vitheo's Watch Duskenlight Stowaway Steppe Lost Basin";
            string weakWindow = WikiClient.RelevantWindowForTests("Zones", navOnly, "Vitheo's Watch Duskenlight route", 420);
            Add(results, "wiki/navigation-only text is insufficient evidence", string.IsNullOrWhiteSpace(weakWindow));
        }

        private static void TestExpeditionAndCamp(List<string> results)
        {
            Add(results, "expedition/arrival eligible meaningful candidate", ExpeditionSocialPolicy.ShouldCreateCandidate("expedition_arrived"));
            Add(results, "expedition/started normally silent", !ExpeditionSocialPolicy.ShouldCreateCandidate("expedition_started"));
            Add(results, "expedition/departed normally silent", !ExpeditionSocialPolicy.ShouldCreateCandidate("expedition_departed"));
            Add(results, "expedition/zone entered structural", !ExpeditionSocialPolicy.ShouldCreateCandidate("expedition_zone_entered"));
            Add(results, "expedition/resumed normally silent", !ExpeditionSocialPolicy.ShouldCreateCandidate("expedition_resumed"));
            Add(results, "expedition/combat interruption may react", ExpeditionSocialPolicy.ShouldCreateCandidate("expedition_combat_interrupted"));

            SemanticEventDeduplicator dedupe = new SemanticEventDeduplicator();
            DateTime now = new DateTime(2026, 8, 10, 17, 0, 0, DateTimeKind.Utc);
            bool first = dedupe.ShouldAccept("expedition_resumed", "Expedition resumed after combat.", now);
            bool second = dedupe.ShouldAccept("expedition_resumed", "Expedition resumed and moving again.", now.AddSeconds(10));
            Add(results, "expedition/repeated resume semantic events deduplicated", first && !second);

            bool oneCampSemantic = !CampSemanticAuthority.ShouldEmitLegacyCampStart(true, true) &&
                string.Equals(CampSemanticAuthority.CanonicalCampStartType(true), "hunt_camp_start", StringComparison.Ordinal) &&
                CampSemanticAuthority.ShouldEmitLegacyCampStart(false, false);
            Add(results, "camp/Campmaster owns equivalent camp start with legacy fallback", oneCampSemantic);
        }

        private static void TestRelax(List<string> results)
        {
            List<string> policy = RelaxSocialPolicy.RunSelfTests();
            for (int i = 0; i < policy.Count; i++) results.Add(policy[i]);

            SocialBudget budget = new SocialBudget();
            budget.SetPreset(SocialActivityPreset.Normal);
            SocialDowntimeContext.SetRelaxActive(false);
            int normalBudget = budget.Profile.MessagesPerTenMinutes;
            SocialDowntimeContext.SetRelaxActive(true);
            SocialBudgetProfile relaxProfile = budget.Profile;
            Add(results, "relax/central budget is roomier only during explicit Relax",
                relaxProfile.MessagesPerTenMinutes > normalBudget && relaxProfile.EventTypeCooldownSeconds < 90.0);
            SocialDowntimeContext.SetRelaxActive(false);

            SimSnapshot sim = new SimSnapshot { Name = "Dancer", ClassName = "Windblade", Level = 20 };
            WorldSnapshot world = new WorldSnapshot();
            world.Scene = "Brasse";
            world.Party = new List<SimSnapshot> { sim };
            world.Player = new PlayerSnapshot { Name = "Player", Level = 20, ClassName = "Paladin" };
            SimMemory memory = new SimMemory();
            memory.Normalize();
            memory.Name = "Dancer";
            string situation = RelaxSocialPolicy.BuildSituation("class_role_preferences", world.Scene, string.Empty);
            List<ChatMessage> prompt = PromptBuilder.BuildAutonomous(sim, memory, world, situation, null, null, false);
            string all = string.Empty;
            for (int i = 0; prompt != null && i < prompt.Count; i++) if (prompt[i] != null) all += "\n" + prompt[i].content;
            Add(results, "relax/prompt enters explicit Relax social mode",
                all.IndexOf("RELAX SOCIAL MODE", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void TestPromptRobustness(List<string> results)
        {
            SimSnapshot sim = new SimSnapshot { Name = "Dancer", ClassName = "Windblade", Level = 20 };
            WorldSnapshot world = new WorldSnapshot();
            world.Scene = "Vitheo";
            world.Party = new List<SimSnapshot> { sim };
            world.Player = new PlayerSnapshot { Name = "Player", Level = 20, ClassName = "Paladin" };
            SimMemory memory = new SimMemory();
            memory.Normalize();
            memory.Name = "Dancer";
            List<ChatMessage> prompt = PromptBuilder.Build(sim, memory, world, "hey", 2, null);
            string system = prompt == null || prompt.Count == 0 || prompt[0] == null ? string.Empty : prompt[0].content;
            Add(results, "prompt/no catchy forbidden random-class example", system.IndexOf("random druid", StringComparison.OrdinalIgnoreCase) < 0);
            Add(results, "prompt/verified self-class instruction present", system.IndexOf("verified class", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void TestMemorySalience(List<string> results)
        {
            SimSnapshot sim = new SimSnapshot { Name = "Fiora", ClassName = "Stormcaller", Level = 19 };
            WorldSnapshot world = new WorldSnapshot { Scene = "Hidden", Party = new List<SimSnapshot> { sim } };
            SimMemory memory = new SimMemory(); memory.Normalize(); memory.Name = sim.Name;
            string legacy = "The player defeated the off-map PvP party led by Rook in Hidden.";
            memory.ImportantMemories.Add(legacy);
            SocialIntent unrelated = new SocialIntent("seed", "memory:level", 1, 1,
                "briefly mention the selected moment", "The player just reached level 19.", sim.Name);
            List<ChatMessage> unrelatedPrompt = PromptBuilder.BuildAutonomous(sim, memory, world,
                "CURRENT SUMMARY: Rook was mentioned earlier.", null, null, false, unrelated);
            string unrelatedText = unrelatedPrompt == null || unrelatedPrompt.Count == 0 || unrelatedPrompt[0] == null
                ? string.Empty : unrelatedPrompt[0].content;
            Add(results, "memory/unrelated legacy important remains stored and seed-eligible",
                memory.ImportantMemories.Contains(legacy) && AmbientSeedProducers.BuildSharedMemoryCandidates(sim, memory, 4, DateTime.UtcNow, string.Empty).Count > 0);
            Add(results, "memory/unrelated autonomous seed omits legacy named actor",
                unrelatedText.IndexOf("Rook", StringComparison.OrdinalIgnoreCase) < 0);

            SocialIntent selected = new SocialIntent("seed", "memory:rook", 1, 1,
                "briefly mention the selected moment", legacy, sim.Name);
            string selectedText = JoinPrompt(PromptBuilder.BuildAutonomous(sim, memory, world,
                "CURRENT SUMMARY: quiet downtime.", null, null, false, selected));
            Add(results, "memory/selected legacy seed remains available in prompt", selectedText.IndexOf("Rook", StringComparison.OrdinalIgnoreCase) >= 0);
            string threadText = JoinPrompt(PromptBuilder.BuildPartyThreadReply(sim, memory, world,
                new List<ConversationLine> { new ConversationLine("Player", "what happened with Rook?") }, 2, null));
            Add(results, "memory/thread-relevant legacy memory remains available", threadText.IndexOf("Rook", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void TestReflectionProvenance(List<string> results)
        {
            DateTime now = DateTime.UtcNow;
            List<SessionSocialEvent> simOnly = new List<SessionSocialEvent>
            {
                new SessionSocialEvent { Provenance = SessionEventProvenance.SimSaid, Text = "Are you still mad at Scrubby?" }
            };
            Add(results, "reflection/sim question cannot become player Scrubby belief",
                !ReflectionProvenanceGuard.Allows("Player confirmed no longer holding a grudge against Scrubby.", simOnly));
            Add(results, "reflection/sim question cannot create player rain preference",
                !ReflectionProvenanceGuard.Allows("Player likes rain.", new List<SessionSocialEvent> { new SessionSocialEvent { Provenance = SessionEventProvenance.SimSaid, Text = "You love rain, don't you?" } }));
            List<SessionSocialEvent> playerRain = new List<SessionSocialEvent>
            {
                new SessionSocialEvent { Provenance = SessionEventProvenance.PlayerSaid, Text = "I love the rain in this game" }
            };
            Add(results, "reflection/player rain statement permits attributed summary",
                ReflectionProvenanceGuard.Allows("Player likes rain in this game.", playerRain));
            List<SessionSocialEvent> playerScrubby = new List<SessionSocialEvent>
            {
                new SessionSocialEvent { Provenance = SessionEventProvenance.PlayerSaid, Text = "No, I'm not mad at Scrubby anymore." }
            };
            Add(results, "reflection/player Scrubby answer permits summary",
                ReflectionProvenanceGuard.Allows("Player confirmed no longer holding a grudge against Scrubby.", playerScrubby));
            Add(results, "reflection/verified event remains factual without player attribution",
                ReflectionProvenanceGuard.Allows("The party completed the verified PvP event.", simOnly));
        }

        private static string JoinPrompt(List<ChatMessage> prompt)
        {
            string text = string.Empty;
            for (int i = 0; prompt != null && i < prompt.Count; i++) if (prompt[i] != null) text += "\n" + prompt[i].content;
            return text;
        }

        private static void Add(List<string> results, string name, bool pass)
        {
            results.Add("[DeepSims Quality] " + name + ": " + (pass ? "PASS" : "FAIL"));
        }
    }
}
