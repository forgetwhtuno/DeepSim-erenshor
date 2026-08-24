using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    internal static class SocialSituationDeterministicTests
    {
        internal static List<string> Run()
        {
            List<string> lines = new List<string>();
            DateTime now = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
            RecentSocialChatBuffer buffer = new RecentSocialChatBuffer();
            buffer.ObserveVisible("Fiora tells the group: anyone want to head out soon?", now);
            buffer.ObserveVisible("Astra shouts: anyone running Bonepits?", now.AddSeconds(1));
            List<RecentSocialLine> recent = buffer.Snapshot(now.AddSeconds(2), 600);
            Add(lines, "party chat enters bounded recent context", recent.Count == 2 && recent[0].Channel == RecentChatChannel.Party);
            Add(lines, "Shout enters read-only ambient context", recent[1].Channel == RecentChatChannel.Shout && recent[1].Public);
            for (int i = 0; i < 30; i++) buffer.ObserveVisible("Person" + i + " says: line " + i, now.AddSeconds(i + 3));
            Add(lines, "recent chat buffer evicts old entries", buffer.Snapshot(now.AddMinutes(1), 600).Count == RecentSocialChatBuffer.Capacity);

            UnansweredTurnTracker unanswered = new UnansweredTurnTracker();
            unanswered.NotePlayer("I'm surprised how much I'm enjoying PvP.", now);
            Add(lines, "meaningful player statement may remain unanswered", unanswered.Current(now.AddSeconds(30)) != null);
            unanswered.NotePlayer("lol", now.AddSeconds(31));
            Add(lines, "trivial line requires no acknowledgement", unanswered.Current(now.AddSeconds(31)) == null);
            unanswered.NotePlayer("I think we should head back soon.", now.AddSeconds(32));
            Add(lines, "unanswered state expires", unanswered.Current(now.AddSeconds(190)) == null);

            ContextPulseDecision speak = ContextPulseDecision.Parse("speakNow=yes\nsilenceStillNatural=no\ncurrentTopic=recent party plans\nreasonCategory=unfinished_turn");
            ContextPulseDecision silence = ContextPulseDecision.Parse("speakNow=no\nsilenceStillNatural=yes\ncurrentTopic=none\nreasonCategory=comfortable_silence");
            Add(lines, "structured pulse may seed a topic", speak != null && speak.SpeakNow && speak.CurrentTopic == "recent party plans");
            Add(lines, "structured pulse may choose silence", silence != null && !silence.SpeakNow && silence.SilenceStillNatural);
            Add(lines, "malformed pulse fails closed", ContextPulseDecision.Parse("talk now please") == null);
            Add(lines, "full-party downtime pulse interval is 20-45s", ContextPulsePolicy.NextSeconds(4, SocialActivityState.SocialDowntime, SocialActivityPreset.Lively, 0) == 20 && ContextPulsePolicy.NextSeconds(4, SocialActivityState.SocialDowntime, SocialActivityPreset.Lively, 1) == 45);
            Add(lines, "one-companion pulses are substantially fewer", ContextPulsePolicy.NextSeconds(1, SocialActivityState.SocialDowntime, SocialActivityPreset.Lively, 0) >= 120);
            Add(lines, "combat suppresses idle pulse", double.IsPositiveInfinity(ContextPulsePolicy.NextSeconds(4, SocialActivityState.Combat, SocialActivityPreset.Lively, 0.5)));
            Add(lines, "base full-party Lively assessment is bounded below three minutes", ContextPulsePolicy.NextSeconds(4, SocialActivityState.ActiveGameplay, SocialActivityPreset.Lively, "standalone", 1) == 90);
            Add(lines, "base one-Sim assessment ceiling is strictly below three minutes", ContextPulsePolicy.NextSeconds(1, SocialActivityState.ActiveGameplay, SocialActivityPreset.Lively, "standalone", 1) == 150);
            Add(lines, "Campmaster downtime remains faster than standalone base", ContextPulsePolicy.NextSeconds(4, SocialActivityState.SocialDowntime, SocialActivityPreset.Lively, "campmaster", 1) < ContextPulsePolicy.NextSeconds(4, SocialActivityState.ActiveGameplay, SocialActivityPreset.Lively, "standalone", 1));
            Add(lines, "standalone downtime uses same finite scheduler", ContextPulsePolicy.NextSeconds(4, SocialActivityState.SocialDowntime, SocialActivityPreset.Lively, "standalone", 1) <= 61);

            SocialFunnelMetrics full = SocialFunnelSimulation.RunTenMinutes(4);
            SocialFunnelMetrics one = SocialFunnelSimulation.RunTenMinutes(1);
            Add(lines, "synthetic full-party funnel produces bounded visible lines", full.ContextPulses > 0 && full.VisibleLines > 0 && full.MaximumVisibleGap < 240);
            Add(lines, "synthetic one-Sim session is substantially quieter", one.ContextPulses < full.ContextPulses && one.VisibleLines < full.VisibleLines);
            Add(lines, "synthetic funnel accounts for every pulse", full.ContextPulses == full.PulseSilence + full.PulseSpeak);
            Add(lines, "social situation is ephemeral by construction", typeof(SocialSituationSnapshot).GetMethod("Save") == null);
            return lines;
        }

        private static void Add(List<string> lines, string name, bool pass)
        {
            lines.Add("[DeepSims Situation] " + name + ": " + (pass ? "PASS" : "FAIL"));
        }
    }
}
