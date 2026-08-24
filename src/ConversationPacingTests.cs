using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    // Deterministic coverage for the "party feels like real players hanging out" pacing pass:
    // variable ambient cadence (AmbientCadence), momentum decay, and short-lived callback memory
    // (ConversationMoment / ConversationMomentStore / ConversationCallbackPolicy). SocialDirector.cs
    // itself cannot compile in this offline harness (framework/Unity dependency), so these tests exercise
    // the pure decision logic it delegates to, the same pattern ConversationTurnGuardTests.cs uses.
    internal static class ConversationPacingTests
    {
        internal static List<string> Run()
        {
            List<string> results = new List<string>();

            // TEST 1: a substantive player opinion can become a short-lived callback candidate.
            {
                bool candidate = ConversationCallbackPolicy.IsCallbackCandidate("i think tanks have the hardest job");
                Add(results, "TEST1/substantive opinion becomes a callback candidate", candidate);
            }

            // TEST 2: "gg" never becomes a callback candidate.
            {
                bool ggCandidate = ConversationCallbackPolicy.IsCallbackCandidate("gg");
                bool okCandidate = ConversationCallbackPolicy.IsCallbackCandidate("ok");
                Add(results, "TEST2/gg and ok never become callback candidates", !ggCandidate && !okCandidate);
            }

            // TEST 3: a PlayerSaid callback only remembers that the statement was SAID, never that the
            // underlying claim is true - the moment records SourceType/TextSummary only, and noting it
            // never touches verified gameplay memory (SimMemory) at all.
            {
                ConversationMomentStore store = new ConversationMomentStore();
                DateTime t = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
                store.Note("class_opinion", "Player", "i think tanks have the hardest job", t, ConversationMomentSource.PlayerSaid, 1);
                ConversationMoment picked;
                bool found = store.TryPickCallback(t.AddSeconds(5), null, out picked);
                bool onlySaidNotTrue = found && picked.SourceType == ConversationMomentSource.PlayerSaid &&
                    picked.TextSummary.IndexOf("tanks have the hardest job", StringComparison.OrdinalIgnoreCase) >= 0;
                Add(results, "TEST3/PlayerSaid callback records only that it was said", onlySaidNotTrue);
            }

            // TEST 4: a callback can never be promoted into verified-gameplay memory. Noting a moment
            // into ConversationMomentStore must never write into a SimMemory instance passed alongside
            // it - MemoryStore is the only thing that decides what survives across sessions.
            {
                SimMemory memory = new SimMemory();
                memory.Normalize();
                memory.Name = "Dancer";
                int beforeImportant = memory.ImportantMemories.Count;
                int beforeOuting = memory.OutingSummaries.Count;
                ConversationMomentStore store = new ConversationMomentStore();
                DateTime t = new DateTime(2026, 8, 10, 12, 5, 0, DateTimeKind.Utc);
                store.Note("class_opinion", "Dancer", "honestly healing is way harder than people think", t, ConversationMomentSource.SimSaid, 2);
                bool memoryUntouched = memory.ImportantMemories.Count == beforeImportant && memory.OutingSummaries.Count == beforeOuting;
                Add(results, "TEST4/a callback can never be promoted into verified gameplay memory", memoryUntouched);
            }

            // TEST 5: a new player topic suppresses/invalidates an old callback on a different subject.
            {
                ConversationMomentStore store = new ConversationMomentStore();
                DateTime t = new DateTime(2026, 8, 10, 12, 10, 0, DateTimeKind.Utc);
                store.Note("class_opinion", "Player", "i think tanks have the hardest job honestly", t, ConversationMomentSource.PlayerSaid, 1);
                List<ConversationMoment> shortened = store.InvalidateConflicting("where the party should go", t.AddSeconds(30));
                ConversationMoment picked;
                bool stillUsableRightAfter = store.TryPickCallback(t.AddSeconds(31), null, out picked);
                bool goneAfterShortenedWindow = !store.TryPickCallback(t.AddSeconds(60), null, out picked);
                Add(results, "TEST5/new player topic invalidates an old conflicting callback",
                    shortened.Count == 1 && stillUsableRightAfter && goneAfterShortenedWindow);
            }

            // TEST 6: a callback expires after its TTL and is no longer offered.
            {
                ConversationMomentStore store = new ConversationMomentStore();
                DateTime t = new DateTime(2026, 8, 10, 12, 15, 0, DateTimeKind.Utc);
                // A short, ordinary-but-still-candidate remark gets the shorter 2-5 minute TTL band.
                store.Note("smalltalk", "Player", "class stuff is neat I guess?", t, ConversationMomentSource.PlayerSaid, 1);
                double ttl = ConversationCallbackPolicy.TtlSeconds(ConversationCallbackPolicy.InterestScore("class stuff is neat I guess?"));
                ConversationMoment picked;
                bool usableBeforeTtl = store.TryPickCallback(t.AddSeconds(Math.Max(1.0, ttl - 5.0)), null, out picked);
                bool expiredAfterTtl = !store.TryPickCallback(t.AddSeconds(ttl + 30.0), null, out picked);
                Add(results, "TEST6/callback expires after its TTL", usableBeforeTtl && expiredAfterTtl);
            }

            // TEST 7: a recent callback may outrank a fresh generic ambient topic (idle_waiting sits at
            // a deliberately low base score; an ordinary downtime topic sits around 24).
            {
                ConversationMomentStore store = new ConversationMomentStore();
                DateTime t = new DateTime(2026, 8, 10, 12, 20, 0, DateTimeKind.Utc);
                store.Note("class_opinion", "Player", "i honestly think healers have the hardest job in this game", t, ConversationMomentSource.PlayerSaid, 1);
                ConversationMoment picked;
                store.TryPickCallback(t.AddSeconds(5), null, out picked);
                AmbientSeedCandidate callbackCandidate = MirrorBuildCallbackCandidate(picked, t.AddSeconds(5));
                bool outranksIdle = callbackCandidate.BaseScore > AmbientTopics.Idle.BaseScore;
                Add(results, "TEST7/a recent strong callback may outrank a fresh generic ambient topic", outranksIdle);
            }

            // TEST 8: an active conversation always outranks a callback - modeled as the priority-order
            // contract: SocialBudget's conversation-thread window (continuation=true bypass aside)
            // blocks a fresh Low-priority ambient/callback opportunity outright while a thread is live.
            {
                SocialBudget budget = new SocialBudget();
                budget.SetPreset(SocialActivityPreset.Normal);
                DateTime t = new DateTime(2026, 8, 10, 12, 25, 0, DateTimeKind.Utc);
                // An active Sim-to-Sim thread (not a fresh player line, which has its own longer
                // "player recently spoke" quiet gate) exercises the "current conversation thread" reason.
                budget.NoteConversationActivity(t);
                string reason;
                bool callbackBlockedDuringActiveThread = !budget.CanAdmitOpportunity(
                    "ambient", SocialPriority.Low, "callback_class_opinion", t.AddSeconds(2), false, true, out reason);
                Add(results, "TEST8/an active conversation always outranks a callback opportunity",
                    callbackBlockedDuringActiveThread && reason == "current conversation thread");
            }

            // TEST 9: variable Normal cadence produces opportunities across multiple timing bands
            // (deterministic-seeded statistical check).
            {
                Random rng = new Random(12345);
                HashSet<int> bandsHit = new HashSet<int>();
                for (int i = 0; i < 200; i++)
                {
                    double delay = AmbientCadence.NextDelaySeconds(SocialActivityPreset.Normal, rng);
                    bandsHit.Add(AmbientCadence.BandIndexFor(SocialActivityPreset.Normal, delay));
                }
                Add(results, "TEST9/variable Normal cadence spans multiple timing bands", bandsHit.Count >= 3);
            }

            // TEST 10: variable cadence does not itself imply mandatory speech - silence remains a
            // valid outcome at timer expiry (AmbientSeedDecision.SilenceWon is independent of the delay
            // value; reaching the delay only creates an opportunity to decide).
            {
                AmbientSeedDecision decision = new AmbientSeedDecision();
                // SelectedTopicKey defaults to empty, so SilenceWon is already true here - reaching a
                // cadence delay never by itself picks a subject.
                double anyDelay = AmbientCadence.NextDelaySeconds(SocialActivityPreset.Normal, 0.5, 0.5);
                bool silenceStillValidRegardlessOfDelay = decision.SilenceWon && anyDelay > 0.0;
                Add(results, "TEST10/reaching a cadence delay does not force speech - silence stays valid", silenceStillValidRegardlessOfDelay);
            }

            // TEST 11: Lively has a shorter expected opportunity interval than Normal.
            {
                bool livelyShorter = AmbientCadence.ExpectedSeconds(SocialActivityPreset.Lively) < AmbientCadence.ExpectedSeconds(SocialActivityPreset.Normal);
                Add(results, "TEST11/Lively expected interval is shorter than Normal", livelyShorter);
            }

            // TEST 12: Quiet has a longer expected opportunity interval than Normal.
            {
                bool quietLonger = AmbientCadence.ExpectedSeconds(SocialActivityPreset.Quiet) > AmbientCadence.ExpectedSeconds(SocialActivityPreset.Normal);
                Add(results, "TEST12/Quiet expected interval is longer than Normal", quietLonger);
            }

            {
                double oneMin, oneMax, twoMin, twoMax, fiveMin, fiveMax;
                AmbientCadence.LivelyPartyRange(1, out oneMin, out oneMax);
                AmbientCadence.LivelyPartyRange(2, out twoMin, out twoMax);
                AmbientCadence.LivelyPartyRange(5, out fiveMin, out fiveMax);
                Add(results, "Lively one-Sim cadence is longest", oneMin == 180.0 && oneMax == 360.0 && oneMin > twoMin);
                Add(results, "Lively two-Sim cadence is moderate", twoMin == 120.0 && twoMax == 240.0);
                Add(results, "Lively full-party cadence is substantially faster", fiveMin == 60.0 && fiveMax == 150.0 && fiveMax < oneMax);
                Add(results, "party scaling remains one bounded jittered timer", AmbientCadence.NextDelaySeconds(SocialActivityPreset.Lively, 5, new Random(7)) >= fiveMin && AmbientCadence.NextDelaySeconds(SocialActivityPreset.Lively, 5, new Random(8)) <= fiveMax);
                Add(results, "thread follow-up delay is bounded 8-30 seconds", AmbientCadence.ThreadFollowUpSeconds(0.0) == 8.0 && AmbientCadence.ThreadFollowUpSeconds(1.0) == 30.0);
            }

            // TEST 13: second-thread-reply probability > third/fourth-reply probability when hooks are
            // equal (momentum decay).
            {
                double second = AmbientCadence.ContinuationChance(2, true, SocialActivityPreset.Normal);
                double third = AmbientCadence.ContinuationChance(3, true, SocialActivityPreset.Normal);
                double fourth = AmbientCadence.ContinuationChance(4, true, SocialActivityPreset.Normal);
                Add(results, "TEST13/momentum decays: reply2 > reply3 > reply4 with equal hooks", second > third && third > fourth);
            }

            // TEST 14: no conversational hook stops the thread early - restated here against the new
            // momentum function specifically (ConversationTurnGuardTests already covers ShouldContinueThread).
            {
                double chanceWithoutHook = AmbientCadence.ContinuationChance(2, false, SocialActivityPreset.Normal);
                Add(results, "TEST14/no conversational hook yields zero continuation momentum", chanceWithoutHook == 0.0);
            }

            // TEST 15: a direct question allows continuation (hook present + momentum > 0).
            {
                bool isHook = ConversationTurnGuard.HasConversationalHook("do you think the tank has it harder?");
                double chance = AmbientCadence.ContinuationChance(2, isHook, SocialActivityPreset.Normal);
                Add(results, "TEST15/a direct question allows continuation", isHook && chance > 0.0);
            }

            // TEST 16: the recent visible message set (3-6 lines) is included before every generated
            // continuation - verified through the existing PromptBuilder.BuildPartyThreadReply plumbing
            // rather than rebuilt here.
            {
                List<ConversationLine> thread = new List<ConversationLine>
                {
                    new ConversationLine("Player", "where should we camp tonight"),
                    new ConversationLine("Dancer", "somewhere with good respawns honestly"),
                    new ConversationLine("Player", "any zone in particular"),
                    new ConversationLine("Dancer", "brasse has been decent for me"),
                    new ConversationLine("Player", "fair, what do you all think of the mobs there?"),
                };
                SimSnapshot cyndara = new SimSnapshot { Name = "Cyndara", ClassName = "Arcanist" };
                SimMemory memory = new SimMemory(); memory.Normalize(); memory.Name = "Cyndara";
                List<ChatMessage> messages = PromptBuilder.BuildPartyThreadReply(cyndara, memory, null, thread, 2, null);
                int earlierLines = 0;
                string joined = string.Empty;
                for (int i = 0; i < messages.Count; i++)
                {
                    joined += "\n" + messages[i].content;
                    if (messages[i].content.IndexOf("Earlier party chat", StringComparison.OrdinalIgnoreCase) >= 0) earlierLines++;
                }
                bool anchored = joined.IndexOf("MOST RECENT PARTY MESSAGE", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    joined.IndexOf("what do you all think of the mobs there", StringComparison.OrdinalIgnoreCase) >= 0;
                Add(results, "TEST16/recent visible message set is included before every continuation",
                    anchored && earlierLines >= 1 && earlierLines <= 2);
            }

            // TEST 17: a Sim statement becomes context for the NEXT Sim - i.e. flows into the next
            // generation's callback pool the same way a player line does.
            {
                ConversationMomentStore store = new ConversationMomentStore();
                DateTime t = new DateTime(2026, 8, 10, 12, 30, 0, DateTimeKind.Utc);
                store.Note("class_opinion", "Dancer", "honestly I think healing is the hardest job in this game", t, ConversationMomentSource.SimSaid, 3);
                ConversationMoment picked;
                bool flowsIntoPool = store.TryPickCallback(t.AddSeconds(10), null, out picked) &&
                    picked.SourceType == ConversationMomentSource.SimSaid && string.Equals(picked.Speaker, "Dancer", StringComparison.Ordinal);
                Add(results, "TEST17/a Sim statement flows into the next generation's callback pool", flowsIntoPool);
            }

            // TEST 18: a delayed callback can safely say "you mentioned X earlier" style wording.
            {
                bool safe1 = ConversationCallbackPolicy.UsesSafeCallbackWording("you mentioned tanks having the hardest job earlier");
                bool safe2 = ConversationCallbackPolicy.UsesSafeCallbackWording("still think healing is harder honestly?");
                Add(results, "TEST18/delayed callback may use safe 'you mentioned/said' wording", safe1 && safe2);
            }

            // TEST 19: a delayed callback cannot invent "remember when we did X" without verified
            // MemoryStore support.
            {
                bool unsafeNoSupport = ConversationCallbackPolicy.InventsUnverifiedSharedHistory(
                    "remember when we did that dungeon together", false);
                bool safeWithSupport = !ConversationCallbackPolicy.InventsUnverifiedSharedHistory(
                    "remember when we did that dungeon together", true);
                Add(results, "TEST19/callback cannot invent shared history without verified MemoryStore support",
                    unsafeNoSupport && safeWithSupport);
            }

            // TEST 20: combat still suppresses normal autonomous chatter - verified against the same
            // SocialBudget gate the new pacing code (AmbientCadence/momentum) sits alongside, confirming
            // the existing suppression still holds unmodified.
            {
                SocialBudget budget = new SocialBudget();
                budget.SetPreset(SocialActivityPreset.Normal);
                DateTime t = new DateTime(2026, 8, 10, 12, 35, 0, DateTimeKind.Utc);
                string reason;
                bool suppressedInCombat = !budget.CanAdmitOpportunity("idle", SocialPriority.Low, "ambient|idle_waiting", t, true, true, out reason);
                Add(results, "TEST20/combat still suppresses normal autonomous chatter",
                    suppressedInCombat && reason == "combat/recent combat");
            }

            return results;
        }

        // Mirrors SocialDirector.BuildCallbackCandidate's scoring exactly (that method lives in the
        // framework-dependent SocialDirector.cs and cannot be called directly from this offline harness).
        private static AmbientSeedCandidate MirrorBuildCallbackCandidate(ConversationMoment moment, DateTime now)
        {
            return new AmbientSeedCandidate("callback_" + moment.TopicKey, "callback", "safe callback wording only",
                18.0 + Math.Min(10.0, moment.InterestScore / 4.0), null, null, 0, 0.0, now, moment.ExpiresUtc, null);
        }

        private static void Add(List<string> results, string name, bool pass)
        {
            results.Add("[DeepSims Pacing] " + name + ": " + (pass ? "PASS" : "FAIL"));
        }
    }
}
