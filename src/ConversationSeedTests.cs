using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    // Deterministic, no-Ollama coverage for ambient seed selection, topic fatigue, and silence.
    // Every test drives the selector through an explicit clock value so nothing depends on wall time.
    internal static class ConversationSeedTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 8, 10, 18, 0, 0, DateTimeKind.Utc);

        internal static List<string> Run()
        {
            List<string> results = new List<string>();

            Add(results, "nothing happened -> silence wins", NothingHappenedPrefersSilence);
            Add(results, "recently used idle topic is heavily suppressed", RecentIdleIsSuppressed);
            Add(results, "fresh meaningful subject beats idle", MeaningfulBeatsIdle);
            Add(results, "repeated subject accumulates fatigue", RepeatedTopicAccumulatesFatigue);
            Add(results, "semantic waiting variants map to idle_waiting", SemanticIdleVariants);
            Add(results, "camp excludes generic waiting commentary", CampSuppressesWaiting);
            Add(results, "no valid seed produces no subject and no anecdote", NoValidSeedNoAnecdote);
            Add(results, "topic usage recorded only after emitted output", UsageOnlyAfterEmit);
            Add(results, "budget suppression does not consume the topic", SuppressionDoesNotConsumeTopic);
            Add(results, "expired candidate cannot win", ExpiredCandidateCannotWin);
            Add(results, "fixed input produces identical scoring and diagnostics", DeterministicScoring);
            Add(results, "active conversation excludes its own subject", ActiveConversationExcludesTopic);
            Add(results, "unsourced fact is excluded rather than spoken", UnsourcedFactExcluded);
            Add(results, "fresh high-importance seed beats silence", FreshImportantSeedBeatsSilence);
            Add(results, "repeated important seed becomes fatigued", RepeatedImportantSeedFatigues);
            Add(results, "authoritative low resource beats generic idle at camp", LowResourceBeatsIdle);
            Add(results, "recovered resource invalidates the low-resource seed", RecoveryInvalidatesSeed);
            Add(results, "unsourced resource reading is refused", UnsourcedResourceRefused);
            Add(results, "personality shifts affinity without creating facts", PersonalityIsFlavorOnly);
            Add(results, "quiet preset raises the effective silence threshold", QuietPresetRaisesSilence);
            Add(results, "pending verified event owns the social moment", PendingEventOwnsMoment);
            Add(results, "shared memory seed is restricted to its owning Sim", SharedMemoryScopedToOwner);
            Add(results, "pinned shared memory requires relevance and respects KnownBy", PinnedSharedMemoryRequiresRelevanceAndKnowledge);
            Add(results, "exact same pinned memory accrues fatigue", PinnedMemoryAccruesFatigue);
            Add(results, "identity seed is relevance-gated and perspective-aware", IdentitySeedIsRelevantAndPerspectiveAware);
            Add(results, "recent verified competitive event beats generic idle", RecentCompetitiveEventBeatsIdle);
            Add(results, "PvP and Nemesis terminal copies correlate to one episode", CompetitiveTerminalCorrelationCollapsesDuplicate);
            Add(results, "seed diagnostics expose bounded labels without raw private memory", SeedDiagnosticsArePrivacySafe);
            Add(results, "player topic classifier maps known phrases and refuses guesses", PlayerTopicClassifierIsConservative);
            Add(results, "player topic scope is fixed to who was present when said", PlayerTopicScopeFixedAtRecordTime);
            Add(results, "player topic expires and never carries a verified-fact bonus", PlayerTopicExpiresAndUnverified);
            Add(results, "familiarity nudges tone only, never fabricates a candidate", FamiliarityIsToneOnly);
            Add(results, "session_observation does not dominate subject selection", SessionObservationDoesNotDominate);
            Add(results, "spaced ambient opportunities produce subject variety and fatigue-limited repeats", AmbientOpportunitySimulationProducesVariety);
            Add(results, "Relax topics flow through the shared selector", RelaxTopicsFlowThroughSharedSelector);
            Add(results, "Relax verified_outing requires a supplied, sourced fact", RelaxVerifiedOutingRequiresSource);
            Add(results, "Relax silence threshold sits below Normal", RelaxSilenceThresholdIsLowerThanNormal);
            Add(results, "seed requiring another Sim is rejected before generation", OtherSimSeedRequiresAnotherVisibleSim);
            Add(results, "post-generation rejection applies bounded temporary penalty", RejectionPenaltyIsBoundedAndTemporary);
            Add(results, "rejection penalty is scoped to topic and speaker", RejectionPenaltyIsScoped);
            Add(results, "Lively contextual preference can break a close generic tie", ContextualPreferenceBreaksCloseTie);
            Add(results, "each free-social shape remains selectable", FreeSocialShapesRemainSelectable);

            return results;
        }

        // Regression coverage for the "session_observation always wins" live diagnostic problem:
        // across many independent (fresh-fatigue) opportunities with a session fact always available,
        // session_observation must not win more than half of the opportunities where something was
        // said, and at least a few different subjects must be able to win.
        private static string SessionObservationDoesNotDominate()
        {
            const int total = 30;
            int sessionWins = 0;
            int silenceCount = 0;
            HashSet<string> distinctWinners = new HashSet<string>();
            for (int i = 0; i < total; i++)
            {
                long opportunityId = 5000 + i;
                DateTime now = T0.AddSeconds(i * 5.0);
                List<AmbientSeedCandidate> candidates = AmbientSeedProducers.BuildDowntimeCandidates(
                    SocialContextMode.Normal, "The party recorded a kill in Krakengard.",
                    "verified current-session outing telemetry", now);
                // Realistic ambient evaluations only fire once the party has been quiet long enough
                // (see SocialDirector.EvaluateIdlePressure), and pressure ramps from 0 to 1 over that
                // wait rather than sitting at a single fixed value, so vary it across the run.
                double pressure = 0.3 + ((i % 8) / 8.0) * 0.7;
                AmbientSeedDecision decision = Select(opportunityId, SocialContextMode.Normal, candidates,
                    VariedParty(), new TopicFatigueTracker(), 0, now, pressure, 0.0, false);
                if (decision.SilenceWon) { silenceCount++; continue; }
                distinctWinners.Add(decision.SelectedTopicKey);
                if (string.Equals(decision.SelectedTopicKey, AmbientTopics.SessionObservation, StringComparison.Ordinal))
                    sessionWins++;
            }
            int spoken = total - silenceCount;
            if (spoken == 0) return "no opportunity produced speech at all";
            if (sessionWins > spoken / 2)
                return "session_observation won " + sessionWins + "/" + spoken + " spoken opportunities";
            if (distinctWinners.Count < 3)
                return "only " + distinctWinners.Count + " distinct subjects won across " + total + " opportunities";
            if (silenceCount == 0) return "silence never won across " + total + " opportunities";
            return null;
        }

        // The deterministic simulation target requested for subject-variety verification: 30 spaced
        // ambient opportunities sharing one fatigue tracker (mirroring how a live session actually
        // accrues fatigue between real speech events). Verifies: several topic groups can win, back-
        // to-back repeats of the same subject are rare, silence still wins sometimes, and total speech
        // frequency is not simply higher than before (spoken opportunities stay a minority).
        private static string AmbientOpportunitySimulationProducesVariety()
        {
            const int total = 30;
            TopicFatigueTracker fatigue = new TopicFatigueTracker();
            Dictionary<string, int> wins = new Dictionary<string, int>(StringComparer.Ordinal);
            int silenceCount = 0;
            int immediateRepeats = 0;
            string lastTopic = null;
            for (int i = 0; i < total; i++)
            {
                long opportunityId = 6000 + i;
                DateTime now = T0.AddSeconds(i * 90.0);
                List<AmbientSeedCandidate> candidates = AmbientSeedProducers.BuildDowntimeCandidates(
                    SocialContextMode.Normal, "The party recorded a kill in Krakengard.",
                    "verified current-session outing telemetry", now);
                double pressure = 0.3 + ((i % 8) / 8.0) * 0.7;
                AmbientSeedDecision decision = Select(opportunityId, SocialContextMode.Normal, candidates,
                    VariedParty(), fatigue, 0, now, pressure, 0.0, false);
                if (decision.SilenceWon) { silenceCount++; lastTopic = null; continue; }
                if (lastTopic != null && string.Equals(lastTopic, decision.SelectedTopicKey, StringComparison.Ordinal))
                    immediateRepeats++;
                int count;
                wins.TryGetValue(decision.SelectedTopicKey, out count);
                wins[decision.SelectedTopicKey] = count + 1;
                fatigue.NoteUsed(decision.SelectedTopicKey, decision.SelectedCooldownGroup, decision.SelectedSpeaker, 0, now);
                lastTopic = decision.SelectedTopicKey;
            }
            if (wins.Count < 3) return "fewer than 3 distinct subjects won across " + total + " spaced opportunities: " + wins.Count;
            if (immediateRepeats > total / 5) return "too many immediate back-to-back repeats: " + immediateRepeats;
            if (silenceCount == 0) return "silence never won across " + total + " opportunities";
            int spoken = total - silenceCount;
            if (spoken > total * 9 / 10) return "spoke on nearly every opportunity (" + spoken + "/" + total + "); silence lost its role";
            return null;
        }

        // Relax must flow through the same AmbientSeedSelector pipeline (candidate creation, topic
        // scoring, fatigue, silence) rather than a separate unscored chooser.
        private static string RelaxTopicsFlowThroughSharedSelector()
        {
            List<AmbientSeedCandidate> candidates = AmbientSeedProducers.BuildRelaxCandidates(null, null, T0);
            for (int i = 0; i < candidates.Count; i++)
                if (string.Equals(candidates[i].TopicKey, "verified_outing", StringComparison.Ordinal) ||
                    string.Equals(candidates[i].TopicKey, "verified_history", StringComparison.Ordinal))
                    return "unsourced Relax fact topic was still offered";
            if (candidates.Count != RelaxSocialPolicy.TopicIds.Length - 2)
                return "expected every Relax topic except the two fact-only ones, got " + candidates.Count;

            AmbientSeedDecision decision = AmbientSeedSelector.Select(7000, SocialContextMode.Relax, candidates,
                Party(), new TopicFatigueTracker(), 0, T0, AmbientSeedSelector.DefaultSilenceNormal,
                AmbientSeedSelector.DefaultSilenceCamp, AmbientSeedSelector.DefaultSilenceRelax, 0.0, 0.0,
                false, true, null);
            if (decision.Mode != SocialContextMode.Relax) return "decision did not record Relax context mode";
            return null;
        }

        private static string RelaxVerifiedOutingRequiresSource()
        {
            List<AmbientSeedCandidate> withFact =
                AmbientSeedProducers.BuildRelaxCandidates("The party looted a rare trinket.", null, T0);
            bool found = false;
            for (int i = 0; i < withFact.Count; i++)
            {
                if (!string.Equals(withFact[i].TopicKey, "verified_outing", StringComparison.Ordinal)) continue;
                found = true;
                if (!withFact[i].HasFact || withFact[i].FactSource.Length == 0)
                    return "verified_outing candidate lost its fact source";
            }
            if (!found) return "verified_outing candidate missing when a fact was supplied";

            List<AmbientSeedCandidate> withoutFact = AmbientSeedProducers.BuildRelaxCandidates(null, null, T0);
            for (int i = 0; i < withoutFact.Count; i++)
                if (string.Equals(withoutFact[i].TopicKey, "verified_outing", StringComparison.Ordinal))
                    return "verified_outing candidate offered with no fact supplied";
            return null;
        }

        private static string RelaxSilenceThresholdIsLowerThanNormal()
        {
            double relax = AmbientSeedSelector.DefaultSilenceRelax;
            double normal = AmbientSeedSelector.DefaultSilenceNormal;
            if (!(relax < normal))
                return "Relax silence threshold is not lower than Normal";
            return null;
        }

        // 1. A long quiet stretch with no verified facts must be allowed to produce nothing.
        private static string NothingHappenedPrefersSilence()
        {
            AmbientSeedDecision decision = Select(1, SocialContextMode.Normal,
                AmbientSeedProducers.BuildDowntimeCandidates(SocialContextMode.Normal, null, null, T0),
                Party(), new TopicFatigueTracker(), 0, T0, 0.0, 0.0, false);
            if (!decision.SilenceWon) return "expected silence, selected " + decision.SelectedTopicKey;
            if (decision.Candidates.Count == 0) return "no candidates were evaluated";
            return null;
        }

        // 2. idle_waiting is weak by construction and unusable once it has just been said.
        private static string RecentIdleIsSuppressed()
        {
            TopicFatigueTracker fatigue = new TopicFatigueTracker();
            fatigue.NoteUsed(AmbientTopics.IdleWaiting, AmbientTopics.Idle.CooldownGroup, "Fiora", 0, T0);

            string detail;
            double before = new TopicFatigueTracker().Penalty(AmbientTopics.IdleWaiting,
                AmbientTopics.Idle.CooldownGroup, "Fiora", 0, T0, out detail);
            double after = fatigue.Penalty(AmbientTopics.IdleWaiting, AmbientTopics.Idle.CooldownGroup,
                "Fiora", 0, T0.AddSeconds(30.0), out detail);
            if (before != 0.0) return "unused topic already penalized";
            if (after < 55.0) return "recent idle penalty too small: " + after;

            AmbientSeedDecision decision = Select(2, SocialContextMode.Normal,
                new List<AmbientSeedCandidate> { new AmbientSeedCandidate(AmbientTopics.Idle, T0) },
                Party(), fatigue, 0, T0.AddSeconds(30.0), 0.0, 0.0, false);
            if (!decision.SilenceWon) return "recently used idle still won";
            return null;
        }

        // 3. A grounded session observation must outrank generic waiting commentary.
        private static string MeaningfulBeatsIdle()
        {
            List<AmbientSeedCandidate> candidates = new List<AmbientSeedCandidate>
            {
                new AmbientSeedCandidate(AmbientTopics.Idle, T0),
                Observation("The party recorded four Molorai militia kills in Krakengard.")
            };
            double idle = ScoreOf(Select(3, SocialContextMode.Camp, candidates, Party(),
                new TopicFatigueTracker(), 0, T0, 0.0, 0.0, true), AmbientTopics.IdleWaiting);
            double observation = ScoreOf(Select(3, SocialContextMode.Camp, candidates, Party(),
                new TopicFatigueTracker(), 0, T0, 0.0, 0.0, true), AmbientTopics.SessionObservation);
            if (observation <= idle) return "observation " + observation + " did not beat idle " + idle;
            return null;
        }

        // 4. Saying the same thing repeatedly must cost more each time.
        private static string RepeatedTopicAccumulatesFatigue()
        {
            TopicFatigueTracker fatigue = new TopicFatigueTracker();
            string detail;
            fatigue.NoteUsed("zone_preference", "preference", "Fiora", 0, T0);
            double once = fatigue.Penalty("zone_preference", "preference", "Phanty", 0, T0.AddSeconds(120.0), out detail);
            fatigue.NoteUsed("zone_preference", "preference", "Phanty", 0, T0.AddSeconds(120.0));
            double twice = fatigue.Penalty("zone_preference", "preference", "Dancer", 0, T0.AddSeconds(240.0), out detail);
            fatigue.NoteUsed("zone_preference", "preference", "Dancer", 0, T0.AddSeconds(240.0));
            double thrice = fatigue.Penalty("zone_preference", "preference", "Fiora", 0, T0.AddSeconds(280.0), out detail);
            if (!(twice > once)) return "second use not more fatigued: " + once + " -> " + twice;
            if (!(thrice > twice)) return "third use not more fatigued: " + twice + " -> " + thrice;
            return null;
        }

        // 5. Different wordings of the same non-subject collapse onto one key.
        private static string SemanticIdleVariants()
        {
            string[] variants = new string[]
            {
                "Nothing is happening.", "Not much going on.", "I'm waiting.",
                "We're just standing here.", "not much happening tbh", "just sitting here"
            };
            for (int i = 0; i < variants.Length; i++)
                if (AmbientTopics.ClassifyIdleVariant(variants[i]) != AmbientTopics.IdleWaiting)
                    return "variant not mapped: " + variants[i];
            if (AmbientTopics.ClassifyIdleVariant("which zone has the best view?") != null)
                return "a real subject was misclassified as waiting";
            if (AmbientTopics.ClassifyIdleVariant("that pull was clean") != null)
                return "a combat comment was misclassified as waiting";
            return null;
        }

        // 6. Camp already means the party has stopped; saying so is not a subject there.
        private static string CampSuppressesWaiting()
        {
            List<AmbientSeedCandidate> camp =
                AmbientSeedProducers.BuildDowntimeCandidates(SocialContextMode.Camp, null, null, T0);
            for (int i = 0; i < camp.Count; i++)
                if (string.Equals(camp[i].TopicKey, AmbientTopics.IdleWaiting, StringComparison.Ordinal))
                    return "camp still offered idle_waiting";
            List<AmbientSeedCandidate> normal =
                AmbientSeedProducers.BuildDowntimeCandidates(SocialContextMode.Normal, null, null, T0);
            bool present = false;
            for (int i = 0; i < normal.Count; i++)
                if (string.Equals(normal[i].TopicKey, AmbientTopics.IdleWaiting, StringComparison.Ordinal)) present = true;
            if (!present) return "normal context lost the idle key entirely";
            return null;
        }

        // 7. With no candidates at all the selector must return silence, never an invented subject.
        private static string NoValidSeedNoAnecdote()
        {
            AmbientSeedDecision decision = Select(7, SocialContextMode.Normal,
                new List<AmbientSeedCandidate>(), Party(), new TopicFatigueTracker(), 0, T0, 0.0, 0.0, false);
            if (!decision.SilenceWon) return "empty candidate list produced a subject";
            if (decision.SelectedPromptHint.Length != 0 || decision.SelectedFact.Length != 0)
                return "silence carried prompt content";

            AmbientSeedDecision forced = Select(7, SocialContextMode.Normal,
                new List<AmbientSeedCandidate>(), Party(), new TopicFatigueTracker(), 0, T0, 0.0, 0.0, true);
            if (!forced.SilenceWon) return "forced speech invented a subject with no candidates";
            return null;
        }

        // 8/26. Choosing a topic is not using it.
        private static string UsageOnlyAfterEmit()
        {
            TopicFatigueTracker fatigue = new TopicFatigueTracker();
            AmbientSeedDecision decision = Select(8, SocialContextMode.Camp,
                new List<AmbientSeedCandidate> { Observation("Aetheria was looted after the fight.") },
                Party(), fatigue, 0, T0, 0.0, 0.0, true);
            if (decision.SilenceWon) return "expected a selected subject";

            string detail;
            if (fatigue.Penalty(decision.SelectedTopicKey, decision.SelectedCooldownGroup,
                decision.SelectedSpeaker, 0, T0, out detail) != 0.0)
                return "selection alone marked the topic as used";

            fatigue.NoteUsed(decision.SelectedTopicKey, decision.SelectedCooldownGroup,
                decision.SelectedSpeaker, 0, T0);
            if (fatigue.Penalty(decision.SelectedTopicKey, decision.SelectedCooldownGroup,
                decision.SelectedSpeaker, 0, T0.AddSeconds(10.0), out detail) <= 0.0)
                return "emitted topic was not recorded";
            return null;
        }

        // 9/25. A budget rejection happens after selection and must leave the topic fresh.
        private static string SuppressionDoesNotConsumeTopic()
        {
            TopicFatigueTracker fatigue = new TopicFatigueTracker();
            SocialBudget budget = new SocialBudget();
            budget.SetPreset(SocialActivityPreset.Normal);
            string reason;
            budget.NotePlayerSpeech(T0);

            AmbientSeedDecision decision = Select(9, SocialContextMode.Camp,
                new List<AmbientSeedCandidate> { Observation("The party cleared the last camp of militia.") },
                Party(), fatigue, 0, T0, 0.0, 0.0, true);
            if (decision.SilenceWon) return "expected a selected subject";

            bool admitted = budget.CanAdmitOpportunity("camp_idle", SocialPriority.Low,
                "ambient|" + decision.SelectedTopicKey, T0.AddSeconds(1.0), false, true, out reason);
            if (admitted) return "budget should have suppressed a subject right after player speech";

            string detail;
            if (fatigue.Penalty(decision.SelectedTopicKey, decision.SelectedCooldownGroup,
                decision.SelectedSpeaker, 0, T0.AddSeconds(1.0), out detail) != 0.0)
                return "suppressed subject was counted as used";
            return null;
        }

        // 10/24. A seed whose supporting state has aged out cannot be selected.
        private static string ExpiredCandidateCannotWin()
        {
            AmbientSeedCandidate expiring = new AmbientSeedCandidate("recent_pull", "encounter",
                "react to the supplied verified pull", 90.0, "The party pulled three militia.",
                "verified encounter telemetry", 80, 0.0, T0, T0.AddSeconds(20.0), null);
            AmbientSeedDecision fresh = Select(10, SocialContextMode.Camp,
                new List<AmbientSeedCandidate> { expiring }, Party(), new TopicFatigueTracker(),
                0, T0.AddSeconds(5.0), 0.0, 0.0, false);
            if (fresh.SilenceWon) return "fresh high-value seed lost";

            AmbientSeedDecision stale = Select(10, SocialContextMode.Camp,
                new List<AmbientSeedCandidate> { expiring }, Party(), new TopicFatigueTracker(),
                0, T0.AddSeconds(60.0), 0.0, 0.0, false);
            if (!stale.SilenceWon) return "expired seed still won";
            if (stale.Candidates.Count != 1 || stale.Candidates[0].ExcludedReason != "expired")
                return "expiry was not reported in diagnostics";

            AmbientSeedDecision forced = Select(10, SocialContextMode.Camp,
                new List<AmbientSeedCandidate> { expiring }, Party(), new TopicFatigueTracker(),
                0, T0.AddSeconds(60.0), 0.0, 0.0, true);
            if (!forced.SilenceWon) return "forced speech resurrected an expired seed";
            return null;
        }

        // 11/28. The same inputs must always rank the same way.
        private static string DeterministicScoring()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                AmbientSeedDecision a = Select(184, SocialContextMode.Camp,
                    AmbientSeedProducers.BuildDowntimeCandidates(SocialContextMode.Camp,
                        "The party recorded four militia kills.", "verified telemetry", T0),
                    Party(), new TopicFatigueTracker(), 0, T0, 0.0, 0.0, false);
                AmbientSeedDecision b = Select(184, SocialContextMode.Camp,
                    AmbientSeedProducers.BuildDowntimeCandidates(SocialContextMode.Camp,
                        "The party recorded four militia kills.", "verified telemetry", T0),
                    Party(), new TopicFatigueTracker(), 0, T0, 0.0, 0.0, false);
                if (a.SelectedTopicKey != b.SelectedTopicKey || a.SelectedSpeaker != b.SelectedSpeaker)
                    return "winner varied between identical evaluations";
                if (a.Candidates.Count != b.Candidates.Count) return "candidate count varied";
                for (int i = 0; i < a.Candidates.Count; i++)
                {
                    if (a.Candidates[i].TopicKey != b.Candidates[i].TopicKey) return "ordering varied";
                    if (a.Candidates[i].Score != b.Candidates[i].Score) return "score varied";
                }
                if (AmbientSeedDiagnostics.Format(a) != AmbientSeedDiagnostics.Format(b))
                    return "diagnostics text varied";
            }
            return null;
        }

        // A running thread must not re-raise the subject it is already about.
        private static string ActiveConversationExcludesTopic()
        {
            TopicFatigueTracker fatigue = new TopicFatigueTracker();
            fatigue.NoteUsed("zone_preference", "preference", "Fiora", 42, T0);
            string detail;
            double penalty = fatigue.Penalty("zone_preference", "preference", "Phanty", 42, T0.AddSeconds(2.0), out detail);
            if (penalty != double.MaxValue) return "active-conversation subject was not excluded";

            AmbientSeedDecision decision = Select(12, SocialContextMode.Normal,
                new List<AmbientSeedCandidate> { new AmbientSeedCandidate(AmbientTopics.Find("zone_preference"), T0) },
                Party(), fatigue, 42, T0.AddSeconds(2.0), 0.0, 0.0, true);
            if (!decision.SilenceWon) return "excluded subject still won under forced speech";
            return null;
        }

        // A seed claiming a fact without provenance must never reach the model.
        private static string UnsourcedFactExcluded()
        {
            AmbientSeedCandidate unsourced = new AmbientSeedCandidate("loot:moonblade", "loot",
                "talk about the loot", 90.0, "We looted Moonblade.", null, 90, 0.0,
                T0, DateTime.MaxValue, null);
            AmbientSeedDecision decision = Select(13, SocialContextMode.Camp,
                new List<AmbientSeedCandidate> { unsourced }, Party(), new TopicFatigueTracker(),
                0, T0, 0.0, 0.0, true);
            if (!decision.SilenceWon) return "unsourced fact was selected";
            if (decision.Candidates.Count != 1 || decision.Candidates[0].ExcludedReason != "unsupported provenance")
                return "provenance exclusion not reported";
            return null;
        }

        // A fresh verified event is exactly the case that should break silence.
        private static string FreshImportantSeedBeatsSilence()
        {
            AmbientSeedDecision decision = Select(14, SocialContextMode.Normal,
                new List<AmbientSeedCandidate> { Duel() }, Party(), new TopicFatigueTracker(),
                0, T0, 0.0, 0.0, false);
            if (decision.SilenceWon) return "fresh duel lost to silence: " + decision.Reason;
            if (decision.SelectedTopicKey != "duel:fiora:player") return "wrong subject: " + decision.SelectedTopicKey;
            return null;
        }

        private static string RepeatedImportantSeedFatigues()
        {
            TopicFatigueTracker fatigue = new TopicFatigueTracker();
            AmbientSeedDecision first = Select(15, SocialContextMode.Normal,
                new List<AmbientSeedCandidate> { Duel() }, Party(), fatigue, 0, T0, 0.0, 0.0, false);
            if (first.SilenceWon) return "fresh duel lost";
            fatigue.NoteUsed(first.SelectedTopicKey, first.SelectedCooldownGroup, first.SelectedSpeaker, 0, T0);

            AmbientSeedDecision second = Select(16, SocialContextMode.Normal,
                new List<AmbientSeedCandidate> { Duel() }, Party(), fatigue, 0, T0.AddSeconds(40.0), 0.0, 0.0, false);
            if (!second.SilenceWon) return "the same duel was raised again 40s later";
            return null;
        }

        // Abstract test only: no live mana reader exists, so the reading is supplied explicitly.
        private static string LowResourceBeatsIdle()
        {
            AmbientSeedCandidate seed;
            AuthoritativeResourceReading reading = new AuthoritativeResourceReading
            {
                SimName = "Cyndara",
                ResourceLabel = "mana",
                Current = 12f,
                Max = 100f,
                Source = "test-supplied authoritative resource reading",
                ObservedUtc = T0
            };
            if (!AmbientSeedProducers.TryBuildLowResourceSeed(reading, T0, out seed))
                return "authoritative low reading produced no seed";

            List<AmbientSeedCandidate> candidates =
                AmbientSeedProducers.BuildDowntimeCandidates(SocialContextMode.Camp, null, null, T0);
            candidates.Add(seed);
            candidates.Add(new AmbientSeedCandidate(AmbientTopics.Idle, T0));

            AmbientSeedDecision decision = Select(17, SocialContextMode.Camp, candidates, Party(),
                new TopicFatigueTracker(), 0, T0, 0.0, 0.0, false);
            if (decision.SelectedTopicKey != "mana:cyndara")
                return "expected mana:cyndara, selected " + (decision.SilenceWon ? "SILENCE" : decision.SelectedTopicKey);
            if (decision.SelectedFact.IndexOf("12", StringComparison.Ordinal) < 0)
                return "seed fact lost the supplied reading";
            return null;
        }

        private static string RecoveryInvalidatesSeed()
        {
            AmbientSeedCandidate seed;
            AuthoritativeResourceReading recovered = new AuthoritativeResourceReading
            {
                SimName = "Cyndara",
                ResourceLabel = "mana",
                Current = 88f,
                Max = 100f,
                Source = "test-supplied authoritative resource reading",
                ObservedUtc = T0
            };
            if (AmbientSeedProducers.TryBuildLowResourceSeed(recovered, T0, out seed))
                return "a recovered reading still produced a low-resource seed";

            AuthoritativeResourceReading low = new AuthoritativeResourceReading
            {
                SimName = "Cyndara",
                ResourceLabel = "mana",
                Current = 10f,
                Max = 100f,
                Source = "test-supplied authoritative resource reading",
                ObservedUtc = T0
            };
            AmbientSeedProducers.TryBuildLowResourceSeed(low, T0, out seed);
            AmbientSeedDecision stale = Select(18, SocialContextMode.Camp,
                new List<AmbientSeedCandidate> { seed }, Party(), new TopicFatigueTracker(),
                0, T0.AddSeconds(120.0), 0.0, 0.0, false);
            if (!stale.SilenceWon) return "a stale low-resource seed still won";
            return null;
        }

        private static string UnsourcedResourceRefused()
        {
            AmbientSeedCandidate seed;
            AuthoritativeResourceReading guessed = new AuthoritativeResourceReading
            {
                SimName = "Cyndara",
                ResourceLabel = "mana",
                Current = 5f,
                Max = 100f,
                Source = string.Empty,
                ObservedUtc = T0
            };
            if (AmbientSeedProducers.TryBuildLowResourceSeed(guessed, T0, out seed))
                return "an unsourced reading produced a seed";
            if (AmbientSeedProducers.TryBuildLowResourceSeed(null, T0, out seed))
                return "a null reading produced a seed";

            AuthoritativeResourceReading noMax = new AuthoritativeResourceReading
            {
                SimName = "Cyndara", ResourceLabel = "mana", Current = 0f, Max = 0f,
                Source = "test", ObservedUtc = T0
            };
            if (AmbientSeedProducers.TryBuildLowResourceSeed(noMax, T0, out seed))
                return "a reading with no maximum produced a seed";
            return null;
        }

        private static string PersonalityIsFlavorOnly()
        {
            SimSnapshot rival = new SimSnapshot { Name = "Fiora", Rival = true };
            SimSnapshot plain = new SimSnapshot { Name = "Phanty" };
            if (!(AmbientSeedSelector.PersonalityAffinity(rival, "light_tease") >
                AmbientSeedSelector.PersonalityAffinity(plain, "light_tease")))
                return "rival did not prefer teasing";

            SimSnapshot gearChaser = new SimSnapshot { Name = "Dancer", GearChase = 80 };
            if (!(AmbientSeedSelector.PersonalityAffinity(gearChaser, "gear_aesthetics") >
                AmbientSeedSelector.PersonalityAffinity(plain, "gear_aesthetics")))
                return "gear chaser did not prefer gear talk";

            // Affinity must stay small and must never conjure a subject that has no candidate.
            double affinity = AmbientSeedSelector.PersonalityAffinity(rival, "light_tease");
            if (affinity > 6.0) return "personality affinity is too strong: " + affinity;

            AmbientSeedDecision decision = Select(19, SocialContextMode.Normal,
                new List<AmbientSeedCandidate>(), new List<SimSnapshot> { rival },
                new TopicFatigueTracker(), 0, T0, 0.0, 0.0, false);
            if (!decision.SilenceWon) return "personality created a subject from nothing";
            return null;
        }

        private static string QuietPresetRaisesSilence()
        {
            List<AmbientSeedCandidate> candidates = new List<AmbientSeedCandidate>
            {
                Observation("The party recorded four militia kills.")
            };
            AmbientSeedDecision lively = Select(20, SocialContextMode.Camp, candidates, Party(),
                new TopicFatigueTracker(), 0, T0, 0.0, -3.0, false);
            AmbientSeedDecision quiet = Select(20, SocialContextMode.Camp, candidates, Party(),
                new TopicFatigueTracker(), 0, T0, 0.0, 5.4, false);
            if (!(quiet.SilenceScore > lively.SilenceScore))
                return "quiet preset did not raise the silence threshold";
            return null;
        }

        // 15/20. The verified-event path and the ambient path must never spend the same moment. The
        // event director exposes its pending candidate so ambient evaluation can stand down, and the
        // one shared SocialBudget still admits a single winner.
        private static string PendingEventOwnsMoment()
        {
            EventConversationDirector director = new EventConversationDirector(null);
            if (director.HasPendingCandidate) return "director started with a pending candidate";
            director.Submit(new SocialEventCandidate("friendly_duel", DateTime.UtcNow,
                new[] { "Player", "Fiora" }, new[] { "Phanty" }, new[] { "Fiora" },
                SocialEventTrust.Experienced, 80, 1.0, "duel",
                "Verified friendly duel completed between Fiora and the player.", 0.9));
            if (!director.HasPendingCandidate) return "submitted verified event was not pending";

            SocialBudget budget = new SocialBudget();
            budget.SetPreset(SocialActivityPreset.Normal);
            string reason;
            if (!budget.CanAdmitOpportunity("friendly_duel", SocialPriority.High, "duel", T0, false, true, out reason))
                return "verified event could not claim the moment";
            budget.CommitOpportunity("friendly_duel", SocialPriority.High, "duel", T0);
            if (budget.CanAdmitOpportunity("camp_idle", SocialPriority.Low, "ambient|zone_preference",
                T0.AddSeconds(1.0), false, true, out reason))
                return "ambient subject double-fired alongside the verified event";
            return null;
        }

        // A remembered outing/important-memory string cannot prove who else witnessed it, so only
        // the Sim whose memory it came from may be offered it as a candidate.
        private static string SharedMemoryScopedToOwner()
        {
            SimSnapshot fiora = new SimSnapshot { Name = "Fiora" };
            SimMemory memory = new SimMemory();
            memory.Normalize();
            memory.ImportantMemories.Add("Nearly wiped to a Molorai ambush in Krakengard.");
            memory.OutingSummaries.Add("Grouped in Azure Hollow and found a lantern trinket.");

            List<AmbientSeedCandidate> candidates =
                AmbientSeedProducers.BuildSharedMemoryCandidates(fiora, memory, 3, T0);
            if (candidates.Count != 2) return "expected an important-memory and an outing-summary seed, got " + candidates.Count;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].FactSource.Length == 0) return "memory seed lost its provenance";
                if (!candidates[i].IsEligibleSpeaker("Fiora")) return "owner was not eligible for their own memory";
                if (candidates[i].IsEligibleSpeaker("Phanty")) return "a non-owning Sim could claim another Sim's memory";
            }

            AmbientSeedDecision decision = Select(21, SocialContextMode.Camp, candidates, Party(),
                new TopicFatigueTracker(), 0, T0, 0.0, 0.0, true);
            if (decision.SilenceWon) return "a properly sourced memory seed still lost to silence";
            if (decision.SelectedSpeaker != "Fiora") return "memory was voiced by " + decision.SelectedSpeaker + ", not its owner";
            return null;
        }

        private static string PinnedSharedMemoryRequiresRelevanceAndKnowledge()
        {
            SimSnapshot fiora = new SimSnapshot { Key = "fiora", Name = "Fiora" };
            SimMemory memory = new SimMemory();
            memory.Normalize();
            memory.Name = "Fiora";
            memory.SimKey = "fiora";
            StructuredMemoryRecord pinned = IdentitySchema.Create("authored_pinned",
                "Fiora and Phanty renewed an old temple order vow at sunrise.", 100, true, true,
                fiora.Key, fiora.Name, "test-pinned");
            pinned.Id = "shared-temple-vow";
            pinned.KnownBy = new List<string> { "player", "fiora", "Fiora", "phanty", "Phanty" };
            pinned.Participants = new List<string> { "player", "fiora", "Fiora", "phanty", "Phanty" };
            memory.AuthoredIdentity.PinnedMemories.Add(pinned);

            List<AmbientSeedCandidate> relevant = AmbientSeedProducers.BuildSharedMemoryCandidates(
                fiora, memory, 900, T0, "We are standing by the temple and talking about the order vow.");
            AmbientSeedCandidate found = null;
            for (int i = 0; i < relevant.Count; i++)
                if (relevant[i].TopicKey.IndexOf("memory:pinned:", StringComparison.Ordinal) == 0) { found = relevant[i]; break; }
            if (found == null) return "relevant pinned memory was not offered";
            if (!found.IsEligibleSpeaker("Fiora") || !found.IsEligibleSpeaker("Phanty")) return "KnownBy participants were not eligible";
            if (found.IsEligibleSpeaker("Dancer")) return "uninformed Sim gained eligibility";

            List<AmbientSeedCandidate> irrelevant = AmbientSeedProducers.BuildSharedMemoryCandidates(
                fiora, memory, 901, T0, "auction prices and weapon repairs");
            for (int i = 0; i < irrelevant.Count; i++)
                if (irrelevant[i].TopicKey.IndexOf("memory:pinned:", StringComparison.Ordinal) == 0)
                    return "pinned memory was injected without relevance";
            return null;
        }

        private static string PinnedMemoryAccruesFatigue()
        {
            SimSnapshot fiora = new SimSnapshot { Key = "fiora", Name = "Fiora" };
            SimMemory memory = new SimMemory(); memory.Normalize(); memory.Name = "Fiora"; memory.SimKey = "fiora";
            StructuredMemoryRecord pinned = IdentitySchema.Create("authored_pinned",
                "The old temple bell marked the promise.", 100, true, true, fiora.Key, fiora.Name, "test");
            pinned.Id = "same-pinned-memory";
            memory.AuthoredIdentity.PinnedMemories.Add(pinned);
            List<AmbientSeedCandidate> first = AmbientSeedProducers.BuildSharedMemoryCandidates(fiora, memory, 902, T0, "temple bell promise");
            List<AmbientSeedCandidate> second = AmbientSeedProducers.BuildSharedMemoryCandidates(fiora, memory, 903, T0.AddSeconds(10), "temple bell promise");
            AmbientSeedCandidate a = null, b = null;
            for (int i = 0; i < first.Count; i++) if (first[i].TopicKey.IndexOf("memory:pinned:", StringComparison.Ordinal) == 0) a = first[i];
            for (int i = 0; i < second.Count; i++) if (second[i].TopicKey.IndexOf("memory:pinned:", StringComparison.Ordinal) == 0) b = second[i];
            if (a == null || b == null || a.TopicKey != b.TopicKey) return "same pinned record did not keep one fatigue key";
            TopicFatigueTracker fatigue = new TopicFatigueTracker();
            fatigue.NoteUsed(a.TopicKey, a.CooldownGroup, "Fiora", 0, T0);
            string detail;
            double penalty = fatigue.Penalty(b.TopicKey, b.CooldownGroup, "Fiora", 0, T0.AddSeconds(10), out detail);
            return penalty > 0.0 ? null : "same pinned memory received no repeat penalty";
        }

        private static string IdentitySeedIsRelevantAndPerspectiveAware()
        {
            SimSnapshot cyndara = new SimSnapshot { Key = "cyndara", Name = "Cyndara", ClassName = "Arcanist" };
            SimMemory memory = new SimMemory(); memory.Normalize(); memory.Name = "Cyndara"; memory.SimKey = "cyndara";
            memory.AuthoredIdentity.ErenshorPersona = "a junior scholar from the mage academy who studies spellcraft";
            memory.AuthoredIdentity.PersonalBackground = "a college student considering a job after graduation";

            List<AmbientSeedCandidate> rpRelevant = AmbientSeedProducers.BuildIdentityCandidates(cyndara, memory,
                "We are discussing spellcraft and the mage academy.", true, T0);
            bool persona = false, background = false;
            for (int i = 0; i < rpRelevant.Count; i++)
            {
                persona |= rpRelevant[i].TopicKey.IndexOf("identity:persona:", StringComparison.Ordinal) == 0;
                background |= rpRelevant[i].TopicKey.IndexOf("identity:background:", StringComparison.Ordinal) == 0;
            }
            if (!persona) return "relevant Erenshor persona did not become a seed";
            if (background) return "modern personal background leaked into Roleplay seed candidates";

            List<AmbientSeedCandidate> rpIrrelevant = AmbientSeedProducers.BuildIdentityCandidates(cyndara, memory,
                "The bridge looks narrow.", true, T0);
            if (rpIrrelevant.Count != 0) return "irrelevant biography was offered as ambient subject";

            List<AmbientSeedCandidate> mmoRelevant = AmbientSeedProducers.BuildIdentityCandidates(cyndara, memory,
                "Have you thought about a job after graduation?", false, T0);
            for (int i = 0; i < mmoRelevant.Count; i++)
                if (mmoRelevant[i].TopicKey.IndexOf("identity:background:", StringComparison.Ordinal) == 0) return null;
            return "relevant personal background was unavailable in MMO perspective";
        }

        private static string RecentCompetitiveEventBeatsIdle()
        {
            SimSnapshot fiora = new SimSnapshot { Key = "fiora", Name = "Fiora", ClassName = "Windblade" };
            SimMemory memory = new SimMemory(); memory.Normalize(); memory.Name = "Fiora"; memory.SimKey = "fiora";
            memory.RecentEvents.Add(new MemoryEvent
            {
                utc = T0.AddMinutes(-5).ToString("o"), type = "pvp_match_completed",
                text = "Verified PvP match completed between the player and Fiora in Port Azure.", importance = 85
            });
            List<AmbientSeedCandidate> candidates = new List<AmbientSeedCandidate> { new AmbientSeedCandidate(AmbientTopics.Idle, T0) };
            candidates.AddRange(AmbientSeedProducers.BuildSharedMemoryCandidates(fiora, memory, 904, T0, "Port Azure"));
            AmbientSeedDecision decision = Select(904, SocialContextMode.Normal, candidates,
                new List<SimSnapshot> { fiora }, new TopicFatigueTracker(), 0, T0, 0.6, 0.0, false);
            if (decision.SilenceWon) return "verified competitive event lost to silence";
            if (decision.SelectedTopicKey.IndexOf("competitive:", StringComparison.Ordinal) != 0)
                return "selected " + decision.SelectedTopicKey + " instead of competitive event";
            return decision.SelectedSource.IndexOf("competitive", StringComparison.OrdinalIgnoreCase) >= 0 ? null : "competitive source label missing";
        }

        private static string CompetitiveTerminalCorrelationCollapsesDuplicate()
        {
            CompetitiveEventCorrelation.ResetForTests();
            string firstKey, duplicateKey, rematchKey;
            if (!CompetitiveEventCorrelation.TryAcceptTerminal("pvp", "match-42", "Fiora", "Port Azure", T0, out firstKey))
                return "first authoritative terminal event was rejected";
            if (CompetitiveEventCorrelation.TryAcceptTerminal("nemesis", "match-42", "Fiora", "Port Azure", T0.AddSeconds(2), out duplicateKey))
                return "cross-source duplicate terminal event was accepted";
            if (!CompetitiveEventCorrelation.TryAcceptTerminal("nemesis", "match-43", "Fiora", "Port Azure", T0.AddSeconds(45), out rematchKey))
                return "distinct later rematch was collapsed";
            if (string.IsNullOrWhiteSpace(firstKey) || string.IsNullOrWhiteSpace(duplicateKey)) return "correlation key was not produced";
            CompetitiveEventCorrelation.ResetForTests();
            return null;
        }

        private static string SeedDiagnosticsArePrivacySafe()
        {
            SimSnapshot fiora = new SimSnapshot { Key = "fiora", Name = "Fiora" };
            SimMemory memory = new SimMemory(); memory.Normalize(); memory.Name = "Fiora"; memory.SimKey = "fiora";
            const string secret = "private moonstone promise alpha";
            StructuredMemoryRecord pinned = IdentitySchema.Create("authored_pinned", secret, 100, true, true,
                fiora.Key, fiora.Name, "private-test");
            pinned.Id = "private-memory-1";
            memory.AuthoredIdentity.PinnedMemories.Add(pinned);
            List<AmbientSeedCandidate> candidates = AmbientSeedProducers.BuildSharedMemoryCandidates(fiora, memory, 905, T0, "moonstone promise");
            AmbientSeedDecision decision = Select(905, SocialContextMode.Camp, candidates,
                new List<SimSnapshot> { fiora }, new TopicFatigueTracker(), 0, T0, 1.0, 0.0, true);
            string diag = AmbientSeedDiagnostics.Format(decision);
            if (diag.IndexOf(secret, StringComparison.OrdinalIgnoreCase) >= 0) return "raw private memory appeared in diagnostics";
            if (diag.IndexOf("opportunity=ambient", StringComparison.Ordinal) < 0 ||
                diag.IndexOf("mode/context=", StringComparison.Ordinal) < 0 ||
                diag.IndexOf("source=", StringComparison.Ordinal) < 0 ||
                diag.IndexOf("speaker=", StringComparison.Ordinal) < 0 ||
                diag.IndexOf("score=", StringComparison.Ordinal) < 0 ||
                diag.IndexOf("exclusion=", StringComparison.Ordinal) < 0 ||
                diag.IndexOf("silenceScore=", StringComparison.Ordinal) < 0 ||
                diag.IndexOf("selected=", StringComparison.Ordinal) < 0)
                return "bounded seed diagnostic fields are incomplete";
            return null;
        }

        private static string PlayerTopicClassifierIsConservative()
        {
            if (PlayerTopicClassifier.Classify("did anyone see what dropped from that pack?") != PlayerTopicClassifier.Loot)
                return "loot phrasing was not classified";
            if (PlayerTopicClassifier.Classify("which zone should we head to next?") != PlayerTopicClassifier.Zone)
                return "zone phrasing was not classified";
            if (PlayerTopicClassifier.Classify("anyone up for a duel later?") != PlayerTopicClassifier.Duel)
                return "duel phrasing was not classified";
            if (PlayerTopicClassifier.Classify("Cyndara, did you study those spells at the mage academy?") != PlayerTopicClassifier.StudyMagic)
                return "spell-study phrasing was not classified";
            if (PlayerTopicClassifier.Classify("does your holy order visit this temple?") != PlayerTopicClassifier.FaithOrder)
                return "faith/order phrasing was not classified";
            if (PlayerTopicClassifier.Classify("what kind of job do you want after graduation?") != PlayerTopicClassifier.WorkLife)
                return "work-life phrasing was not classified";
            if (PlayerTopicClassifier.Classify("does this lever work?") != null)
                return "generic use of work was over-classified as real-life work";
            if (PlayerTopicClassifier.Classify("lol nice one") != null)
                return "unrelated chatter was classified as a topic";
            if (PlayerTopicClassifier.Classify(string.Empty) != null)
                return "empty text was classified as a topic";
            return null;
        }

        // A Sim who was not present when the player raised a topic must not become eligible to
        // reference it later, matching the party-witnessed rule for verified events.
        private static string PlayerTopicScopeFixedAtRecordTime()
        {
            PlayerTopicTracker tracker = new PlayerTopicTracker(300.0);
            tracker.NotePartyMessage("did anyone see what dropped from that pack?", new[] { "Fiora", "Phanty" }, T0);

            List<AmbientSeedCandidate> candidates = tracker.BuildCandidates(T0.AddSeconds(5.0));
            if (candidates.Count != 1) return "expected exactly one recorded player topic";
            if (!candidates[0].IsEligibleSpeaker("Fiora")) return "a present Sim lost eligibility";
            if (candidates[0].IsEligibleSpeaker("Dancer")) return "a Sim who joined afterward gained eligibility";
            if (candidates[0].HasFact) return "an unverified player line was carried as a verified Fact";
            string relevance = tracker.BuildRelevanceContext(T0.AddSeconds(5.0));
            if (relevance.IndexOf("dropped", StringComparison.OrdinalIgnoreCase) < 0)
                return "recent heard topic was unavailable as bounded relevance context";
            return null;
        }

        private static string PlayerTopicExpiresAndUnverified()
        {
            PlayerTopicTracker tracker = new PlayerTopicTracker(60.0);
            tracker.NotePartyMessage("which zone should we head to next?", new[] { "Fiora" }, T0);

            List<AmbientSeedCandidate> fresh = tracker.BuildCandidates(T0.AddSeconds(30.0));
            if (fresh.Count != 1) return "topic disappeared before its TTL";

            List<AmbientSeedCandidate> stale = tracker.BuildCandidates(T0.AddSeconds(90.0));
            if (stale.Count != 0) return "topic survived past its TTL";
            return null;
        }

        // Familiarity may only shift which existing candidate a Sim prefers; with no candidates at
        // all it must never manufacture one.
        private static string FamiliarityIsToneOnly()
        {
            SimSnapshot familiar = new SimSnapshot { Name = "Fiora" };
            double bare = AmbientSeedSelector.PersonalityAffinity(familiar, "other_sim_preference", 0.0);
            double warm = AmbientSeedSelector.PersonalityAffinity(familiar, "other_sim_preference", 1.0);
            if (!(warm > bare)) return "high familiarity did not raise other_sim_preference affinity";
            if (warm > 6.0) return "familiarity-driven affinity is too strong: " + warm;

            Dictionary<string, double> familiarityBySpeaker = new Dictionary<string, double> { { "Fiora", 1.0 } };
            AmbientSeedDecision decision = AmbientSeedSelector.Select(22, SocialContextMode.Normal,
                new List<AmbientSeedCandidate>(), new List<SimSnapshot> { familiar }, new TopicFatigueTracker(),
                0, T0, AmbientSeedSelector.DefaultSilenceNormal, AmbientSeedSelector.DefaultSilenceCamp,
                0.0, 0.0, false, true, familiarityBySpeaker);
            if (!decision.SilenceWon) return "familiarity manufactured a subject from an empty candidate list";
            return null;
        }

        private static string OtherSimSeedRequiresAnotherVisibleSim()
        {
            AmbientSeedCandidate candidate = new AmbientSeedCandidate(AmbientTopics.Find("other_sim_preference"), T0);
            string reason;
            if (AmbientSeedPrerequisitePolicy.IsSupported(candidate, new List<SimSnapshot> { new SimSnapshot { Name = "Fiora" } }, out reason))
                return "single-Sim party was allowed to select an other-Sim seed";
            if (reason.IndexOf("another visible", StringComparison.OrdinalIgnoreCase) < 0) return "missing explicit prerequisite reason";
            if (!AmbientSeedPrerequisitePolicy.IsSupported(candidate, new List<SimSnapshot> { new SimSnapshot { Name = "Fiora" }, new SimSnapshot { Name = "Phanty" } }, out reason))
                return "two visible eligible Sims were incorrectly rejected";
            return null;
        }

        private static string RejectionPenaltyIsBoundedAndTemporary()
        {
            TopicFatigueTracker fatigue = new TopicFatigueTracker();
            string detail;
            double before = fatigue.Penalty("other_sim_preference", "social", "Astra", 0, T0, out detail);
            fatigue.NoteRejected("other_sim_preference", "Astra", "topic mismatch", T0);
            double immediate = fatigue.Penalty("other_sim_preference", "social", "Astra", 0, T0.AddSeconds(1), out detail);
            double later = fatigue.Penalty("other_sim_preference", "social", "Astra", 0, T0.AddSeconds(240), out detail);
            if (before != 0.0) return "fresh topic unexpectedly penalized";
            if (immediate < 20.0 || immediate > 60.0) return "temporary rejection penalty out of bounds: " + immediate;
            if (later != 0.0) return "rejection penalty did not expire";
            return null;
        }

        private static string RejectionPenaltyIsScoped()
        {
            TopicFatigueTracker fatigue = new TopicFatigueTracker();
            fatigue.NoteRejected("other_sim_preference", "Astra", "topic mismatch", T0);
            string detail;
            double same = fatigue.Penalty("other_sim_preference", "social", "Astra", 0, T0.AddSeconds(2), out detail);
            double otherSpeaker = fatigue.Penalty("other_sim_preference", "social", "Cyndara", 0, T0.AddSeconds(2), out detail);
            double otherTopic = fatigue.Penalty("class_opinion", "preference", "Astra", 0, T0.AddSeconds(2), out detail);
            if (same <= 0.0) return "same failed semantic seed received no penalty";
            if (otherSpeaker != 0.0) return "one Sim's stochastic failure penalized another Sim";
            if (otherTopic != 0.0) return "one topic failure penalized a different topic";
            return null;
        }

        private static AmbientSeedDecision Select(long opportunityId, SocialContextMode mode,
            IList<AmbientSeedCandidate> candidates, IList<SimSnapshot> speakers,
            TopicFatigueTracker fatigue, long conversationId, DateTime now,
            double quietPressure, double silenceAdjust, bool forceSpeech)
        {
            return AmbientSeedSelector.Select(opportunityId, mode, candidates, speakers, fatigue,
                conversationId, now, AmbientSeedSelector.DefaultSilenceNormal,
                AmbientSeedSelector.DefaultSilenceCamp, quietPressure, silenceAdjust, forceSpeech, true);
        }

        private static double ScoreOf(AmbientSeedDecision decision, string topicKey)
        {
            for (int i = 0; i < decision.Candidates.Count; i++)
                if (string.Equals(decision.Candidates[i].TopicKey, topicKey, StringComparison.Ordinal))
                    return decision.Candidates[i].Excluded ? double.MinValue : decision.Candidates[i].Score;
            return double.MinValue;
        }

        private static AmbientSeedCandidate Observation(string fact)
        {
            return new AmbientSeedCandidate(AmbientTopics.SessionObservation,
                AmbientTopics.Observation.CooldownGroup, AmbientTopics.Observation.PromptHint,
                AmbientTopics.Observation.BaseScore, fact, "verified current-session outing telemetry",
                0, 0.0, T0, DateTime.MaxValue, null);
        }

        private static AmbientSeedCandidate Duel()
        {
            return new AmbientSeedCandidate("duel:fiora:player", "duel",
                "react to the verified completed practice duel without inventing a winner or a wager",
                28.0, "Verified friendly duel completed between Fiora and the player.",
                "verified duel telemetry", 80, 0.0, T0, T0.AddSeconds(600.0), null);
        }

        private static string ContextualPreferenceBreaksCloseTie()
        {
            List<SimSnapshot> speakers = Party();
            for (long id = 8000; id < 8100; id++)
            {
                List<AmbientSeedCandidate> candidates = new List<AmbientSeedCandidate>
                {
                    new AmbientSeedCandidate("ordinary_downtime", "smalltalk", "generic fallback", 33.0, string.Empty, string.Empty, 0, 0.0, T0, DateTime.MaxValue, null),
                    new AmbientSeedCandidate("free_social_hypothetical", "smalltalk", "safe hypothetical", 32.0, string.Empty, string.Empty, 0, 0.0, T0, DateTime.MaxValue, null)
                };
                AmbientSeedDecision ordinary = AmbientSeedSelector.Select(id, SocialContextMode.Camp, candidates, speakers,
                    new TopicFatigueTracker(), 0, T0, AmbientSeedSelector.DefaultSilenceNormal, AmbientSeedSelector.DefaultSilenceCamp,
                    AmbientSeedSelector.DefaultSilenceRelax, 0.0, 0.0, false, true, null, 0.0);
                AmbientSeedDecision lively = AmbientSeedSelector.Select(id, SocialContextMode.Camp, candidates, speakers,
                    new TopicFatigueTracker(), 0, T0, AmbientSeedSelector.DefaultSilenceNormal, AmbientSeedSelector.DefaultSilenceCamp,
                    AmbientSeedSelector.DefaultSilenceRelax, 0.0, 0.0, false, true, null, 1.0);
                if (ordinary.SelectedTopicKey == "ordinary_downtime" && lively.SelectedTopicKey == "free_social_hypothetical") return null;
            }
            return "contextual nudge never changed a close generic-vs-free-social result";
        }

        private static string FreeSocialShapesRemainSelectable()
        {
            string[] shapes = { "free_social_question", "free_social_hypothetical", "free_social_impulse" };
            for (int i = 0; i < shapes.Length; i++)
            {
                List<AmbientSeedCandidate> one = new List<AmbientSeedCandidate>
                {
                    new AmbientSeedCandidate(shapes[i], "smalltalk", "fact-free social shape", 31.0, string.Empty, string.Empty, 0, 0.0, T0, DateTime.MaxValue, null)
                };
                AmbientSeedDecision decision = AmbientSeedSelector.Select(8200 + i, SocialContextMode.Camp, one, Party(),
                    new TopicFatigueTracker(), 0, T0, AmbientSeedSelector.DefaultSilenceNormal, AmbientSeedSelector.DefaultSilenceCamp,
                    AmbientSeedSelector.DefaultSilenceRelax, 0.0, 0.0, true, true, null, 1.0);
                if (decision.SilenceWon || decision.SelectedTopicKey != shapes[i]) return shapes[i] + " did not remain selectable";
            }
            return null;
        }

        private static List<SimSnapshot> Party()
        {
            return new List<SimSnapshot>
            {
                new SimSnapshot { Key = "fiora", Name = "Fiora", ClassName = "Windblade", Level = 14 },
                new SimSnapshot { Key = "phanty", Name = "Phanty", ClassName = "Arcanist", Level = 12 },
                new SimSnapshot { Key = "dancer", Name = "Dancer", ClassName = "Druid", Level = 13 }
            };
        }

        // A party with some personality spread (rival, gear-chaser, low-patience), matching the kind
        // of PersonalityAffinity nudges a real live party actually produces, rather than the neutral
        // Party() used by the rest of this file where that spread would be an unwanted variable.
        private static List<SimSnapshot> VariedParty()
        {
            return new List<SimSnapshot>
            {
                new SimSnapshot { Key = "fiora", Name = "Fiora", ClassName = "Windblade", Level = 14, Rival = true },
                new SimSnapshot { Key = "phanty", Name = "Phanty", ClassName = "Arcanist", Level = 12, GearChase = 70, Greed = 65 },
                new SimSnapshot { Key = "dancer", Name = "Dancer", ClassName = "Druid", Level = 13, Patience = 20 }
            };
        }

        private static void Add(List<string> results, string name, Func<string> test)
        {
            try
            {
                string reason = test();
                results.Add("seeds/" + name + ": " + (reason == null ? "PASS" : "FAIL (" + reason + ")"));
            }
            catch (Exception ex)
            {
                results.Add("seeds/" + name + ": FAIL (" + ex.GetType().Name + ": " + ex.Message + ")");
            }
        }
    }
}
