using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    internal static class LiveSocialQualityDeterministicTests
    {
        internal static List<string> Run()
        {
            List<string> lines = new List<string>();

            // Practice Duel v4 adapter / correlation.
            lines.Add(Line("Duel exact v4 accepted", DuelV4ContractPolicy.AcceptsContract(4)));
            lines.Add(Line("Duel future version fails soft", !DuelV4ContractPolicy.AcceptsContract(5)));
            string duelKey = DuelV4ContractPolicy.Fingerprint("req-1", "duel-1", "duel_completed", "", "completed", "player", "opponent", "yield");
            lines.Add(Line("Duel RequestId/DuelId fingerprint stable", duelKey == DuelV4ContractPolicy.Fingerprint("req-1", "duel-1", "duel_completed", "", "completed", "player", "opponent", "yield")));
            lines.Add(Line("DuelId distinguishes terminal events", duelKey != DuelV4ContractPolicy.Fingerprint("req-1", "duel-2", "duel_completed", "", "completed", "player", "opponent", "yield")));
            string duelFact = DuelV4ContractPolicy.BuildCompletedFact("player", "Fiorana", "Fiorana", "player", "opponent", "Brinon");
            lines.Add(Line("Duel winner/yield factual wording", duelFact == "Brinon defeated Fiorana in a friendly Practice Duel. Fiorana yielded."));
            VerifiedDuelEvent duelLanguage;
            VerifiedDuelEvent.TryCreate("duel_completed", "Fiorana", "party", "accepted", "yield", "player", "opponent", "yield", "", out duelLanguage);
            lines.Add(Line("duel prompt context avoids non-lethal engineering phrase", duelLanguage != null && duelLanguage.VerifiedContext().IndexOf("non-lethal", StringComparison.OrdinalIgnoreCase) < 0));
            lines.Add(Line("loaded same-scene witness admitted", SocialWitnessPolicy.IsLocalSceneWitness(true, "Azure", "Azure")));
            lines.Add(Line("off-map witness rejected", !SocialWitnessPolicy.IsLocalSceneWitness(true, "Brake", "Azure")));
            lines.Add(Line("unloaded witness rejected", !SocialWitnessPolicy.IsLocalSceneWitness(false, "Azure", "Azure")));
            lines.Add(Line("missing witness scene fails closed", !SocialWitnessPolicy.IsLocalSceneWitness(true, "", "Azure") && !SocialWitnessPolicy.IsLocalSceneWitness(true, "Azure", "")));

            // Campmaster factual v1 living rows.
            List<Dictionary<string, string>> campRows = new List<Dictionary<string, string>>
            {
                new Dictionary<string, string>
                {
                    { "sequence", "17" }, { "eventId", "living-17" }, { "source", "ErenshorCampmaster" },
                    { "livingContractVersion", "1" }, { "sessionId", "camp-session" }, { "type", "camp_watch_event" },
                    { "participantName", "Phanty" }, { "meaningful", "true" },
                    { "detail", "Phanty noticed signs of movement near camp while on watch." }
                }
            };
            List<CampEventFact> camp = CampmasterBridge.ParseEvents(campRows);
            lines.Add(Line("Camp watch factual event parsed", camp.Count == 1 && camp[0].Type == "camp_watch_event" && camp[0].ParticipantName == "Phanty"));
            lines.Add(Line("Camp living contract v1 retained", camp.Count == 1 && camp[0].ContractVersion == 1 && camp[0].SessionId == "camp-session"));
            lines.Add(Line("Camp event does not invent attacker", camp.Count == 1 && camp[0].Detail.IndexOf("wolf", StringComparison.OrdinalIgnoreCase) < 0 && camp[0].Detail.IndexOf("attack", StringComparison.OrdinalIgnoreCase) < 0));
            lines.Add(Line("Camp missing rows fail soft", CampmasterBridge.ParseEvents(null).Count == 0));

            // Persistent fictional MMO player background.
            SimSnapshot sim = new SimSnapshot { Key = "fiorana-key", Name = "Fiorana", ClassName = "Druid", Scene = "Azure" };
            SimMemory memory = new SimMemory { SimKey = sim.Key, Name = sim.Name, AuthoredIdentity = new AuthoredIdentityProfile() };
            memory.Normalize();
            bool created = SimulatedPlayerBackgroundPolicy.Ensure(memory, sim);
            string generated = memory.GeneratedSimulatedPlayerBackground;
            bool stable = !SimulatedPlayerBackgroundPolicy.Ensure(memory, sim) && memory.GeneratedSimulatedPlayerBackground == generated;
            lines.Add(Line("MMO generated background created once", created && !string.IsNullOrWhiteSpace(generated)));
            lines.Add(Line("MMO generated background stable", stable));
            bool motivationsCreated = GeneratedIdentityMotivationPolicy.Ensure(memory, sim);
            string generatedWants = memory.GeneratedLongTermWants;
            string generatedCares = memory.GeneratedCaresAbout;
            lines.Add(Line("generated wants and cares are complete", motivationsCreated &&
                !string.IsNullOrWhiteSpace(generatedWants) && !string.IsNullOrWhiteSpace(generatedCares)));
            lines.Add(Line("generated wants and cares are stable", !GeneratedIdentityMotivationPolicy.Ensure(memory, sim) &&
                memory.GeneratedLongTermWants == generatedWants && memory.GeneratedCaresAbout == generatedCares));
            IdentityPromptContext motivations = IdentityContextPolicy.Select(sim, memory, "what matters to you?", false);
            lines.Add(Line("generated motivations reach prompt without editor", motivations.LongTermWants == generatedWants &&
                motivations.CaresAbout == generatedCares && motivations.LongTermWantsSource == IdentityValueSource.PersistedGeneratedDefault &&
                motivations.CaresAboutSource == IdentityValueSource.PersistedGeneratedDefault));
            IdentityPromptContext roleplayMotivations = IdentityContextPolicy.Select(sim, memory, "what matters to you?", true);
            lines.Add(Line("roleplay retains character motivations but hides MMO background",
                roleplayMotivations.LongTermWants == generatedWants && roleplayMotivations.CaresAbout == generatedCares &&
                string.IsNullOrWhiteSpace(roleplayMotivations.PersonalBackground)));
            SimSnapshot sameClass = new SimSnapshot { Key = "another-druid-key", Name = "Another", ClassName = "Druid", Scene = "Azure" };
            DefaultIdentityProfile firstBundle = DefaultIdentityPolicy.Build(sim);
            DefaultIdentityProfile secondBundle = DefaultIdentityPolicy.Build(sameClass);
            lines.Add(Line("same-class identities retain stable variation", firstBundle.LongTermWants != secondBundle.LongTermWants ||
                firstBundle.CaresAbout != secondBundle.CaresAbout || firstBundle.RelationshipToPlayer != secondBundle.RelationshipToPlayer));
            SimSnapshot mutableFriend = new SimSnapshot { Key = sim.Key, Name = sim.Name, ClassName = sim.ClassName, FriendStateKnown = true, IsFriend = true };
            string friendRelationship = DefaultIdentityPolicy.Build(mutableFriend).RelationshipToPlayer;
            mutableFriend.IsFriend = false;
            lines.Add(Line("generated relationship excludes mutable Friend state",
                friendRelationship == DefaultIdentityPolicy.Build(mutableFriend).RelationshipToPlayer &&
                friendRelationship.IndexOf("verified Erenshor friend", StringComparison.OrdinalIgnoreCase) < 0));
            IdentityPromptContext mmo = IdentityContextPolicy.Select(sim, memory, "how are things IRL?", false);
            lines.Add(Line("IRL question uses simulated-player layer", mmo.PersonalBackground == generated && mmo.PersonalBackgroundSource == IdentityValueSource.PersistedGeneratedDefault));
            memory.AuthoredIdentity.PersonalBackground = "Works a quiet daytime schedule and mostly plays on weekends.";
            IdentityPromptContext authored = IdentityContextPolicy.Select(sim, memory, "how was work?", false);
            lines.Add(Line("authored player background wins", authored.PersonalBackground.IndexOf("quiet daytime", StringComparison.OrdinalIgnoreCase) >= 0 && authored.PersonalBackgroundSource == IdentityValueSource.AuthoredOverride));
            IdentityPromptContext roleplay = IdentityContextPolicy.Select(sim, memory, "how was work?", true);
            lines.Add(Line("Roleplay mode hides MMO background", string.IsNullOrWhiteSpace(roleplay.PersonalBackground)));
            SimMemory unknown = new SimMemory { AuthoredIdentity = new AuthoredIdentityProfile() }; unknown.Normalize();
            lines.Add(Line("background generator fails conservative unknown", !SimulatedPlayerBackgroundPolicy.Ensure(unknown, null)));

            // Prompt-facing voice is a separate rendering of stored/editor identity.
            IdentityPromptContext voiceContext = IdentityContextPolicy.Select(sim, memory, "what matters to you and how is work?", false);
            string voice = PromptIdentityRenderer.Render(sim, voiceContext, "Brinon", false);
            string boundaries = PromptIdentityRenderer.FactualBoundaries();
            lines.Add(Line("prompt identity uses direct named speaker framing", voice.Contains("YOU ARE FIORANA.")));
            lines.Add(Line("prompt identity keeps factual boundaries separate", voice.IndexOf("FACTUAL BOUNDARIES", StringComparison.OrdinalIgnoreCase) < 0 && boundaries.StartsWith("FACTUAL BOUNDARIES")));
            lines.Add(Line("prompt identity includes generated wants and cares", voice.Contains(generatedWants) && voice.Contains(generatedCares)));
            lines.Add(Line("prompt personality avoids engineering vocabulary", !ContainsAny(voice, "verified", "grounded", "grounding", "provenance", "current zone", "authority", "admissible", "non-lethal")));
            lines.Add(Line("factual guardrail remains explicit", boundaries.IndexOf("Only state", StringComparison.OrdinalIgnoreCase) >= 0 && boundaries.IndexOf("CURRENT SOCIAL PRESENCE", StringComparison.Ordinal) >= 0));
            lines.Add(Line("voice permits opinions disagreement and quiet", voice.IndexOf("disagree", StringComparison.OrdinalIgnoreCase) >= 0 && voice.IndexOf("stay quiet", StringComparison.OrdinalIgnoreCase) >= 0));
            string editorCoreBefore = DefaultIdentityPolicy.Build(sim).CorePersonality;
            PromptIdentityRenderer.Render(sim, voiceContext, "Brinon", false);
            lines.Add(Line("prompt rendering does not rewrite editor metadata", DefaultIdentityPolicy.Build(sim).CorePersonality == editorCoreBefore));
            DefaultIdentityProfile stableA = DefaultIdentityPolicy.Build(sim);
            DefaultIdentityProfile stableB = DefaultIdentityPolicy.Build(sim);
            lines.Add(Line("same stable Sim has same personality dimensions", DimensionSignature(stableA.Dimensions) == DimensionSignature(stableB.Dimensions)));
            lines.Add(Line("same-class different Sims can differ in dimensions", DimensionSignature(firstBundle.Dimensions) != DimensionSignature(secondBundle.Dimensions)));
            lines.Add(Line("generated dimensions contain no sensitive inferred canon", !ContainsAny(voice, "mental illness", "trauma", "politics", "religion", "sexuality", "criminal", "diagnosis", "family history")));
            lines.Add(Line("generated dimensions contain no unsupported history", !DefaultIdentityPolicy.ContainsUnsupportedSpecificHistory(voice)));
            lines.Add(Line("disagreement social energy and humor reach prompt", voice.IndexOf("disagree", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (voice.IndexOf("social", StringComparison.OrdinalIgnoreCase) >= 0 || voice.IndexOf("quiet", StringComparison.OrdinalIgnoreCase) >= 0) && voice.IndexOf("humor", StringComparison.OrdinalIgnoreCase) >= 0));

            memory.AuthoredIdentity.CorePersonality = "Thoughtful, precise, and fond of dry jokes.";
            IdentityPromptContext authoredVoiceContext = IdentityContextPolicy.Select(sim, memory, "how was work and what are you like?", false);
            string authoredVoice = PromptIdentityRenderer.Render(sim, authoredVoiceContext, "Brinon", false);
            lines.Add(Line("authored personality wins in prompt voice", authoredVoice.Contains("Thoughtful, precise") && authoredVoiceContext.CorePersonalitySource == IdentityValueSource.AuthoredOverride));
            memory.AuthoredIdentity.CorePersonality = string.Empty;
            string roleplayVoice = PromptIdentityRenderer.Render(sim, IdentityContextPolicy.Select(sim, memory, "how is work?", true), "Brinon", true);
            lines.Add(Line("Roleplay voice excludes fictional MMO background", roleplayVoice.IndexOf("quiet daytime", StringComparison.OrdinalIgnoreCase) < 0));
            lines.Add(Line("MMO voice includes relevant fictional player background", authoredVoice.IndexOf("quiet daytime", StringComparison.OrdinalIgnoreCase) >= 0));

            WorldSnapshot promptWorld = TestWorld(sim, sameClass, "Brinon", "Azure");
            List<ConversationLine> promptThread = new List<ConversationLine> { new ConversationLine { Speaker = "Brinon", Text = "what do you care about?" } };
            string compactPrompt = JoinMessages(PromptBuilder.BuildCompactDirectPartyReply(sim, memory, promptWorld, promptThread, null, null, string.Empty));
            lines.Add(Line("actual compact PromptBuilder uses voice renderer", compactPrompt.Contains("WHO YOU ARE") && compactPrompt.Contains("YOU ARE FIORANA.")));
            lines.Add(Line("actual compact prompt separates factual boundaries", compactPrompt.Contains("FACTUAL BOUNDARIES") && compactPrompt.Contains("CURRENT SOCIAL PRESENCE")));

            // Addressability: selection requires current loaded/same-scene native party presence;
            // remembered subjects remain discussion-only, and completion revalidation rejects departure.
            lines.Add(Line("current nearby native party Sim addressable", LiveSocialAddressability.IsAddressable(true, "Azure", "Azure", true)));
            lines.Add(Line("current party without physical actor denied", !LiveSocialAddressability.IsAddressable(false, "Azure", "Azure", true)));
            lines.Add(Line("previous-scene actor denied", !LiveSocialAddressability.IsAddressable(true, "Brake", "Azure", true)));
            lines.Add(Line("loaded non-party actor not addressable under current design", !LiveSocialAddressability.IsAddressable(true, "Azure", "Azure", false)));
            List<string> rememberedNames = new List<string> { "Dullin", "Fiora", "PracticeDuelSim" };
            List<string> currentNames = new List<string> { "Fiora" };
            string staleName;
            lines.Add(Line("completed PvP opponent remains discussable as memory subject", !LiveSocialAddressability.HasStaleDirectAddress("Dullin's team gave us a good fight.", rememberedNames, currentNames, "Brinon", "Fiora", out staleName)));
            lines.Add(Line("completed PvP opponent direct addressee denied", LiveSocialAddressability.HasStaleDirectAddress("Dullin, what do you like to eat?", rememberedNames, currentNames, "Brinon", "Fiora", out staleName) && staleName == "Dullin"));
            lines.Add(Line("Practice Duel disappeared participant denied", LiveSocialAddressability.HasStaleDirectAddress("PracticeDuelSim, do you want another?", rememberedNames, currentNames, "Brinon", "Fiora", out staleName)));
            List<string> initiallyPresent = new List<string> { "Fiora", "Dullin" };
            lines.Add(Line("target valid at request creation", !LiveSocialAddressability.HasStaleDirectAddress("Dullin, how do you prepare?", rememberedNames, initiallyPresent, "Brinon", "Fiora", out staleName)));
            lines.Add(Line("target invalid before completion discarded", LiveSocialAddressability.HasStaleDirectAddress("Dullin, how do you prepare?", rememberedNames, currentNames, "Brinon", "Fiora", out staleName)));
            lines.Add(Line("current legitimate Sim remains directly addressable", !LiveSocialAddressability.HasStaleDirectAddress("Fiora, how do you prepare?", rememberedNames, currentNames, "Brinon", "Phanty", out staleName)));

            CampEventFact genericDisagreement = new CampEventFact { Type = "camp_minor_disagreement", ParticipantName = "Fiora" };
            CampEventFact specificDisagreement = new CampEventFact { Type = "camp_minor_disagreement", ParticipantName = "Fiora", Detail = "Fiora had a minor disagreement with the group about whether camp preparations were sufficient.", Counterpart = "group", SubjectCategory = "preparation", SubjectSource = "current_preparation_state", PresentationCategory = "warning" };
            lines.Add(Line("camp disagreement absent subject stays generic", CampLivingEventSemantics.FactualSummary(genericDisagreement) == "Fiora had a minor disagreement with the group."));
            lines.Add(Line("camp one-participant schema uses group counterpart", CampLivingEventSemantics.FactualSummary(genericDisagreement).Contains("with the group")));
            lines.Add(Line("camp supplied subject preserved verbatim", CampLivingEventSemantics.FactualSummary(specificDisagreement) == specificDisagreement.Detail));
            lines.Add(Line("camp structured subject is additive and exact", specificDisagreement.Counterpart == "group" && specificDisagreement.SubjectCategory == "preparation" && specificDisagreement.SubjectSource == "current_preparation_state"));
            lines.Add(Line("camp disagreement presentation category retained", specificDisagreement.PresentationCategory == "warning"));
            lines.Add(Line("Deep Sims does not strengthen Campmaster fact", CampLivingEventSemantics.FactualSummary(specificDisagreement).IndexOf("route", StringComparison.OrdinalIgnoreCase) < 0 && CampLivingEventSemantics.FactualSummary(specificDisagreement).IndexOf("attack", StringComparison.OrdinalIgnoreCase) < 0));

            lines.Add(Line("missing activity setting resolves Lively", DeepSimsControlPolicy.ActivityOrDefault(null) == "Lively"));
            lines.Add(Line("explicit Quiet Normal Lively settings preserved", DeepSimsControlPolicy.ActivityOrDefault("Quiet") == "Quiet" &&
                DeepSimsControlPolicy.ActivityOrDefault("Normal") == "Normal" && DeepSimsControlPolicy.ActivityOrDefault("Lively") == "Lively"));

            // Production-path regression: first prompt read creates/persists the generated default;
            // opening the editor later observes the same value, authored wins, and Reset restores it.
            string identityRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DeepSimsFreshIdentity-" + Guid.NewGuid().ToString("N"));
            try
            {
                MemoryStore store = new MemoryStore(identityRoot, NullDeepSimsLog.Instance);
                SimSnapshot freshSim = new SimSnapshot { Key = "fresh-no-editor", Name = "Fresh", ClassName = "Druid", Scene = "Azure" };
                SimMemory firstPrompt = store.LoadForPrompt(freshSim);
                string firstGenerated = firstPrompt.GeneratedSimulatedPlayerBackground;
                IdentityPromptContext firstContext = IdentityContextPolicy.Select(freshSim, firstPrompt, "what do you do IRL?", false);
                IdentityEditorModel editor = store.GetIdentityEditorModel(freshSim);
                lines.Add(Line("fresh Sim prompt establishes generated background without editor interaction",
                    !string.IsNullOrWhiteSpace(firstGenerated) && firstContext.PersonalBackground == firstGenerated));
                lines.Add(Line("editor observes the same prompt-established generated background",
                    editor.Background.EffectiveText == firstGenerated && editor.Background.Source == IdentityValueSource.PersistedGeneratedDefault));
                string firstWants = firstPrompt.GeneratedLongTermWants;
                string firstCares = firstPrompt.GeneratedCaresAbout;
                lines.Add(Line("fresh prompt persists generated motivations before editor",
                    !string.IsNullOrWhiteSpace(firstWants) && !string.IsNullOrWhiteSpace(firstCares) &&
                    editor.Wants.EffectiveText == firstWants && editor.Cares.EffectiveText == firstCares &&
                    editor.Wants.Source == IdentityValueSource.PersistedGeneratedDefault && editor.Cares.Source == IdentityValueSource.PersistedGeneratedDefault));

                string saveResult;
                AuthoredIdentityProfile overrideProfile = new AuthoredIdentityProfile
                {
                    PersonalBackground = "Studies marine biology during the week.",
                    LongTermWants = "Wants to make every expedition feel welcoming."
                };
                bool saved = store.TrySaveAuthoredIdentity(freshSim, overrideProfile, out saveResult);
                IdentityEditorModel authoredEditor = store.GetIdentityEditorModel(freshSim);
                string resetResult;
                bool reset = store.TryResetAllAuthoredIdentity(freshSim, out resetResult);
                IdentityEditorModel resetEditor = store.GetIdentityEditorModel(freshSim);
                lines.Add(Line("authored background overrides generated background in editor",
                    saved && authoredEditor.Background.Source == IdentityValueSource.AuthoredOverride && authoredEditor.Background.EffectiveText.IndexOf("marine biology", StringComparison.OrdinalIgnoreCase) >= 0));
                lines.Add(Line("one authored motivation leaves unrelated generated fields unchanged",
                    authoredEditor.Wants.Source == IdentityValueSource.AuthoredOverride &&
                    authoredEditor.Cares.Source == IdentityValueSource.PersistedGeneratedDefault && authoredEditor.Cares.EffectiveText == firstCares &&
                    authoredEditor.Background.DefaultText == firstGenerated));
                lines.Add(Line("Reset restores the original persisted generated background",
                    reset && resetEditor.Background.Source == IdentityValueSource.PersistedGeneratedDefault && resetEditor.Background.EffectiveText == firstGenerated));
                lines.Add(Line("Reset restores original generated wants and cares",
                    resetEditor.Wants.Source == IdentityValueSource.PersistedGeneratedDefault && resetEditor.Wants.EffectiveText == firstWants &&
                    resetEditor.Cares.Source == IdentityValueSource.PersistedGeneratedDefault && resetEditor.Cares.EffectiveText == firstCares));
                SimMemory afterReset = store.LoadForPrompt(freshSim);
                IdentityPromptContext resetRoleplay = IdentityContextPolicy.Select(freshSim, afterReset, "what do you do IRL?", true);
                lines.Add(Line("fresh generated background remains absent from Roleplay prompt context",
                    string.IsNullOrWhiteSpace(resetRoleplay.PersonalBackground)));
                store.FlushPending(true);
                store.Shutdown();

                MemoryStore reloadStore = new MemoryStore(identityRoot, NullDeepSimsLog.Instance);
                SimMemory persisted = reloadStore.LoadForPrompt(freshSim);
                lines.Add(Line("prompt-established generated background persists unchanged",
                    persisted.GeneratedSimulatedPlayerBackground == firstGenerated));
                lines.Add(Line("prompt-established generated motivations persist unchanged",
                    persisted.GeneratedLongTermWants == firstWants && persisted.GeneratedCaresAbout == firstCares));
                reloadStore.Shutdown();
            }
            finally
            {
                try { if (System.IO.Directory.Exists(identityRoot)) System.IO.Directory.Delete(identityRoot, true); } catch { }
            }

            // Input evidence dedupe keeps recurrence without repeated prompt evidence.
            ConversationEvidenceDedupe dedupe = new ConversationEvidenceDedupe(30.0, 8);
            DateTime t = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
            ConversationEvidenceDecision d1 = dedupe.Observe("Phanty", "Preparing utility spell; stay nearby until it ends.", t);
            ConversationEvidenceDecision d2 = dedupe.Observe("Phanty", "Preparing utility spell; stay nearby until it ends.", t.AddSeconds(2));
            ConversationEvidenceDecision d3 = dedupe.Observe("Phanty", "Preparing utility spell, stay nearby until it ends", t.AddSeconds(4));
            lines.Add(Line("identical line canonicalized once", d1.IsNewCanonical && d2.IsRepeat && !d2.IsNewCanonical));
            lines.Add(Line("near-identical line deduped", d3.IsRepeat));
            lines.Add(Line("recurrence count retained", d3.RecurrenceCount >= 3 && dedupe.DedupeCount >= 2));

            // Retained UI normalized layout policy.
            float[,] sizes = { {1280,720}, {1600,900}, {1920,1080}, {2560,1440} };
            bool layoutOk = true;
            for (int i = 0; i < sizes.GetLength(0); i++)
            {
                IdentityEditorLayoutMetrics m = IdentityEditorLayoutPolicy.Compute(sizes[i,0], sizes[i,1]);
                layoutOk &= m.Left >= 0 && m.Bottom >= 0 && m.Right <= sizes[i,0] && m.Top <= sizes[i,1] && m.Width > 600 && m.Height > 500;
            }
            lines.Add(Line("identity UI resolution math stays onscreen", layoutOk));

            // Latency policy: direct replies may retry once; background chatter cannot spend a second semantic model call.
            lines.Add(Line("direct group grounding retry allowed", InferencePriorityPolicy.AllowsGroundingRetry("group", true)));
            lines.Add(Line("autonomous grounding retry bounded to zero", !InferencePriorityPolicy.AllowsGroundingRetry("autonomous", false)));
            lines.Add(Line("verified-event grounding retry bounded to zero", !InferencePriorityPolicy.AllowsGroundingRetry("verified_event", false)));
            lines.Add(Line("empty curation does no model work", !InferencePriorityPolicy.ShouldRunCuration(0)));
            lines.Add(Line("meaningful pending curation may run", InferencePriorityPolicy.ShouldRunCuration(1)));

            return lines;
        }

        private static string DimensionSignature(GeneratedPersonalityDimensions d)
        {
            return d.SocialEnergy + "|" + d.RiskTolerance + "|" + d.Improvisation + "|" + d.Competitiveness + "|" + d.Curiosity + "|" +
                d.Patience + "|" + d.WillingnessToDisagree + "|" + d.Directness + "|" + d.GroupOrientation + "|" + d.HumorStyle + "|" + d.PreferredInterests;
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            for (int i = 0; i < values.Length; i++) if ((text ?? string.Empty).IndexOf(values[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static string JoinMessages(List<ChatMessage> messages)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < messages.Count; i++) if (messages[i] != null) sb.AppendLine(messages[i].content);
            return sb.ToString();
        }

        private static WorldSnapshot TestWorld(SimSnapshot first, SimSnapshot second, string player, string scene)
        {
            first.PartyActorId = "sim:first"; first.RuntimeSim = null;
            second.PartyActorId = "sim:second"; second.RuntimeSim = null;
            List<LivePartyActorFacts> actors = new List<LivePartyActorFacts>
            {
                new LivePartyActorFacts(first.PartyActorId, first.Name, LivePartyActorKind.LocalSim, LivePartyStatus.CurrentPartyMember, KnownTruth.True, KnownTruth.True, "test"),
                new LivePartyActorFacts(second.PartyActorId, second.Name, LivePartyActorKind.LocalSim, LivePartyStatus.CurrentPartyMember, KnownTruth.True, KnownTruth.True, "test")
            };
            LivePartyActorFacts local = new LivePartyActorFacts("player", player, LivePartyActorKind.LocalHuman, LivePartyStatus.CurrentPartyMember, KnownTruth.True, KnownTruth.True, "test");
            return new WorldSnapshot { Scene = scene, Player = new PlayerSnapshot { Name = player }, Party = new List<SimSnapshot> { first, second },
                LiveParty = new LivePartyFacts(1, DateTime.UtcNow, 1, LivePartyMembershipState.Confirmed, "test", local, actors, "test") };
        }

        private static string Line(string name, bool pass)
        {
            return "[DeepSims LiveSocial " + (pass ? "PASS" : "FAIL") + "] " + name;
        }
    }
}
