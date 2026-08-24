using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ErenshorDeepSims
{
    // Optional public surface discovered by Practice Duels through reflection.  It contains only
    // verified, fact-shaped values.  Deep Sims never returns a gameplay decision through this API.
    public static class DuelEventBridge
    {
        public static void NotifyDuelEvent(string eventType, string opponent, string scope,
            string decision, string outcome, string winner, string yielded,
            string reasonToken, string reason)
        {
            DeepSimsPlugin plugin = DeepSimsPlugin.Instance;
            if (plugin == null) return;

            VerifiedDuelEvent value;
            if (!VerifiedDuelEvent.TryCreate(eventType, opponent, scope, decision, outcome,
                winner, yielded, reasonToken, reason, out value)) return;
            DuelSocialIntegration.Handle(plugin, value, "structured");
        }
    }

    // Runtime adapter only.  Social admission/expression still goes through the existing
    // EventConversationDirector -> SocialBudget -> QueueVerifiedEventConversation pipeline.
    // This class owns no scheduler, no personality registry, and no gameplay state.
    internal static class DuelSocialIntegration
    {
        private static readonly object DedupLock = new object();
        private static readonly DuelEventDeduplicator Dedup = new DuelEventDeduplicator(6.0);
        private static readonly FieldInfo PluginDirectorField = AccessTools.Field(typeof(DeepSimsPlugin), "_director");
        private static readonly FieldInfo EventDirectorField = AccessTools.Field(typeof(SocialDirector), "_eventConversations");
        private static int _acceptedEvents;
        private static int _duplicateEvents;
        private static string _lastType = string.Empty;
        private static string _lastSource = string.Empty;

        internal static void ResetRuntimeState()
        {
            lock (DedupLock)
            {
                Dedup.Clear();
                _acceptedEvents = 0;
                _duplicateEvents = 0;
                _lastType = string.Empty;
                _lastSource = string.Empty;
            }
        }

        internal static void Handle(DeepSimsPlugin plugin, VerifiedDuelEvent value, string source)
        {
            if (plugin == null || value == null || !plugin.EnabledConfig.Value) return;
            plugin.NotifyCompetitiveCombatSemantic("duel", value.Type, value.ReasonToken);
            lock (DedupLock)
            {
                if (!Dedup.TryAccept(value, DateTime.UtcNow))
                {
                    _duplicateEvents++;
                    return;
                }
            }

            _acceptedEvents++;
            _lastType = value.Type;
            _lastSource = string.IsNullOrWhiteSpace(source) ? "unknown" : source;

            List<SimSnapshot> active = plugin.GetActiveDeepSims();
            bool opponentDeep = DuelSocialPolicy.OpponentIsCurrentDeepSim(value, active);

            // Contract v4 exposes DuelId/participants through PracticeDuelEvents.SemanticEvent. When
            // that optional surface is bound, DuelV4SemanticBridge owns the correlated Living Social
            // episode. Keep this legacy uncorrelated path only for older Duel builds / binding gaps.
            if (!DuelV4SemanticBridge.IsBound)
            {
                List<string> socialParticipants = new List<string> { "player" };
                if (!string.IsNullOrWhiteSpace(value.Opponent)) socialParticipants.Add(value.Opponent);
                List<string> socialTags = new List<string> { "duel", "competitiveness" };
                if (value.IsCompleted && value.Yielded == "opponent") socialTags.Add("duel_win");
                if (value.IsCompleted && value.Yielded == "player") socialTags.Add("duel_loss");
                string socialSummary = value.MemorySummary();
                if (string.IsNullOrWhiteSpace(socialSummary))
                    socialSummary = "A verified Practice Duel event occurred with " + (string.IsNullOrWhiteSpace(value.Opponent) ? "a nearby Sim" : value.Opponent) + ": " + value.Type + ".";
                string socialEventId = SocialEpisodeLedger.StableId("duel|" + value.Fingerprint() + "|" + DateTime.UtcNow.Ticks.ToString());
                plugin.RecordLivingSocialEpisode("duel", string.Empty, socialEventId, socialSummary, socialParticipants, string.Empty,
                    DuelSocialPolicy.Importance(value), socialTags, 0f, value.IsHostileInterruption ? .45f : .15f, value.IsCompleted);
            }

            // One compact verified social memory at terminal completion only.  RecordSharedEvent
            // writes only to current Deep Sims, so a nearby non-party opponent never gains a Deep
            // Sim identity or memory file just because Duel can target it.
            if (!DuelV4SemanticBridge.IsBound && DuelSocialPolicy.ShouldPersistMemory(value))
            {
                string summary = value.MemorySummary();
                if (!string.IsNullOrWhiteSpace(summary))
                    plugin.RecordSharedEvent("friendly_duel", summary, 70, false);
            }

            double chance = DuelSocialPolicy.ReactionChance(value, opponentDeep);
            if (chance <= 0.0) return;

            List<string> eligible = DuelSocialPolicy.EligibleSpeakers(value, active);
            if (eligible.Count == 0) return;

            EventConversationDirector director = GetEventDirector(plugin);
            if (director == null) return;

            // Completion uses the established friendly_duel routing key so the existing expression
            // path enforces its one-post-duel-line cap. Hostile interruption keeps its medium-priority
            // canonical type and has only one eligible speaker, so real combat can suppress the line.
            // The authoritative lifecycle type always remains inside DUEL_EVENT.
            string routingType = value.Type == "duel_completed" ? "friendly_duel" : value.Type;
            string[] entities = string.IsNullOrWhiteSpace(value.Opponent)
                ? new string[0] : new string[] { value.Opponent };
            List<string> involved = new List<string>();
            if (!string.IsNullOrWhiteSpace(value.Opponent)) involved.Add(value.Opponent);

            SocialEventCandidate candidate = new SocialEventCandidate(
                routingType,
                DateTime.UtcNow,
                involved,
                eligible,
                entities,
                value.Type == "duel_completed" ? SocialEventTrust.Experienced : SocialEventTrust.ObservedNow,
                DuelSocialPolicy.Importance(value),
                value.Type == "duel_completed" ? 1.0 : 0.75,
                "duel",
                value.VerifiedContext(),
                chance);
            director.Submit(candidate);
        }

        internal static bool TryHandleGeneric(DeepSimsPlugin plugin, string type, string description)
        {
            if (!DuelSocialPolicy.IsTransportType(type)) return false;
            VerifiedDuelEvent value;
            if (!VerifiedDuelEvent.TryParseTransport(type, description, out value)) return true;
            Handle(plugin, value, "generic-fallback");
            return true;
        }

        internal static string Describe()
        {
            return "events=" + _acceptedEvents + ", dedup=" + _duplicateEvents +
                (string.IsNullOrWhiteSpace(_lastType) ? string.Empty :
                    ", last=" + _lastType + " via " + _lastSource);
        }

        private static EventConversationDirector GetEventDirector(DeepSimsPlugin plugin)
        {
            try
            {
                SocialDirector social = PluginDirectorField == null ? null : PluginDirectorField.GetValue(plugin) as SocialDirector;
                return social == null || EventDirectorField == null
                    ? null : EventDirectorField.GetValue(social) as EventConversationDirector;
            }
            catch { return null; }
        }
    }

    // Current/older Practice Duels may use the generic observed-event method.  Intercept only duel
    // event types before generic telemetry/memory processing, then feed the exact same structured
    // adapter.  A current structured call never invokes this fallback, while a misbehaving caller
    // that sends both is collapsed by DuelEventDeduplicator.
    [HarmonyPatch(typeof(DeepSimsPlugin), "NotifyObservedGameEvent")]
    internal static class DuelGenericObservedEventPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(DeepSimsPlugin __instance, string type, string description)
        {
            return !DuelSocialIntegration.TryHandleGeneric(__instance, type, description);
        }
    }

    // Auto mode may use deterministic templates for small verified duel phrases even when Ollama is
    // healthy.  This extends the existing ritual classification; it does not alter direct chat mode.
    [HarmonyPatch(typeof(SocialPolicy), "IsTrivialRitualEvent")]
    internal static class DuelRitualPolicyPatch
    {
        [HarmonyPostfix]
        private static void Postfix(string type, ref bool __result)
        {
            if (!__result && DuelSocialPolicy.IsCanonicalDuelType(type)) __result = true;
        }
    }

    // DuelReactionChance remains the user-facing multiplier.  The candidate's verified lifecycle
    // policy supplies the base chance, preventing the legacy friendly_duel special case from turning
    // every completed/cancelled duel into a guaranteed probability pass.
    [HarmonyPatch(typeof(DeepSimsPlugin), "GetEventReactionChance")]
    internal static class DuelReactionChancePatch
    {
        [HarmonyPostfix]
        private static void Postfix(string type, double baseChance, ref double __result)
        {
            if (!string.Equals(type, "friendly_duel", StringComparison.OrdinalIgnoreCase)) return;
            __result = Math.Max(0.0, Math.Min(1.0, __result * Math.Max(0.0, Math.Min(1.0, baseChance))));
        }
    }

    // Replace the legacy generic "gg" renderer only when a fact-shaped DUEL_EVENT is present.
    // Other SocialTemplates behavior is unchanged.
    [HarmonyPatch(typeof(SocialTemplates), "TryRenderEvent")]
    internal static class DuelTemplatePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(SocialEventCandidate candidate, SimSnapshot speaker,
            RelationshipTone tone, ref string message, ref bool __result)
        {
            VerifiedDuelEvent value;
            if (candidate == null || !VerifiedDuelEvent.TryParseVerifiedContext(candidate.VerifiedContext, out value))
                return true;
            __result = DuelTemplateRenderer.TryRender(value, speaker, tone, out message);
            return false;
        }
    }

    // Generic grounding remains the first authority.  If it accepted a line tied to DUEL_EVENT,
    // apply stricter practice-duel semantics so virtual sparring cannot become death/loot/PvP lore
    // or contradict the deterministic accept/decline/result token.
    [HarmonyPatch]
    internal static class DuelGroundingPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(GroundingGuard), "IsGrounded", new Type[]
            {
                typeof(string), typeof(SimMemory), typeof(WorldSnapshot), typeof(string),
                typeof(string), typeof(string).MakeByRefType()
            });
        }

        [HarmonyPostfix]
        private static void Postfix(string reply, SimMemory memory, string verifiedSituation,
            ref string reason, ref bool __result)
        {
            if (!__result || string.IsNullOrWhiteSpace(verifiedSituation)) return;
            VerifiedDuelEvent value;
            if (!VerifiedDuelEvent.TryParseVerifiedContext(verifiedSituation, out value)) return;
            string duelReason;
            if (DuelGroundingPolicy.IsGrounded(reply, value, memory, out duelReason)) return;
            __result = false;
            reason = duelReason;
        }
    }

    // Keep /dsevents test useful in the full game while the standalone regression executable also
    // invokes DuelSocialSemantics directly.
    [HarmonyPatch(typeof(EventConversationDirector), "RunDeterministicSelfTests")]
    internal static class DuelEventSelfTestPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref List<string> __result)
        {
            if (__result == null) __result = new List<string>();
            __result.AddRange(DuelSocialSemantics.RunSelfTests());
        }
    }

    [HarmonyPatch(typeof(SocialDirector), "DescribeEvents")]
    internal static class DuelEventDiagnosticsPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref string __result)
        {
            __result = (__result ?? string.Empty) + "\n[DeepSims Duel] " + DuelSocialIntegration.Describe();
        }
    }
}
