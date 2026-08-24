using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    // Deterministic coverage for party-conversation turn ownership (ConversationTurnGuard). These tests
    // exercise the pure decision logic that backs the async generation/staleness pipeline in
    // DeepSimsPlugin.cs (QueuePartyChatResponse / ContinueConversationThreadAsync / etc.), which itself
    // cannot compile in the offline regression harness because it depends on framework/Unity.
    internal static class ConversationTurnGuardTests
    {
        internal static List<string> Run()
        {
            List<string> results = new List<string>();

            // TEST 1: player topic A, AI reply A, player topic B arrives before queued continuation A
            // displays -> old continuation A must never display. Modeled as: the generation captured by
            // continuation-A's work becomes stale the instant topic B increments the live generation.
            {
                long genAtQueueTime = 1;
                long liveGeneration = 1;
                bool continuationAStillFresh = !ConversationTurnGuard.IsStale(genAtQueueTime, liveGeneration);
                liveGeneration = 2; // player sends topic B, generation advances
                bool continuationAMustDiscard = ConversationTurnGuard.IsStale(genAtQueueTime, liveGeneration);
                Add(results, "TEST1/continuation queued under old generation is stale once player advances it",
                    continuationAStillFresh && continuationAMustDiscard);
            }

            // TEST 2: player sends two messages during PartyReadDelay -> reply reflects both messages,
            // not just the first. Modeled as: the recent window built after message B arrives contains
            // both A and B in order.
            {
                List<ConversationLine> history = new List<ConversationLine>
                {
                    new ConversationLine("Player", "I really like tanking more than dps"),
                    new ConversationLine("Player", "but healing looks fun too")
                };
                List<ConversationLine> window = ConversationTurnGuard.BuildRecentWindow(history, 5);
                bool sawBoth = window.Count == 2 &&
                    window[0].Text.IndexOf("tanking", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    window[1].Text.IndexOf("healing", StringComparison.OrdinalIgnoreCase) >= 0;
                Add(results, "TEST2/recent window reflects both player messages sent during the read delay", sawBoth);
            }

            // TEST 3: AI line 1 displays, continuation waits, player speaks during the wait with a
            // subject change -> the continuation must recognize the topic changed from the newest window.
            {
                List<ConversationLine> window = new List<ConversationLine>
                {
                    new ConversationLine("Player", "how is this fight going anyway"),
                    new ConversationLine("Brinon", "That boss hits hard, watch the target.")
                };
                bool changed = ConversationTurnGuard.TopicChanged(window,
                    "I think this zone looks better than the last one", PromptBuilder.ClassifyThreadTopic);
                Add(results, "TEST3/subject change from the current fight to the zone is detected", changed);

                bool unchanged = ConversationTurnGuard.TopicChanged(window,
                    "yeah that boss fight was rough", PromptBuilder.ClassifyThreadTopic);
                Add(results, "TEST3/staying on the same subject is not flagged as a change", !unchanged);
            }

            // TEST 4: old LLM request finishes after generation changed -> result discarded before
            // display. Same staleness contract as TEST1, checked at the post-generation/pre-display
            // boundary specifically.
            {
                long workGeneration = 5;
                long liveGeneration = 6; // advanced while the model call was in flight
                bool discardedBeforeDisplay = ConversationTurnGuard.IsStale(workGeneration, liveGeneration);
                Add(results, "TEST4/late-finishing generation result is discarded before display", discardedBeforeDisplay);
            }

            // TEST 5: same speaker is not automatically chosen twice in a row in a continued thread.
            {
                bool blockedWithoutReason = !ConversationTurnGuard.AllowSameSpeakerAgain("Brinon", "Brinon", false, false);
                bool allowedWhenReaddressed = ConversationTurnGuard.AllowSameSpeakerAgain("Brinon", "Brinon", true, false);
                bool allowedWhenOnlyOne = ConversationTurnGuard.AllowSameSpeakerAgain("Brinon", "Brinon", false, true);
                bool allowedDifferentSpeaker = ConversationTurnGuard.AllowSameSpeakerAgain("Dancer", "Brinon", false, false);
                Add(results, "TEST5/same speaker blocked back-to-back without a reason", blockedWithoutReason);
                Add(results, "TEST5/same speaker allowed when directly re-addressed", allowedWhenReaddressed);
                Add(results, "TEST5/same speaker allowed when they are the only eligible speaker", allowedWhenOnlyOne);
                Add(results, "TEST5/different speaker is never blocked by this rule", allowedDifferentSpeaker);
            }

            // Player priority remains bounded: it suppresses unrelated autonomous admission during a
            // direct exchange, then releases the floor after the quiet window.
            {
                DateTime direct = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
                bool blocksDuringExchange = ConversationTurnGuard.DirectConversationOwnsTurn(
                    direct.Ticks, direct.AddSeconds(5).Ticks, 12.0);
                bool releasesAfterWindow = !ConversationTurnGuard.DirectConversationOwnsTurn(
                    direct.Ticks, direct.AddSeconds(13).Ticks, 12.0);
                bool noPriorDirectTurnAllowsBanter = !ConversationTurnGuard.DirectConversationOwnsTurn(
                    0L, direct.Ticks, 12.0);
                Add(results, "priority/direct player exchange suppresses conflicting autonomous admission",
                    blocksDuringExchange);
                Add(results, "priority/non-conflicting autonomous banter resumes after bounded quiet window",
                    releasesAfterWindow && noPriorDirectTurnAllowsBanter);
            }

            // TEST 6: MaxAutonomousThreadReplies is a hard upper bound - verify a thread can stop early
            // (not always fill to the cap) and can never exceed the cap regardless of hook strength.
            {
                bool stopsEarlyWithoutHook = !ConversationTurnGuard.ShouldContinueThread(1, 4, false);
                bool continuesWithHookUnderCap = ConversationTurnGuard.ShouldContinueThread(1, 4, true);
                bool neverExceedsCapEvenWithHook = !ConversationTurnGuard.ShouldContinueThread(4, 4, true);
                Add(results, "TEST6/thread stops early when there is no conversational hook", stopsEarlyWithoutHook);
                Add(results, "TEST6/thread may continue below the cap when there is a hook", continuesWithHookUnderCap);
                Add(results, "TEST6/cap is a hard upper bound even with a strong hook", neverExceedsCapEvenWithHook);
            }

            // Supporting coverage: noise/diagnostic lines never enter the recent window or anchor topic
            // detection.
            {
                List<ConversationLine> history = new List<ConversationLine>
                {
                    new ConversationLine("Player", "I really like tanking more than dps"),
                    new ConversationLine("Player", "/dsperf"),
                    new ConversationLine("Player", "WTB healing potions 5g"),
                    new ConversationLine("Brinon", "Tanking keeps me busy for sure.")
                };
                List<ConversationLine> window = ConversationTurnGuard.BuildRecentWindow(history, 5);
                bool noiseExcluded = true;
                for (int i = 0; i < window.Count; i++)
                {
                    if (window[i].Text.StartsWith("/", StringComparison.Ordinal) ||
                        window[i].Text.StartsWith("WTB", StringComparison.OrdinalIgnoreCase))
                        noiseExcluded = false;
                }
                Add(results, "support/diagnostic and WTB spam excluded from the recent window", noiseExcluded && window.Count == 2);
            }

            // TEST 7: a direct Sim name mention is treated as a conversational hook even without a
            // question mark or hedge word, so continuation-turn speaker selection can favor that Sim.
            {
                List<string> names = new List<string> { "Phanty", "Cyndara", "Dancer" };
                bool namedMentionIsHook = ConversationTurnGuard.HasConversationalHook(
                    "Phanty would probably pull half the zone if we let him", names);
                bool plainStatementNoHook = ConversationTurnGuard.HasConversationalHook(
                    "the weather in this zone is nice today", names);
                Add(results, "TEST7/direct Sim name mention counts as a conversational hook", namedMentionIsHook);
                Add(results, "TEST7/unrelated statement with no name/question/hedge is not a hook", !plainStatementNoHook);
            }

            // TEST 8: a question in the newest visible line is a hook, so the thread controller has a
            // real reason to consider a continuation opportunity (subject to the hard cap).
            {
                bool questionIsHook = ConversationTurnGuard.HasConversationalHook("do you think the tank has it harder?");
                bool questionAllowsContinuation = ConversationTurnGuard.ShouldContinueThread(0, 4, questionIsHook);
                Add(results, "TEST8/question in newest line is a conversational hook", questionIsHook);
                Add(results, "TEST8/question hook creates a continuation opportunity under the cap", questionAllowsContinuation);
            }

            // TEST 9: trivial acknowledgements ("lol", "yeah", "nice", "gg") carry no hook, so the
            // thread controller stops instead of forcing another AI line onto a dead-end reply.
            {
                string[] trivialLines = new string[] { "lol", "yeah", "nice", "gg", "ok" };
                bool allTrivialStopThread = true;
                for (int i = 0; i < trivialLines.Length; i++)
                {
                    bool hook = ConversationTurnGuard.HasConversationalHook(trivialLines[i]);
                    if (hook || ConversationTurnGuard.ShouldContinueThread(0, 4, hook)) allTrivialStopThread = false;
                }
                Add(results, "TEST9/trivial acknowledgements never produce a hook and stop the thread", allTrivialStopThread);
            }

            // TEST 10: a player topic change terminates the old thread - reusing TEST3's TopicChanged
            // contract but from the controller's perspective (old thread must not continue past it).
            {
                List<ConversationLine> oldThread = new List<ConversationLine>
                {
                    new ConversationLine("Player", "that boss fight was rough"),
                    new ConversationLine("Cyndara", "yeah that fight nearly wiped us")
                };
                bool topicChanged = ConversationTurnGuard.TopicChanged(
                    ConversationTurnGuard.BuildRecentWindow(oldThread, 5),
                    "anyway, where should we go next zone-wise", PromptBuilder.ClassifyThreadTopic);
                Add(results, "TEST10/player topic change is detected and must terminate the prior thread", topicChanged);
            }

            // TEST 11: speaker rotation - the same speaker should not be picked twice in a row absent a
            // re-address, matching the party-of-several example in the shape (player -> A -> B -> ...).
            {
                bool rotatesAwayFromLastSpeaker = ConversationTurnGuard.AllowSameSpeakerAgain("Dancer", "Cyndara", false, false) &&
                    !ConversationTurnGuard.AllowSameSpeakerAgain("Cyndara", "Cyndara", false, false);
                Add(results, "TEST11/speaker rotation avoids repeating the immediately previous speaker", rotatesAwayFromLastSpeaker);
            }

            // TEST 12: no unexplained continuation after the previously-visible context vanished. If the
            // newest visible line no longer relates to the thread's original subject, and the classifier
            // reports a real topic change, the thread must not silently keep replying to the old subject.
            {
                List<ConversationLine> vanishedContextThread = new List<ConversationLine>
                {
                    new ConversationLine("Player", "that boss fight was rough"),
                    new ConversationLine("Brinon", "yeah that fight nearly wiped us")
                };
                bool contextVanished = ConversationTurnGuard.TopicChanged(
                    ConversationTurnGuard.BuildRecentWindow(vanishedContextThread, 5),
                    "I like the music in this zone we're in", PromptBuilder.ClassifyThreadTopic);
                Add(results, "TEST12/no unexplained continuation once the visible topic has moved on", contextVanished);
            }

            // TEST 13: thread never exceeds the configured hard cap regardless of hook strength, mirrored
            // against a couple of concrete MaxAutonomousThreadRepliesConfig-style values (2 and 6).
            {
                bool capTwoHonored = !ConversationTurnGuard.ShouldContinueThread(2, 2, true);
                bool capSixHonored = !ConversationTurnGuard.ShouldContinueThread(6, 6, true);
                bool capSixAllowsUnderneath = ConversationTurnGuard.ShouldContinueThread(5, 6, true);
                Add(results, "TEST13/hard cap of 2 is honored even with a strong hook", capTwoHonored);
                Add(results, "TEST13/hard cap of 6 is honored even with a strong hook", capSixHonored);
                Add(results, "TEST13/one reply below a cap of 6 with a hook may still continue", capSixAllowsUnderneath);
            }


            // TEST 14: exact live repro. The old worker can pass its final pre-queue check for the pull
            // topic, then the player immediately changes to classes. The queue boundary itself must
            // therefore reject the old generation; a check only before QueueGroupMessage is racy.
            {
                long oldPullGeneration = 20;
                long liveGeneration = 20;
                bool oldWorkerPassedPreQueueCheck = !ConversationTurnGuard.IsStale(oldPullGeneration, liveGeneration);
                string oldTopic = "slow pulls on the boss are better than chain pulls";
                string newTopic = "actually nevermind what classes do you guys enjoy?";
                liveGeneration = 21; // fresh player line clears the old turn and takes ownership
                bool queueBoundaryMustRejectOldPullReply = ConversationTurnGuard.IsStale(oldPullGeneration, liveGeneration);
                bool topicActuallyChanged = PromptBuilder.ClassifyThreadTopic(oldTopic) != PromptBuilder.ClassifyThreadTopic(newTopic);
                Add(results, "TEST14/topic switch after old pre-queue check is rejected at queue boundary",
                    oldWorkerPassedPreQueueCheck && queueBoundaryMustRejectOldPullReply && topicActuallyChanged);
            }

            // TEST 15: the scheduled typing object itself retains the generation/context that created
            // it, so final display can independently discard a line even if an earlier checkpoint missed it.
            {
                DateTime now = new DateTime(2026, 8, 10, 18, 13, 0, DateTimeKind.Utc);
                GroupMessageQueue queue = new GroupMessageQueue();
                queue.Enqueue(now, "Dancer", "slow pulls are better", false, 30, "party");
                List<ScheduledGroupMessage> due = queue.TakeDue(now);
                bool retainedOwnership = due.Count == 1 && due[0].ConversationGeneration == 30 &&
                    string.Equals(due[0].DiagnosticContext, "party", StringComparison.Ordinal);
                bool finalBoundarySeesStale = retainedOwnership && ConversationTurnGuard.IsStale(due[0].ConversationGeneration, 31);
                Add(results, "TEST15/scheduled line retains generation for final-display stale rejection",
                    retainedOwnership && finalBoundarySeesStale);
            }

            // TEST 16: a hypothetical/preference question ("what class would you play if you weren't
            // your current one?") must not be killed by the "no verified fact" path. It is a hook (it
            // carries a '?'), and a fully-supported hypothetical answer ("if I weren't a Paladin I'd
            // probably be a Druid") must pass grounding with no gameplay provenance at all.
            {
                bool questionIsHook = ConversationTurnGuard.HasConversationalHook(
                    "what class would you play if you weren't your current one?");
                SimSnapshot phanty = new SimSnapshot { Name = "Phanty", ClassName = "Paladin" };
                WorldSnapshot world = new WorldSnapshot { Party = new List<SimSnapshot> { phanty } };
                SimMemory memory = new SimMemory(); memory.Normalize(); memory.Name = "Phanty";
                string reason;
                bool hypotheticalAnswerGrounded = GroundingGuard.IsGrounded(
                    "if I weren't a Paladin I'm just a Druid honestly", memory, world, string.Empty, out reason);
                Add(results, "TEST16/hypothetical preference question is a hook and needs no verified fact",
                    questionIsHook && hypotheticalAnswerGrounded);
            }

            // TEST 17: trivial acknowledgements ("gg"/"ok"/"yeah") still stop the thread even under a
            // generous cap - restates TEST9's contract explicitly against the three named examples from
            // the quality-pass request.
            {
                bool ggStops = !ConversationTurnGuard.HasConversationalHook("gg") &&
                    !ConversationTurnGuard.ShouldContinueThread(0, 6, ConversationTurnGuard.HasConversationalHook("gg"));
                bool okStops = !ConversationTurnGuard.HasConversationalHook("ok") &&
                    !ConversationTurnGuard.ShouldContinueThread(0, 6, ConversationTurnGuard.HasConversationalHook("ok"));
                bool yeahStops = !ConversationTurnGuard.HasConversationalHook("yeah") &&
                    !ConversationTurnGuard.ShouldContinueThread(0, 6, ConversationTurnGuard.HasConversationalHook("yeah"));
                Add(results, "TEST17/gg-ok-yeah trivial acknowledgements still stop the thread", ggStops && okStops && yeahStops);
            }

            // TEST 18: the prompt sent for a continuation turn must anchor on the newest visible line,
            // not merely the line that opened the thread, so the second Sim engages what was actually
            // just said rather than re-answering the original prompt.
            {
                List<ConversationLine> thread = new List<ConversationLine>
                {
                    new ConversationLine("Player", "phanty do you think camping one good spot is better than dungeon crawling?"),
                    new ConversationLine("Phanty", "actually, yeah. the grind feels too much like a chore sometimes."),
                };
                SimSnapshot cyndara = new SimSnapshot { Name = "Cyndara", ClassName = "Arcanist" };
                SimMemory memory = new SimMemory(); memory.Normalize(); memory.Name = "Cyndara";
                List<ChatMessage> messages = PromptBuilder.BuildPartyThreadReply(cyndara, memory, null, thread, 2, null);
                string joined = string.Empty;
                for (int i = 0; i < messages.Count; i++) joined += "\n" + messages[i].content;
                bool anchorsOnNewestLine = joined.IndexOf("MOST RECENT PARTY MESSAGE", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    joined.IndexOf("the grind feels too much like a chore", StringComparison.OrdinalIgnoreCase) >= 0;
                Add(results, "TEST18/continuation prompt anchors on the newest visible line", anchorsOnNewestLine);
            }

            // TEST 19: an unsupported future/shared-plan phrase ("next run" with no verified plan) must
            // not be allowed through grounding, matching the existing unsupported-"again" pattern.
            {
                SimMemory plainMemory = new SimMemory(); plainMemory.Normalize(); plainMemory.Name = "Dancer";
                WorldSnapshot plainWorld = new WorldSnapshot();
                string reason;
                bool nextRunRejected = !GroundingGuard.IsGrounded("we should hit this again next run", plainMemory, plainWorld, string.Empty, out reason);
                bool whenWeGoBackRejected = !GroundingGuard.IsGrounded("when we go back here it'll be easier", plainMemory, plainWorld, string.Empty, out reason);
                Add(results, "TEST19/unsupported future shared-plan phrase is rejected", nextRunRejected && whenWeGoBackRejected);
            }

            // TEST 20: application shutdown invalidates queued/autonomous output. Modeled the same way
            // DeepSimsPlugin.OnDestroy invalidates work: advance the generation and clear the typing
            // queue together (GroupMessageQueue.Clear mirrors AdvanceConversationGeneration(true)).
            {
                DateTime now = new DateTime(2026, 8, 10, 20, 0, 0, DateTimeKind.Utc);
                GroupMessageQueue queue = new GroupMessageQueue();
                long liveGeneration = 40;
                queue.Enqueue(now.AddSeconds(1), "Phanty", "queued autonomous line", true, (int)liveGeneration, "conversation_continuation");
                bool queuedBeforeShutdown = queue.Count == 1;
                // Shutdown: stop new work, advance generation, clear the queue.
                List<ScheduledGroupMessage> invalidated = queue.Clear();
                liveGeneration++;
                bool queueEmptiedByShutdown = queue.Count == 0 && invalidated.Count == 1;
                bool invalidatedLineIsNowStale = ConversationTurnGuard.IsStale(invalidated[0].ConversationGeneration, liveGeneration);
                Add(results, "TEST20/shutdown clears queued autonomous output and invalidates its generation",
                    queuedBeforeShutdown && queueEmptiedByShutdown && invalidatedLineIsNowStale);
            }

            // Unsupported generated quest-guide specificity is not game truth. Retrieved/verified
            // support may still authorize the same locator through the ordinary grounding boundary.
            {
                SimMemory plainMemory = new SimMemory(); plainMemory.Normalize(); plainMemory.Name = "Cyndara";
                WorldSnapshot plainWorld = new WorldSnapshot();
                string reason;
                bool inventedRejected = !GroundingGuard.IsGrounded(
                    "I thought it was the dungeon map quest, check page 4.", plainMemory, plainWorld,
                    string.Empty, null, out reason);
                bool supportedAllowed = GroundingGuard.IsGrounded(
                    "check page 4 for the dungeon map quest", plainMemory, plainWorld,
                    string.Empty, "The dungeon map quest instructions say to check page 4.", out reason);
                Add(results, "grounding/unsupported generated quest-page guidance is rejected",
                    inventedRejected && supportedAllowed);
            }

            // TEST 21: no visible line can be displayed after shutdown generation invalidation, even for
            // a line that was already past TakeDue and only needs the final-display check.
            {
                DateTime now = new DateTime(2026, 8, 10, 20, 0, 5, DateTimeKind.Utc);
                GroupMessageQueue queue = new GroupMessageQueue();
                long liveGeneration = 50;
                queue.Enqueue(now, "Dancer", "line generated just before shutdown", false, (int)liveGeneration, "party");
                liveGeneration++; // shutdown advances the generation before this line is flushed
                List<ScheduledGroupMessage> due = queue.TakeDue(now);
                bool finalDisplayMustReject = due.Count == 1 && ConversationTurnGuard.IsStale(due[0].ConversationGeneration, liveGeneration);
                Add(results, "TEST21/no visible line survives final-display check after shutdown generation invalidation",
                    finalDisplayMustReject);
            }

            return results;
        }

        private static void Add(List<string> results, string name, bool pass)
        {
            results.Add("[DeepSims TurnGuard] " + name + ": " + (pass ? "PASS" : "FAIL"));
        }
    }
}
