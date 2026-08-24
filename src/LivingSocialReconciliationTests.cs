using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    internal static class LivingSocialReconciliationTests
    {
        internal static List<string> RunSelfTests()
        {
            List<string> lines = new List<string>();
            int failures = 0;
            failures += Check(lines, "PvP/Nemesis correlation canonicalizes to one episode", CorrelationCanonicalizes());
            failures += Check(lines, "Duel correlation remains separate from PvP", DuelCorrelationIsSeparate());
            failures += Check(lines, "correlated social episodes merge rather than duplicate", LedgerMergesCorrelatedEpisodes());
            failures += Check(lines, "empty KnownBy is not public knowledge", EmptyKnowledgeIsPrivate());
            failures += Check(lines, "knowledge intersection requires actual evidence knowledge", KnowledgeIntersectionFailsClosed());
            failures += Check(lines, "affect is episode-caused", AffectNeedsEpisode());
            failures += Check(lines, "temporary irritation decays with active play", AffectDecays());
            failures += Check(lines, "pivot opportunity is deterministic", PivotOpportunityDeterministic());
            failures += Check(lines, "combat/urgent context suppresses response pivot", PivotSuppressionWorks());
            failures += Check(lines, "high anger suppresses response pivot", AngerSuppressesPivot());
            lines.Add(failures == 0
                ? "[LivingSocial] reconciliation self-tests: ALL PASS"
                : "[LivingSocial] reconciliation self-tests summary: " + failures + " test(s) failed");
            return lines;
        }

        private static int Check(List<string> lines, string name, bool passed)
        {
            lines.Add("[LivingSocial] " + name + ": " + (passed ? "PASS" : "FAIL"));
            return passed ? 0 : 1;
        }

        private static bool CorrelationCanonicalizes()
        {
            return SocialEpisodeLedger.CanonicalCorrelation("pvp", "MATCH-7") == "pvp:match-7" &&
                   SocialEpisodeLedger.CanonicalCorrelation("nemesis_pvp", "MATCH-7") == "pvp:match-7";
        }

        private static bool DuelCorrelationIsSeparate()
        {
            return SocialEpisodeLedger.CanonicalCorrelation("duel", "abc") == "duel:abc" &&
                   SocialEpisodeLedger.CanonicalCorrelation("duel", "abc") != SocialEpisodeLedger.CanonicalCorrelation("pvp", "abc");
        }

        private static bool LedgerMergesCorrelatedEpisodes()
        {
            SocialEpisodeLedger ledger = new SocialEpisodeLedger();
            SocialEpisodeRecord first = NewEpisode("pvp-one", "pvp", "m1", "PvP fact", "pvp");
            SocialEpisodeRecord second = NewEpisode("nem-one", "nemesis_pvp", "m1", "Longer correlated Nemesis PvP fact", "nemesis");
            SocialEpisodeRecord a = ledger.AddOrMerge(first);
            SocialEpisodeRecord b = ledger.AddOrMerge(second);
            if (!object.ReferenceEquals(a, b) || ledger.Count != 1) return false;
            return Contains(b.Tags, "pvp") && Contains(b.Tags, "nemesis") && b.FactualSummary.IndexOf("Nemesis", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static SocialEpisodeRecord NewEpisode(string id, string source, string correlation, string fact, string tag)
        {
            SocialEpisodeRecord record = new SocialEpisodeRecord();
            record.EpisodeId = id; record.SourceSystem = source; record.SourceCorrelationId = correlation;
            record.CharacterScope = "slot0"; record.Utc = DateTime.UtcNow.ToString("o"); record.FactualSummary = fact;
            record.Participants = new List<string> { "player", "Cyndara" };
            record.KnownBy = new List<string> { "Cyndara" };
            record.Tags = new List<string> { tag }; record.Importance = 70; record.Provenance = "verified";
            return record;
        }

        private static bool EmptyKnowledgeIsPrivate()
        {
            SocialEpisodeRecord record = NewEpisode("e", "chat", string.Empty, "fact", "social");
            record.KnownBy.Clear();
            SimSnapshot sim = new SimSnapshot(); sim.Key = "sim-1"; sim.Name = "Cyndara";
            return !SocialKnowledgePolicy.CanKnow(record, sim, "slot0");
        }

        private static bool KnowledgeIntersectionFailsClosed()
        {
            SocialCurationEvidence evidence = new SocialCurationEvidence();
            evidence.KnownBy = new List<string> { "Phanty" };
            List<SocialCurationEvidence> rows = new List<SocialCurationEvidence> { evidence };
            return !SocialKnowledgePolicy.IntersectsKnowledge(rows, "sim-cyndara", "Cyndara");
        }

        private static bool AffectNeedsEpisode()
        {
            SocialAffectState state = new SocialAffectState();
            if (state.Describe() != "baseline") return false;
            SocialEpisodeRecord episode = NewEpisode("fun", "chat", string.Empty, "A shared funny moment happened.", "humor");
            episode.HumorScore = .8f;
            SocialAffectPolicy.Apply(state, episode, "Cyndara");
            return state.Amusement > 0f;
        }

        private static bool AffectDecays()
        {
            SocialAffectState state = new SocialAffectState();
            state.Irritation = .8f; state.LastActiveSeconds = 1.0;
            state.Decay(901.0);
            return state.Irritation > 0f && state.Irritation < .8f;
        }

        private static bool PivotOpportunityDeterministic()
        {
            bool a = SocialPivotPolicy.HasOpportunity("slot0|Cyndara|42");
            bool b = SocialPivotPolicy.HasOpportunity("slot0|Cyndara|42");
            return a == b && Math.Abs(SocialPivotPolicy.OpportunityRate - .20) < .0001;
        }

        private static bool PivotSuppressionWorks()
        {
            SocialAffectState state = new SocialAffectState();
            return !SocialPivotPolicy.CanOffer(true, "how are you", false, false, false, state) &&
                   !SocialPivotPolicy.CanOffer(false, "heal me now", false, false, false, state);
        }

        private static bool AngerSuppressesPivot()
        {
            SocialAffectState state = new SocialAffectState(); state.Anger = .7f;
            return !SocialPivotPolicy.CanOffer(false, "how are you", false, false, false, state);
        }

        private static bool Contains(List<string> values, string value)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Count; i++) if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
