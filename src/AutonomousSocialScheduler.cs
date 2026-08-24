using System;
using System.Collections.Generic;
using System.Text;

namespace ErenshorDeepSims
{
    internal sealed class AutonomousSocialScheduler
    {
        private const int RecentCapacity = 40;
        private readonly object _sync = new object();
        private readonly List<AutonomousSocialRecord> _recent = new List<AutonomousSocialRecord>();
        private string _signature = string.Empty;
        private long _nextId;

        internal DateTime LastTickUtc { get; private set; }
        internal DateTime NextBaseOpportunityUtc { get; private set; }
        internal DateTime NextContextPulseUtc { get; private set; }
        internal DateTime LastContextPulseUtc { get; private set; }
        internal string LastOpportunityResult { get; private set; }
        internal string LastOpportunityReason { get; private set; }
        internal string LastContextPulseResult { get; private set; }
        internal bool Eligible { get; private set; }
        internal string BlockReason { get; private set; }
        internal int SchedulerTicks { get; private set; }
        internal int Opportunities { get; private set; }
        internal int InferenceRequests { get; private set; }
        internal int SilenceResults { get; private set; }
        internal int SpeakResults { get; private set; }
        internal int VisibleMessages { get; private set; }
        internal int BlockedResults { get; private set; }
        internal double MaximumContextEvaluationGapSeconds { get; private set; }

        internal void Observe(DateTime now, bool characterReady, int partyCount, bool authority,
            string authorityReason, SocialActivityState activity, string activitySource,
            SocialActivityPreset preset, string scene, bool currentThreadActive, double roll)
        {
            lock (_sync)
            {
                LastTickUtc = now;
                SchedulerTicks++;
                if (NextBaseOpportunityUtc == DateTime.MinValue)
                    NextBaseOpportunityUtc = now.AddSeconds(15.0);

                string reason = !characterReady ? "character_not_ready" :
                    partyCount <= 0 ? "no_eligible_party" :
                    !authority ? Safe(authorityReason, "autonomous_authority_denied") :
                    currentThreadActive ? "current_thread_active" :
                    !ContextPulsePolicy.IsEligibleActivity(activity)
                        ? "activity_" + activity.ToString().ToLowerInvariant() : string.Empty;
                Eligible = reason.Length == 0;
                BlockReason = Eligible ? "none" : reason;

                string signature = characterReady + "|" + partyCount + "|" + authority + "|" + activity + "|" +
                    (activitySource ?? string.Empty) + "|" + preset + "|" + (scene ?? string.Empty) + "|" + currentThreadActive;
                if (!string.Equals(signature, _signature, StringComparison.Ordinal))
                {
                    _signature = signature;
                    Rearm(now, partyCount, activity, activitySource, preset, roll);
                }
                else if (Eligible && (NextContextPulseUtc == DateTime.MinValue || NextContextPulseUtc == DateTime.MaxValue))
                    Rearm(now, partyCount, activity, activitySource, preset, roll);
                else if (!Eligible)
                {
                    NextContextPulseUtc = DateTime.MinValue;
                    NextBaseOpportunityUtc = now.AddSeconds(15.0);
                }
            }
        }

        internal bool TryTakeDue(DateTime now, int partyCount, SocialActivityState activity,
            SocialActivityPreset preset, string activitySource, double roll, out long opportunityId)
        {
            lock (_sync)
            {
                opportunityId = 0;
                if (!Eligible || NextContextPulseUtc == DateTime.MinValue || now < NextContextPulseUtc) return false;
                opportunityId = ++_nextId;
                Opportunities++;
                double delay = ContextPulsePolicy.NextSeconds(partyCount, activity, preset, activitySource, roll);
                NextContextPulseUtc = FiniteFuture(now, delay);
                NextBaseOpportunityUtc = NextContextPulseUtc;
                Add(new AutonomousSocialRecord(opportunityId, now, activity, partyCount, activitySource,
                    "opportunity_created", "due", false, "not_started", false));
                LastOpportunityResult = "opportunity_created";
                LastOpportunityReason = "due";
                return true;
            }
        }

