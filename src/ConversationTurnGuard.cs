using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    // Pure, Unity/framework-free helpers for party-conversation turn ownership. DeepSimsPlugin.cs owns
    // the actual generation counter (_partyConversationGeneration) and the async request pipeline; this
    // class holds the deterministic decision logic so it can be covered by the offline regression suite
    // (tests/RUN_DETERMINISTIC_TESTS.ps1) without needing the game assemblies.
    //
    // Core rule: a fresh player /p line always outranks an in-flight AI conversation thread. Every work
    // item derived from a player message carries the generation that was current when it was created;
    // once the live generation advances past that value the work is stale and must not display.
    internal static class ConversationTurnGuard
    {
        // True once the generation that owns a piece of work is no longer the live generation - the
        // work must be discarded silently rather than displayed or used to spawn further work.
        internal static bool IsStale(long workGeneration, long currentGeneration)
        {
            return workGeneration != currentGeneration;
        }

        // A completed direct reply does not instantly release the party floor to unrelated ambient
        // inference. Keep a short, bounded quiet window so a player's next direct line owns the
        // exchange; autonomous speech remains eligible again once that window expires.
        internal static bool DirectConversationOwnsTurn(long lastDirectPlayerUtcTicks, long nowUtcTicks,
            double quietSeconds)
        {
            if (lastDirectPlayerUtcTicks <= 0 || nowUtcTicks < lastDirectPlayerUtcTicks) return false;
            long quietTicks = (long)(Math.Max(0.0, quietSeconds) * TimeSpan.TicksPerSecond);
            return nowUtcTicks - lastDirectPlayerUtcTicks < quietTicks;
        }

        // Lines that are tactical commands, spam, or bookkeeping rather than actual banter. These must
        // never anchor topic detection or count toward the recent conversational window.
        internal static bool IsNoiseLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            string m = text.Trim().ToLowerInvariant();
            if (m.Length == 0) return true;
            if (m[0] == '/') return true; // slash commands / diagnostics
            string[] starts = new string[]
            {
                "wtb ", "wts ", "wtt ", "casting ", "assisting ", "attacking ", "killing ", "following ",
                "pulling ", "target is ", "roger", "aye aye", "consider it done", "on it", "lead on",
                "lets do this", "let's do this", "[deepsims", "[dsperf", "[deep sims"
            };
            for (int i = 0; i < starts.Length; i++) if (m.StartsWith(starts[i], StringComparison.Ordinal)) return true;
            if (m.Contains("'s target is ") || m.Contains(" is on a ") || m.Contains(" and so am i")) return true;
            return false;
        }

        // Bounded, chronological window of the last meaningful visible party messages (player + Deep
        // Sim + relevant vanilla lines). Diagnostic commands, tactical spam, and system output are
        // excluded so a stale subject cannot leak back into a fresh reply.
        internal static List<ConversationLine> BuildRecentWindow(IList<ConversationLine> history, int maxLines)
        {
            List<ConversationLine> result = new List<ConversationLine>();
            if (history == null || history.Count == 0) return result;
            int take = Math.Max(1, Math.Min(5, maxLines));
            for (int i = history.Count - 1; i >= 0 && result.Count < take; i--)
            {
                ConversationLine line = history[i];
                if (line == null || IsNoiseLine(line.Text)) continue;
                result.Insert(0, line);
            }
            return result;
        }

        // Compares the subject of the newest visible line in the window against a candidate message.
        // A blank/general classification on either side is not evidence of a change by itself.
        internal static bool TopicChanged(IList<ConversationLine> recentWindow, string latestText,
            Func<string, string> classify)
        {
            if (classify == null || string.IsNullOrWhiteSpace(latestText)) return false;
            if (recentWindow == null || recentWindow.Count == 0) return false;
            string previousText = null;
            for (int i = recentWindow.Count - 1; i >= 0; i--)
            {
                if (recentWindow[i] != null && !string.IsNullOrWhiteSpace(recentWindow[i].Text)) { previousText = recentWindow[i].Text; break; }
            }
            if (previousText == null) return false;
            string oldTopic = classify(previousText);
            string newTopic = classify(latestText);
            if (string.IsNullOrWhiteSpace(oldTopic) || string.IsNullOrWhiteSpace(newTopic)) return false;
            if (string.Equals(oldTopic, "general party chat", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(newTopic, "general party chat", StringComparison.OrdinalIgnoreCase)) return false;
            return !string.Equals(oldTopic, newTopic, StringComparison.OrdinalIgnoreCase);
        }

        // A natural hook to keep a thread going: a direct question, disagreement/contrast, or a named
        // address. Reaching the reply cap is never itself a reason to continue - MaxAutonomousThreadReplies
        // is a cap, not a target.
        internal static bool HasConversationalHook(string text)
        {
            return HasConversationalHook(text, null);
        }

        // Overload used by continuation-turn speaker/topic selection: a line that names another present
        // Sim ("Phanty would probably pull half the zone") is just as strong a hook as a question or an
        // explicit disagreement, so it must not be dropped merely because it lacks '?' or a hedge word.
        internal static bool HasConversationalHook(string text, IList<string> knownSimNames)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string m = text.Trim().ToLowerInvariant();
            if (m.IndexOf('?') >= 0) return true;
            string[] hooks = new string[]
            {
                "actually", "but ", "no,", "no ", "wait", "disagree", "really?", "seriously", "hmm",
                "though", "on the other hand", "i think", "i don't think", "i dont think"
            };
            for (int i = 0; i < hooks.Length; i++) if (m.Contains(hooks[i])) return true;
            if (knownSimNames != null)
            {
                for (int i = 0; i < knownSimNames.Count; i++)
                {
                    string name = knownSimNames[i];
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (m.IndexOf(name.Trim().ToLowerInvariant(), StringComparison.Ordinal) >= 0) return true;
                }
            }
            return false;
        }

        // MaxAutonomousThreadReplies is a hard upper bound, never a target: a thread only continues when
        // both the cap allows another line AND there is a real conversational reason to keep going.
        internal static bool ShouldContinueThread(int repliesSoFar, int hardCap, bool hasHook)
        {
            if (repliesSoFar >= Math.Max(0, hardCap)) return false;
            return hasHook;
        }

        // Avoid the same Sim speaking twice in a row unless the player re-addressed them, another Sim
        // asked them directly, or no other eligible speaker is present.
        internal static bool AllowSameSpeakerAgain(string candidateName, string previousSpeaker,
            bool wasReaddressed, bool onlyEligibleSpeaker)
        {
            if (string.IsNullOrWhiteSpace(candidateName) || string.IsNullOrWhiteSpace(previousSpeaker)) return true;
            if (!string.Equals(candidateName, previousSpeaker, StringComparison.OrdinalIgnoreCase)) return true;
            return wasReaddressed || onlyEligibleSpeaker;
        }
    }
}
