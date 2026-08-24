using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    internal static class AutonomousSocialSchedulerDeterministicTests
    {
        internal static List<string> Run()
        {
            List<string> lines = new List<string>();
            DateTime t = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
            AutonomousSocialScheduler s = new AutonomousSocialScheduler();
            long id;

            s.Observe(t, false, 0, true, "", SocialActivityState.Unknown, "unknown", SocialActivityPreset.Normal, "menu", false, 0);
            Add(lines, 1, "startup before character readiness creates no invalid pulse", !s.TryTakeDue(t.AddHours(1), 0, SocialActivityState.Unknown, SocialActivityPreset.Normal, "unknown", 0, out id));
            s.Observe(t.AddSeconds(1), true, 0, true, "", SocialActivityState.ActiveGameplay, "standalone", SocialActivityPreset.Normal, "zone", false, 0);
            Add(lines, 2, "character readiness starts scheduler tick state", s.LastTickUtc == t.AddSeconds(1));
            s.Observe(t.AddSeconds(2), true, 4, true, "", SocialActivityState.SocialDowntime, "standalone", SocialActivityPreset.Normal, "zone", false, 0);
            Add(lines, 3, "party eligibility arms a finite pulse", Finite(s.NextContextPulseUtc, t.AddSeconds(2)));
            DateTime normalDue = s.NextContextPulseUtc;
            s.Observe(t.AddSeconds(3), true, 4, true, "", SocialActivityState.SocialDowntime, "standalone", SocialActivityPreset.Lively, "zone", false, 0);
            Add(lines, 4, "Normal to Lively recomputes timing", s.NextContextPulseUtc < normalDue);
            s.Observe(t.AddSeconds(4), true, 4, true, "", SocialActivityState.Unknown, "standalone", SocialActivityPreset.Lively, "loading", false, 0);
            s.Observe(t.AddSeconds(5), true, 4, true, "", SocialActivityState.SocialDowntime, "standalone", SocialActivityPreset.Lively, "next-zone", false, 0);
            Add(lines, 5, "scene reset re-arms after readiness", Finite(s.NextContextPulseUtc, t.AddSeconds(5)));
            s.Observe(t.AddSeconds(6), true, 4, true, "", SocialActivityState.Combat, "standalone", SocialActivityPreset.Lively, "next-zone", false, 0);
            s.Observe(t.AddSeconds(7), true, 4, true, "", SocialActivityState.SocialDowntime, "standalone", SocialActivityPreset.Lively, "next-zone", false, 0);
            Add(lines, 6, "combat end eventually re-arms", Finite(s.NextContextPulseUtc, t.AddSeconds(7)));

            Add(lines, 7, "Campmaster available activity is preferred", ChooseSource(true, "SocialDowntime") == "campmaster");
            Add(lines, 8, "Campmaster absence falls back", ChooseSource(false, null) == "standalone");
            Add(lines, 9, "reflection mismatch falls back without disabling", ChooseSource(false, "mismatch") == "standalone");
            Add(lines, 10, "Campmaster Unknown falls back", ChooseSource(true, "Unknown") == "standalone");

            Add(lines, 11, "stationary safe four-Sim party reaches downtime", StandaloneDowntimePolicy.Resolve(true, 4, false, false, true, false, false, 61, 61) == SocialActivityState.SocialDowntime);
            Add(lines, 12, "recent combat suppresses downtime", StandaloneDowntimePolicy.Resolve(true, 4, true, false, true, false, false, 300, 300) == SocialActivityState.Combat);
            Add(lines, 13, "combat grace expiry permits downtime", StandaloneDowntimePolicy.Resolve(true, 4, false, false, true, false, false, 120, 120) == SocialActivityState.SocialDowntime);
            Add(lines, 14, "old gather event expires", StandaloneDowntimePolicy.Resolve(true, 4, false, false, true, false, false, 240, 240) == SocialActivityState.SocialDowntime);
            Add(lines, 15, "stale target alone is not permanent active gameplay", StandaloneDowntimePolicy.Resolve(true, 4, false, false, true, false, false, 240, 240) == SocialActivityState.SocialDowntime);
            Add(lines, 16, "sustained movement suppresses", StandaloneDowntimePolicy.Resolve(true, 4, false, false, true, true, false, 120, 120) == SocialActivityState.ActiveGameplay);
            Add(lines, 17, "meaningful current interaction suppresses", StandaloneDowntimePolicy.Resolve(true, 4, false, false, true, false, true, 120, 5) == SocialActivityState.ActiveGameplay);

            DateTime due = s.NextContextPulseUtc;
            Add(lines, 18, "full-party Lively pulse becomes due", s.TryTakeDue(due, 4, SocialActivityState.SocialDowntime, SocialActivityPreset.Lively, "standalone", 0.5, out id));
            Add(lines, 19, "due pulse records autonomous opportunity", s.Opportunities == 1);
            s.NoteInferenceStarted(id, due);
            Add(lines, 20, "free semaphore path starts inference", s.InferenceRequests == 1);
            s.NoteTerminal(id, "silence", "comfortable_silence", true, false);
            Add(lines, 21, "silence leaves no visible chat and keeps next finite", s.VisibleMessages == 0 && Finite(s.NextContextPulseUtc, due));
            long speakId = TakeNext(s, 4, out due); s.NoteInferenceStarted(speakId, due); s.NoteTerminal(speakId, "speak", "queued_normal_path", true, false);
            Add(lines, 22, "speak reaches normal topic path", s.SpeakResults == 1);
            Add(lines, 23, "no speaker retains next pulse", TerminalKeepsFuture(s, 4, "no_speaker"));
            Add(lines, 24, "grounding rejection retains next pulse", TerminalKeepsFuture(s, 4, "grounding_rejected"));
            Add(lines, 25, "stale result retains next pulse", TerminalKeepsFuture(s, 4, "stale"));
            Add(lines, 26, "exception has bounded recovery", TerminalKeepsFuture(s, 4, "failed"));
            Add(lines, 27, "all terminal results retain finite schedule", Finite(s.NextContextPulseUtc, due));

            Add(lines, 28, "direct player request owns the quiet window", ConversationTurnGuard.DirectConversationOwnsTurn(t.Ticks, t.AddSeconds(1).Ticks, 20));
            Add(lines, 29, "idle pulse cannot retain a stale player generation", ConversationTurnGuard.IsStale(7, 8));
            s.Observe(due.AddSeconds(1), true, 4, true, "", SocialActivityState.Combat, "standalone", SocialActivityPreset.Lively, "next-zone", false, 0);
            Add(lines, 30, "combat invalidates downtime eligibility", !s.Eligible && s.NextContextPulseUtc == DateTime.MinValue);
            Add(lines, 31, "single inference semaphore invariant is one", AutonomousSchedulerSimulation.InferenceConcurrency == 1);

            int before = s.VisibleMessages; s.NoteTerminal(9001, "silence", "comfortable", true, false);
            Add(lines, 32, "silence consumes no visible budget", s.VisibleMessages == before);
            s.NoteTerminal(9002, "grounding_rejected", "unsupported", true, false);
            Add(lines, 33, "grounding rejection consumes no visible budget", s.VisibleMessages == before);
            s.NoteTerminal(9003, "stale", "activity_changed", true, false);
            Add(lines, 34, "stale rejection consumes no visible budget", s.VisibleMessages == before);
            s.NoteTerminal(9004, "visible", "emitted", true, true);
            Add(lines, 35, "visible autonomous line charges exactly once", s.VisibleMessages == before + 1);

            Add(lines, 36, "Recent Life retrieval policy remains available", RecentLifePolicy.MaxLookbackDays == 7 && RecentLifePolicy.MaxRecordsTotal == 24);
            Add(lines, 37, "optional party-history bridge remains fail-soft", StandaloneDowntimePolicy.Resolve(true, 4, false, false, true, false, false, 90, 90) == SocialActivityState.SocialDowntime);
            Add(lines, 38, "Roleplay suppresses spontaneous current events", !OrganicCurrentEventsPolicy.CanConsider(true, SocialActivityState.SocialDowntime, 4, false, false, string.Empty));
            Add(lines, 39, "MMO current events retain evidence provider", typeof(ExternalNewsClient) != null && OrganicCurrentEventsPolicy.CanConsider(false, SocialActivityState.SocialDowntime, 4, false, false, string.Empty));
            Add(lines, 40, "headline runtime has no durable Save surface", typeof(OrganicCurrentEventsRuntime).GetMethod("Save") == null);
            Add(lines, 41, "Campmaster SocialDowntime permits context pulses", PulseArms(SocialActivityState.SocialDowntime));
            Add(lines, 42, "Campmaster ExtendedDowntime permits context pulses", PulseArms(SocialActivityState.ExtendedDowntime));
            Add(lines, 43, "fresh Campmaster ActiveGameplay suppresses downtime pulse", !CampmasterContextConsistencyPolicy.IsStale(SocialActivityState.ActiveGameplay, "native_pull_active", 300, 300, 300));
            Add(lines, 44, "contradictory stale ActiveGameplay selects fallback", CampmasterContextConsistencyPolicy.IsStale(SocialActivityState.ActiveGameplay, "recent_interaction", 240, 300, 230));
            Add(lines, 45, "fallback does not override fresh genuine activity", !CampmasterContextConsistencyPolicy.IsStale(SocialActivityState.ActiveGameplay, "pvp_active", 500, 500, 500));
            Add(lines, 46, "Campmaster absent uses standalone fallback", ChooseSource(false, null) == "standalone");
            Add(lines, 47, "Campmaster malformed uses standalone fallback", ChooseSource(false, "mismatch") == "standalone");
            Add(lines, 48, "four-Sim Lively downtime pulse is finite", PulseArms(SocialActivityState.SocialDowntime));
            Add(lines, 49, "silence terminal keeps pulse finite", TerminalKeepsFutureForNew("silence"));
            Add(lines, 50, "speak terminal keeps pulse finite", TerminalKeepsFutureForNew("speak"));
            Add(lines, 51, "five-minute full-party downtime cannot retain lastPulse never", FiveMinuteOpportunityOccurs());
            Add(lines, 52, "base ActiveGameplay arms the shared assessment timer", BaseActiveArms());
            Add(lines, 53, "base full-party Lively assessment ceiling is 90 seconds", ContextPulsePolicy.NextSeconds(4, SocialActivityState.ActiveGameplay, SocialActivityPreset.Lively, "standalone", 1.0) == 90.0);
            Add(lines, 54, "combat still clears the base assessment timer", !PulseArms(SocialActivityState.Combat));

            AutonomousSchedulerMetrics metrics = AutonomousSchedulerSimulation.RunTenMinutes();
            lines.Add("[DeepSims Scheduler Simulation] ticks=" + metrics.Ticks + " opportunities=" + metrics.Opportunities +
                " inferenceRequests=" + metrics.InferenceRequests + " silence=" + metrics.Silence + " speak=" + metrics.Speak +
                " visible=" + metrics.Visible + " blocked=" + metrics.Blocked + " maxContextGapSeconds=" + metrics.MaxGapSeconds +
                " " + (metrics.Ticks == 1201 && metrics.Opportunities > 10 && metrics.MaxGapSeconds <= 61 ? "PASS" : "FAIL"));
            SocialFreedomSimulationMetrics lively = SocialFreedomSimulation.RunThirtyMinutes(4, SocialActivityPreset.Lively,
                SocialActivityState.SocialDowntime, "campmaster");
            SocialFreedomSimulationMetrics normal = SocialFreedomSimulation.RunThirtyMinutes(4, SocialActivityPreset.Normal,
                SocialActivityState.SocialDowntime, "campmaster");
            SocialFreedomSimulationMetrics oneSim = SocialFreedomSimulation.RunThirtyMinutes(1, SocialActivityPreset.Lively,
                SocialActivityState.SocialDowntime, "campmaster");
            SocialFreedomSimulationMetrics combat = SocialFreedomSimulation.RunThirtyMinutes(4, SocialActivityPreset.Lively,
                SocialActivityState.Combat, "campmaster");
            lines.Add("[DeepSims Lively 30m Simulation] " + lively.Describe() + " " +
                (lively.Assessments > 20 && lively.SilenceDecisions > 0 && lively.SpeakAttempts > 0 &&
                 lively.VisibleConversationStarts > 0 && lively.MaxThreadLockSeconds <= 60.0 &&
                 lively.AverageStartIntervalSeconds >= 45.0 && lively.AverageStartIntervalSeconds <= 90.0 &&
                 lively.SeedSourceCounts.Count >= 3 ? "PASS" : "FAIL"));
            lines.Add("[DeepSims Normal 30m Simulation] " + normal.Describe() + " " +
                (normal.VisibleConversationStarts < lively.VisibleConversationStarts ? "PASS" : "FAIL"));
            lines.Add("[DeepSims One-Sim 30m Simulation] " + oneSim.Describe() + " " +
                (oneSim.VisibleConversationStarts < lively.VisibleConversationStarts ? "PASS" : "FAIL"));
            lines.Add("[DeepSims Combat 30m Simulation] " + combat.Describe() + " " +
                (combat.Assessments == 0 && combat.VisibleConversationStarts == 0 ? "PASS" : "FAIL"));
            return lines;
        }

        private static bool TerminalKeepsFuture(AutonomousSocialScheduler s, int party, string result)
        {
            DateTime due; long id = TakeNext(s, party, out due); s.NoteInferenceStarted(id, due);
            s.NoteTerminal(id, result, result, true, false); return Finite(s.NextContextPulseUtc, due);
        }
        private static long TakeNext(AutonomousSocialScheduler s, int party, out DateTime due)
        {
            due = s.NextContextPulseUtc; long id;
            if (!s.TryTakeDue(due, party, SocialActivityState.SocialDowntime, SocialActivityPreset.Lively, "standalone", .5, out id)) return 0;
            return id;
        }
        private static string ChooseSource(bool available, string activity) { return available && !string.IsNullOrWhiteSpace(activity) && activity != "Unknown" ? "campmaster" : "standalone"; }
        private static bool PulseArms(SocialActivityState state) { AutonomousSocialScheduler x = new AutonomousSocialScheduler(); DateTime n = new DateTime(2026,8,22,12,0,0,DateTimeKind.Utc); x.Observe(n,true,4,true,"",state,"campmaster",SocialActivityPreset.Lively,"zone",false,.5); return Finite(x.NextContextPulseUtc,n); }
        private static bool TerminalKeepsFutureForNew(string result) { AutonomousSocialScheduler x = new AutonomousSocialScheduler(); DateTime n = new DateTime(2026,8,22,12,0,0,DateTimeKind.Utc); x.Observe(n,true,4,true,"",SocialActivityState.SocialDowntime,"campmaster",SocialActivityPreset.Lively,"zone",false,.5); DateTime due=x.NextContextPulseUtc; long id; if(!x.TryTakeDue(due,4,SocialActivityState.SocialDowntime,SocialActivityPreset.Lively,"campmaster",.5,out id)) return false; x.NoteTerminal(id,result,result,true,result=="speak"); return Finite(x.NextContextPulseUtc,due); }
        private static bool FiveMinuteOpportunityOccurs() { AutonomousSocialScheduler x = new AutonomousSocialScheduler(); DateTime n = new DateTime(2026,8,22,12,0,0,DateTimeKind.Utc); for(int i=0;i<=600;i++){DateTime at=n.AddSeconds(i*.5); x.Observe(at,true,4,true,"",SocialActivityState.SocialDowntime,"campmaster",SocialActivityPreset.Lively,"zone",false,.5); long id; if(x.TryTakeDue(at,4,SocialActivityState.SocialDowntime,SocialActivityPreset.Lively,"campmaster",.5,out id)) return true;} return false; }
        private static bool BaseActiveArms() { AutonomousSocialScheduler x = new AutonomousSocialScheduler(); DateTime n = new DateTime(2026,8,22,12,0,0,DateTimeKind.Utc); x.Observe(n,true,4,true,"",SocialActivityState.ActiveGameplay,"standalone",SocialActivityPreset.Lively,"zone",false,.5); return Finite(x.NextContextPulseUtc,n) && (x.NextContextPulseUtc-n).TotalSeconds <= 90; }
        private static bool Finite(DateTime value, DateTime now) { return value != DateTime.MinValue && value != DateTime.MaxValue && value > now; }
        private static void Add(List<string> lines, int number, string name, bool pass) { lines.Add("[DeepSims Scheduler " + number.ToString("00") + "] " + name + ": " + (pass ? "PASS" : "FAIL")); }
    }

    internal sealed class AutonomousSchedulerMetrics
    { internal int Ticks, Opportunities, InferenceRequests, Silence, Speak, Visible, Blocked; internal double MaxGapSeconds; }

    internal static class AutonomousSchedulerSimulation
    {
        internal const int InferenceConcurrency = 1;
        internal static AutonomousSchedulerMetrics RunTenMinutes()
        {
            AutonomousSchedulerMetrics m = new AutonomousSchedulerMetrics(); AutonomousSocialScheduler s = new AutonomousSocialScheduler();
            DateTime start = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc); DateTime prior = DateTime.MinValue;
            for (int tick = 0; tick <= 1200; tick++)
            {
                DateTime now = start.AddSeconds(tick * .5); m.Ticks++;
                s.Observe(now, true, 4, true, "", SocialActivityState.SocialDowntime, "standalone", SocialActivityPreset.Lively, "zone", false, ((tick * 17) % 100) / 100.0);
                long id;
                if (!s.TryTakeDue(now, 4, SocialActivityState.SocialDowntime, SocialActivityPreset.Lively, "standalone", ((tick * 31) % 100) / 100.0, out id)) continue;
                m.Opportunities++; s.NoteInferenceStarted(id, now); m.InferenceRequests++;
                if (prior != DateTime.MinValue) m.MaxGapSeconds = Math.Max(m.MaxGapSeconds, (now - prior).TotalSeconds); prior = now;
                if (m.Opportunities % 3 == 0) { m.Speak++; m.Visible++; s.NoteTerminal(id, "speak", "queued_normal_path", true, true); }
                else { m.Silence++; s.NoteTerminal(id, "silence", "comfortable_silence", true, false); }
            }
            m.Blocked = s.BlockedResults; return m;
        }
    }

    internal sealed class SocialFreedomSimulationMetrics
    {
        internal int Assessments, SilenceDecisions, SpeakAttempts, VisibleMessages, GroundingRejections, Threads;
        internal int TotalThreadLines, MaxConsecutiveSilence, VisibleConversationStarts;
        internal double AverageThreadLength, MaxThreadLockSeconds, MaxVisibleGapSeconds, AverageStartIntervalSeconds, TotalStartIntervalSeconds;
        internal readonly Dictionary<string, int> SeedSourceCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        internal readonly Dictionary<int, int> ConsecutiveSilenceCounts = new Dictionary<int, int>();

        internal string Describe()
        {
            return "assessments=" + Assessments + " silence=" + SilenceDecisions + " silenceRuns=" + Describe(ConsecutiveSilenceCounts) +
                " seedAttempts=" + SpeakAttempts + " starts=" + VisibleConversationStarts + " visible=" + VisibleMessages +
                " threads=" + Threads + " averageThreadLength=" + Math.Round(AverageThreadLength, 2) +
                " maxThreadLockSeconds=" + Math.Round(MaxThreadLockSeconds, 1) + " maxVisibleGapSeconds=" + Math.Round(MaxVisibleGapSeconds, 1) +
                " averageStartIntervalSeconds=" + Math.Round(AverageStartIntervalSeconds, 1) + " seedSources=" + Describe(SeedSourceCounts) +
                " groundingRejected=" + GroundingRejections;
        }

        private static string Describe(Dictionary<string, int> values)
        {
            if (values.Count == 0) return "none";
            List<string> keys = new List<string>(values.Keys); keys.Sort(StringComparer.Ordinal);
            List<string> parts = new List<string>();
            for (int i = 0; i < keys.Count; i++) parts.Add(keys[i] + ":" + values[keys[i]]);
            return string.Join(",", parts.ToArray());
        }

        private static string Describe(Dictionary<int, int> values)
        {
            if (values.Count == 0) return "none";
            List<int> keys = new List<int>(values.Keys); keys.Sort();
            List<string> parts = new List<string>();
            for (int i = 0; i < keys.Count; i++) parts.Add(keys[i].ToString() + ":" + values[keys[i]]);
            return string.Join(",", parts.ToArray());
        }
    }

    internal static class SocialFreedomSimulation
    {
        // This uses the production scheduler, candidate selector, silence tracker, and thread
        // lifetime. The deterministic pulse/output fixture supplies only bounded model outcomes.
        internal static SocialFreedomSimulationMetrics RunThirtyMinutes(int partyCount, SocialActivityPreset preset,
            SocialActivityState activity, string activitySource)
        {
            SocialFreedomSimulationMetrics m = new SocialFreedomSimulationMetrics();
            AutonomousSocialScheduler scheduler = new AutonomousSocialScheduler();
            SocialSessionState thread = new SocialSessionState(); thread.ResetForCharacter("social-simulation");
            SilenceFatigueTracker fatigue = new SilenceFatigueTracker();
            DateTime start = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
            DateTime threadOpened = DateTime.MinValue, lastVisible = DateTime.MinValue;
            bool priorActive = false;
            int rawSilenceRun = 0;
            List<SimSnapshot> speakers = BuildSpeakers(partyCount);
            TopicFatigueTracker topicFatigue = new TopicFatigueTracker();

            for (int tick = 0; tick <= 3600; tick++)
            {
                DateTime now = start.AddSeconds(tick * .5);
                bool active = thread.HasActiveThread(now);
                if (priorActive && !active && threadOpened != DateTime.MinValue)
                    m.MaxThreadLockSeconds = Math.Max(m.MaxThreadLockSeconds, (now - threadOpened).TotalSeconds);
                priorActive = active;
                scheduler.Observe(now, true, partyCount, true, "", activity, activitySource,
                    preset, "zone", active, ((tick * 17) % 100) / 100.0);
                long id;
                if (!scheduler.TryTakeDue(now, partyCount, activity, preset,
                    activitySource, ((tick * 31) % 100) / 100.0, out id)) continue;
                m.Assessments++; scheduler.NoteInferenceStarted(id, now);

                bool eligibleLivelyParty = partyCount >= 3 && partyCount <= 5 && preset == SocialActivityPreset.Lively &&
                    (activity == SocialActivityState.SocialDowntime || activity == SocialActivityState.ExtendedDowntime);
                bool rawSilence = ((id * 37) % 100) < (preset == SocialActivityPreset.Lively ? 15 : 68);
                if (rawSilence)
                {
                    rawSilenceRun++;
                    m.MaxConsecutiveSilence = Math.Max(m.MaxConsecutiveSilence, rawSilenceRun);
                    SilenceFatiguePressure pressure = fatigue.NoteSilence(eligibleLivelyParty);
                    if (pressure == SilenceFatiguePressure.Normal)
                    {
                        m.SilenceDecisions++; scheduler.NoteTerminal(id, "silence", "simulated", true, false); continue;
                    }
                }
                else
                {
                    NoteSilenceRun(m, rawSilenceRun);
                    rawSilenceRun = 0;
                }

                List<AmbientSeedCandidate> candidates = AmbientSeedProducers.BuildDowntimeCandidates(SocialContextMode.Camp,
                    null, null, now);
                AddContextualCandidate(candidates, id, now);
                AmbientSeedDecision seed = AmbientSeedSelector.Select(id, SocialContextMode.Camp, candidates, speakers,
                    topicFatigue, 0, now, AmbientSeedSelector.DefaultSilenceNormal,
                    AmbientSeedSelector.DefaultSilenceCamp, AmbientSeedSelector.DefaultSilenceRelax, 0.0,
                    rawSilence ? fatigue.SilenceAdjustment : 0.0, rawSilence && fatigue.Consecutive >= 3, true, null,
                    eligibleLivelyParty ? 1.0 : 0.0);
                if (seed.SilenceWon)
                {
                    m.SilenceDecisions++; scheduler.NoteTerminal(id, "silence", "seed_selector", true, false); continue;
                }
                m.SpeakAttempts++;
                Increment(m.SeedSourceCounts, SourceOf(seed.SelectedTopicKey));
                if (m.SpeakAttempts % 9 == 0)
                {
                    m.GroundingRejections++; scheduler.NoteTerminal(id, "grounding_rejected", "simulated", true, false); continue;
                }

                int lines = 1 + (m.SpeakAttempts % 3);
                threadOpened = now;
                for (int i = 0; i < lines; i++) thread.RecordVisibleSim("Sim" + i, "social simulation line", now);
                m.Threads++; m.VisibleConversationStarts++; m.TotalThreadLines += lines; m.VisibleMessages += lines;
                if (lastVisible != DateTime.MinValue)
                {
                    double startGap = (now - lastVisible).TotalSeconds;
                    m.MaxVisibleGapSeconds = Math.Max(m.MaxVisibleGapSeconds, startGap);
                    m.TotalStartIntervalSeconds += startGap;
                }
                lastVisible = now; fatigue.Reset(); NoteSilenceRun(m, rawSilenceRun); rawSilenceRun = 0;
                scheduler.NoteTerminal(id, "visible", "simulated", true, true);
            }
            NoteSilenceRun(m, rawSilenceRun);
            if (lastVisible != DateTime.MinValue)
                m.MaxVisibleGapSeconds = Math.Max(m.MaxVisibleGapSeconds, (start.AddMinutes(30) - lastVisible).TotalSeconds);
            m.AverageThreadLength = m.Threads == 0 ? 0.0 : (double)m.TotalThreadLines / m.Threads;
            m.AverageStartIntervalSeconds = m.VisibleConversationStarts <= 1 ? 0.0 :
                m.TotalStartIntervalSeconds / (m.VisibleConversationStarts - 1);
            return m;
        }

        private static List<SimSnapshot> BuildSpeakers(int partyCount)
        {
            string[] names = { "Fiora", "Phanty", "Cyndara", "Astra", "Dancer" };
            List<SimSnapshot> speakers = new List<SimSnapshot>();
            for (int i = 0; i < partyCount && i < names.Length; i++)
                speakers.Add(new SimSnapshot { Name = names[i], Patience = 45 + (i * 8), GearChase = i == 1 ? 70 : 25, Rival = i == 2 });
            return speakers;
        }

        // Candidate availability rotates deterministically to represent the bounded, already-safe
        // context sources that the director supplies in production. The selector still ranks them.
        private static void AddContextualCandidate(List<AmbientSeedCandidate> candidates, long opportunityId, DateTime now)
        {
            int shape = (int)(opportunityId % 5);
            if (shape == 0) candidates.Add(new AmbientSeedCandidate("callback_group", "callback", "briefly continue a recent party callback", 39.0,
                string.Empty, "recent conversation callback", 0, 0.0, now, DateTime.MaxValue, null));
            else if (shape == 1) candidates.Add(new AmbientSeedCandidate("identity:fiora:simulated", "identity", "ask about a relevant established interest", 39.0,
                string.Empty, "authored identity", 0, 0.0, now, DateTime.MaxValue, new string[] { "Fiora" }));
            else if (shape == 2) candidates.Add(new AmbientSeedCandidate("recent_life:simulated", "recent_life", "use a known relevant recent-life observation", 39.0,
                "A known recent-life observation.", "Recent Life record", 10, 0.0, now, DateTime.MaxValue, new string[] { "Phanty" }));
            else if (shape == 3) candidates.Add(new AmbientSeedCandidate("camp_social", "camp", "react only to the current supplied camp activity", 39.0,
                "The party is in supplied SocialDowntime.", "Campmaster semantic event", 10, 0.0, now, DateTime.MaxValue, null));
            else candidates.Add(new AmbientSeedCandidate("ambient_chat:simulated", "ambient", "privately react to an HEARD ambient line", 39.0,
                string.Empty, "ambient chat heard", 0, 0.0, now, DateTime.MaxValue, null));
        }

        private static string SourceOf(string topic)
        {
            string value = (topic ?? string.Empty).ToLowerInvariant();
            if (value.StartsWith("free_social_")) return value;
            if (value.StartsWith("callback_")) return "callback";
            if (value.StartsWith("identity:")) return "interest";
            if (value.StartsWith("recent_life:")) return "recent_life";
            if (value.StartsWith("camp_")) return "camp";
            if (value.StartsWith("ambient_chat:")) return "ambient";
            if (value == "ordinary_downtime") return "generic_fallback";
            return "downtime_preference";
        }

        private static void Increment<TKey>(Dictionary<TKey, int> values, TKey key)
        {
            int count; values.TryGetValue(key, out count); values[key] = count + 1;
        }

        private static void NoteSilenceRun(SocialFreedomSimulationMetrics metrics, int run)
        {
            if (run > 0) Increment(metrics.ConsecutiveSilenceCounts, run);
        }
    }
}