        internal void NoteInferenceStarted(long id, DateTime now)
        {
            lock (_sync)
            {
                InferenceRequests++;
                if (LastContextPulseUtc != DateTime.MinValue)
                    MaximumContextEvaluationGapSeconds = Math.Max(MaximumContextEvaluationGapSeconds,
                        Math.Max(0.0, (now - LastContextPulseUtc).TotalSeconds));
                LastContextPulseUtc = now;
                LastContextPulseResult = "started";
                Update(id, "context_inference_started", "none", true, "started", false);
            }
        }

        internal void NoteTerminal(long id, string result, string reason, bool inferenceStarted, bool visible)
        {
            lock (_sync)
            {
                string safeResult = Safe(result, "unknown");
                LastOpportunityResult = safeResult;
                LastOpportunityReason = Safe(reason, "none");
                if (inferenceStarted)
                {
                    LastContextPulseResult = safeResult;
                    if (safeResult == "silence" || safeResult == "context_chose_silence") SilenceResults++;
                    if (safeResult == "speak" || safeResult == "queued") SpeakResults++;
                }
                if (visible) VisibleMessages++;
                if (safeResult == "blocked" || safeResult == "failed" || safeResult == "stale") BlockedResults++;
                Update(id, safeResult, LastOpportunityReason, inferenceStarted, safeResult, visible);
            }
        }

        internal void RequestRefresh() { lock (_sync) { _signature = string.Empty; } }

        internal string DescribeStatus(DateTime now)
        {
            lock (_sync)
            {
                return "scheduler=" + (LastTickUtc == DateTime.MinValue ? "not-started" : "alive") +
                    " nextBase=" + Eta(NextBaseOpportunityUtc, now) + " nextPulse=" + Eta(NextContextPulseUtc, now) +
                    " block=" + Safe(BlockReason, "unknown") + " lastOpportunity=" + Safe(LastOpportunityResult, "none") +
                    "/" + Safe(LastOpportunityReason, "none") + " lastPulse=" + Age(LastContextPulseUtc, now) +
                    "/" + Safe(LastContextPulseResult, "none");
            }
        }

        internal string DescribeRecent(DateTime now, int maximum)
        {
            lock (_sync)
            {
                if (_recent.Count == 0) return "[DeepSims Social Recent] no scheduler evaluations recorded.";
                StringBuilder sb = new StringBuilder("[DeepSims Social Recent]");
                int start = Math.Max(0, _recent.Count - Math.Max(1, maximum));
                for (int i = start; i < _recent.Count; i++)
                {
                    AutonomousSocialRecord r = _recent[i];
                    sb.Append("\n").Append(Math.Round(Math.Max(0, (now - r.Utc).TotalSeconds))).Append("s ago")
                        .Append(" activity=").Append(r.Activity).Append(" party=").Append(r.PartyCount)
                        .Append(" source=").Append(Safe(r.Source, "unknown")).Append(" outcome=").Append(r.Outcome)
                        .Append(" reason=").Append(Safe(r.Reason, "none")).Append(" inference=")
                        .Append(r.InferenceStarted ? Safe(r.InferenceResult, "started") : "no")
                        .Append(" visible=").Append(r.Visible ? "yes" : "no");
                }
                return sb.ToString();
            }
        }

        private void Rearm(DateTime now, int partyCount, SocialActivityState activity, string activitySource,
            SocialActivityPreset preset, double roll)
        {
            if (!Eligible) { NextContextPulseUtc = DateTime.MinValue; NextBaseOpportunityUtc = now.AddSeconds(15.0); return; }
            NextContextPulseUtc = FiniteFuture(now, ContextPulsePolicy.NextSeconds(partyCount, activity, preset, activitySource, roll));
            NextBaseOpportunityUtc = NextContextPulseUtc;
        }

