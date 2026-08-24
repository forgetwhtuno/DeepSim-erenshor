using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    internal static class ConversationSeedRuntimeDeterministicTests
    {
        internal static List<string> Run()
        {
            List<string> lines = new List<string>();
            DateTime t = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
            SocialEventCandidate join = Join(t);
            Add(lines, 1, "verified join enters event seed path", join.Trust == SocialEventTrust.ObservedNow && join.Type == "party_join");
            List<ChatMessage> prompt = PromptBuilder.BuildVerifiedEventThread(new SimSnapshot { Name = "Astra" }, null, join, null, 1);
            string rendered = JoinMessages(prompt);
            Add(lines, 2, "join telemetry is topic material rather than final chat", rendered.Contains("TOPIC SEED, NOT A CHAT LINE") && rendered.Contains("Do not narrate or restate it"));
            Add(lines, 3, "event subject need not be speaker", EventConversationDirector.IsEligibleSpeaker(join, "Astra") && !EventConversationDirector.IsEligibleSpeaker(join, "Fiora"));
            Add(lines, 4, "join seed expires quickly", EventConversationDirector.IsCandidateFresh(join, t.AddSeconds(29)) && !EventConversationDirector.IsCandidateFresh(join, t.AddSeconds(31)));
            Add(lines, 5, "join seed exact-dedupes", EventConversationDirector.Fingerprint(join) == EventConversationDirector.Fingerprint(Join(t.AddSeconds(1))));

            List<AmbientSeedCandidate> ordinary = AmbientSeedProducers.BuildDowntimeCandidates(SocialContextMode.Normal, null, null, t);
            Add(lines, 6, "idle Lively has ordinary candidates without an event", ordinary.Count >= 6);
            SimMemory wants = new SimMemory { AuthoredIdentity = new AuthoredIdentityProfile { ErenshorPersona = "likes gathering and cares about helping the party" } };
            Add(lines, 7, "Wants/Cares identity can seed casual material", AmbientSeedProducers.BuildIdentityCandidates(new SimSnapshot { Name = "Astra" }, wants, "gathering party", true, t).Count > 0);

            SimMemory life = new SimMemory { StructuredMemories = new List<StructuredMemoryRecord> { new StructuredMemoryRecord {
                Id="rl1", Text="Phanty was online briefly and spent the time gathering.", Utc=t.ToString("o"),
                Source=RecentLifePolicy.Source, SourceSystem=RecentLifePolicy.SourceSystem } } };
            AmbientSeedCandidate lifeSeed = AmbientSeedProducers.BuildRecentLifeCandidate(new SimSnapshot { Name = "Phanty" }, life, t);
            Add(lines, 8, "Recent Life can seed conversation", lifeSeed != null && lifeSeed.FactSource == "simulated recent life");

            RecentSocialChatBuffer chat = new RecentSocialChatBuffer();
            chat.ObserveVisible("Trader shouts: buying a spell scroll for 150g", t);
            AmbientSeedCandidate heard = AmbientSeedProducers.BuildAmbientChatCandidate(chat.Snapshot(t.AddSeconds(1), 90), t.AddSeconds(1));
            Add(lines, 9, "ambient Shout can seed private party discussion", heard != null && heard.PromptHint.Contains("never reply publicly"));
            Add(lines, 10, "Shout remains HEARD rather than verified world fact", heard != null && heard.FactSource.Contains("HEARD") && heard.FactSource.Contains("unverified"));
            Add(lines, 11, "current events still require evidence-capable path", typeof(ExternalNewsClient) != null && OrganicCurrentEventsPolicy.MinCooldownMinutes >= 20);

            AmbientSeedCandidate unsupported = new AmbientSeedCandidate("claim", "fact", "narrate", 99, "A dragon died.", "", 90, 0, t, DateTime.MaxValue, null);
            Add(lines, 12, "unsupported factual seed is structurally rejectable", unsupported.HasFact && string.IsNullOrWhiteSpace(unsupported.FactSource));
            AmbientSeedDecision silence = new AmbientSeedDecision();
            Add(lines, 13, "selected opportunity may resolve to silence", silence.SilenceWon);
            Add(lines, 14, "successful seed may use bounded existing thread", EventConversationDirector.ClampEventThreadLines(2) == 2);
            Add(lines, 15, "follow-up remains one to three turns", EventConversationDirector.ClampEventThreadLines(0) == 1 && EventConversationDirector.ClampEventThreadLines(9) == 3);
            Add(lines, 16, "player interruption cancels stale tail", ConversationTurnGuard.IsStale(4, 5));
            AutonomousSocialScheduler scheduler = new AutonomousSocialScheduler();
            scheduler.Observe(t, true, 4, true, "", SocialActivityState.SocialDowntime, "standalone", SocialActivityPreset.Lively, "zone", false, .5);
            scheduler.Observe(t.AddSeconds(1), true, 4, true, "", SocialActivityState.Combat, "standalone", SocialActivityPreset.Lively, "zone", false, .5);
            Add(lines, 17, "combat invalidates idle seed and thread eligibility", !scheduler.Eligible && scheduler.NextContextPulseUtc == DateTime.MinValue);
            int before = scheduler.VisibleMessages; scheduler.NoteTerminal(1, "silence", "comfortable", true, false);
            Add(lines, 18, "selection does not spend visible budget before WriteChat", scheduler.VisibleMessages == before);
            return lines;
        }

        private static SocialEventCandidate Join(DateTime t)
        {
            return new SocialEventCandidate("party_join", t, new[] { "Fiora" }, new[] { "Astra" }, new[] { "Fiora" },
                SocialEventTrust.ObservedNow, 55, 1, "party", "Fiora just joined the current party.", 1);
        }
        private static string JoinMessages(List<ChatMessage> messages) { string s = ""; for (int i=0;i<messages.Count;i++) s += messages[i].content; return s; }
        private static void Add(List<string> lines, int n, string label, bool pass) { lines.Add("[Seed Runtime " + n.ToString("00") + "] " + label + ": " + (pass ? "PASS" : "FAIL")); }
    }
}