        private void Add(AutonomousSocialRecord record)
        {
            _recent.Add(record);
            while (_recent.Count > RecentCapacity) _recent.RemoveAt(0);
        }

        private void Update(long id, string outcome, string reason, bool inferenceStarted, string inferenceResult, bool visible)
        {
            for (int i = _recent.Count - 1; i >= 0; i--)
            {
                if (_recent[i].Id != id) continue;
                _recent[i].Outcome = outcome; _recent[i].Reason = reason;
                _recent[i].InferenceStarted = inferenceStarted; _recent[i].InferenceResult = inferenceResult;
                _recent[i].Visible = visible; return;
            }
        }

        private static DateTime FiniteFuture(DateTime now, double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) seconds = 60.0;
            return now.AddSeconds(seconds);
        }
        private static string Eta(DateTime value, DateTime now) { return value == DateTime.MinValue ? "none" : Math.Max(0, Math.Ceiling((value - now).TotalSeconds)) + "s"; }
        private static string Age(DateTime value, DateTime now) { return value == DateTime.MinValue ? "never" : Math.Round(Math.Max(0, (now - value).TotalSeconds)) + "s-ago"; }
        private static string Safe(string value, string fallback) { return string.IsNullOrWhiteSpace(value) ? fallback : value.Replace('\r', ' ').Replace('\n', ' ').Trim(); }
    }

    internal sealed class AutonomousSocialRecord
    {
        internal readonly long Id; internal readonly DateTime Utc; internal readonly SocialActivityState Activity;
        internal readonly int PartyCount; internal readonly string Source;
        internal string Outcome, Reason, InferenceResult; internal bool InferenceStarted, Visible;
        internal AutonomousSocialRecord(long id, DateTime utc, SocialActivityState activity, int partyCount,
            string source, string outcome, string reason, bool inferenceStarted, string inferenceResult, bool visible)
        { Id = id; Utc = utc; Activity = activity; PartyCount = partyCount; Source = source; Outcome = outcome;
          Reason = reason; InferenceStarted = inferenceStarted; InferenceResult = inferenceResult; Visible = visible; }
    }

    internal static class StandaloneDowntimePolicy
    {
        internal static SocialActivityState Resolve(bool characterReady, int partyCount, bool combatOrGrace,
            bool competitiveActive, bool sceneStable, bool sustainedMovement, bool meaningfulActivity,
            double stationarySeconds, double secondsSinceMeaningfulActivity)
        {
            if (!characterReady || partyCount <= 0 || !sceneStable) return SocialActivityState.Unknown;
            if (combatOrGrace || competitiveActive) return SocialActivityState.Combat;
            if (sustainedMovement || meaningfulActivity) return SocialActivityState.ActiveGameplay;
            return stationarySeconds >= 60.0 && secondsSinceMeaningfulActivity >= 60.0
                ? SocialActivityState.SocialDowntime : SocialActivityState.ActiveGameplay;
        }
    }

    internal static class CampmasterContextConsistencyPolicy
    {
        internal static bool IsStale(SocialActivityState state, string reason, double stationarySeconds,
            double outOfCombatSeconds, double meaningfulGameplayAge)
        {
            if (state != SocialActivityState.ActiveGameplay) return false;
            string token = (reason ?? string.Empty).Trim().ToLowerInvariant();
            // These are positive current/readiness signals, not age-derived claims. Deep Sims must
            // not second-guess them merely because the position counters are high.
            if (token == "native_pull_active" || token == "pvp_active" || token == "duel_active" ||
                token == "gameplay_not_ready" || token == "scene_changed" || token == "recent_displacement") return false;
            bool longSafeWindow = stationarySeconds >= 90.0 && outOfCombatSeconds >= 90.0 && meaningfulGameplayAge >= 60.0;
            if (token == "stationary_threshold_pending" && stationarySeconds >= 60.0) return true;
            if ((token == "meaningful_gameplay" || token == "recent_interaction") && meaningfulGameplayAge >= 60.0) return true;
            return longSafeWindow;
        }
    }
}
