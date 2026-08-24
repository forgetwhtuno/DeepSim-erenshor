using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    internal enum SocialEventTrust
    {
        ObservedNow,
        Experienced
    }

    internal sealed class SocialEventCandidate
    {
        internal readonly string Type;
        internal readonly DateTime ObservedUtc;
        internal readonly List<string> InvolvedNames;
        internal readonly List<string> EligibleSpeakerNames;
        internal readonly List<string> VerifiedEntities;
        internal readonly SocialEventTrust Trust;
        internal readonly int Importance;
        internal readonly double Novelty;
        internal readonly string CooldownCategory;
        internal readonly string VerifiedContext;
        internal readonly double BaseChance;

        internal SocialEventCandidate(string type, DateTime observedUtc, IEnumerable<string> involvedNames,
            IEnumerable<string> eligibleSpeakerNames, IEnumerable<string> verifiedEntities, SocialEventTrust trust,
            int importance, double novelty, string cooldownCategory, string verifiedContext, double baseChance)
        {
            Type = type ?? string.Empty;
            ObservedUtc = observedUtc;
            InvolvedNames = CopyNames(involvedNames);
            EligibleSpeakerNames = CopyNames(eligibleSpeakerNames);
            VerifiedEntities = CopyNames(verifiedEntities);
            Trust = trust;
            Importance = Math.Max(0, Math.Min(100, importance));
            Novelty = Math.Max(0.0, Math.Min(1.0, novelty));
            CooldownCategory = cooldownCategory ?? Type;
            VerifiedContext = verifiedContext ?? string.Empty;
            BaseChance = Math.Max(0.0, Math.Min(1.0, baseChance));
        }

        private static List<string> CopyNames(IEnumerable<string> source)
        {
            List<string> copy = new List<string>();
            if (source == null) return copy;
            foreach (string value in source)
                if (!string.IsNullOrWhiteSpace(value) && !copy.Contains(value)) copy.Add(value);
            return copy;
        }
    }

    internal sealed class EventConversationDecision
    {
        internal DateTime Utc;
        internal string Type;
        internal bool Accepted;
        internal string Reason;
        internal string Speaker;
        internal int Importance;
        internal bool LlmRequested;
    }

    // Verified game events enter here, but all actual autonomous admission is delegated to the one
    // SocialBudget owned by DeepSimsPlugin. This director only performs event-specific validation,
    // priority arbitration, and speaker eligibility.
    internal sealed class EventConversationDirector
    {
        private const int MaxRecentDecisions = 16;
        private const double CandidateLifetimeSeconds = 120.0;
        internal const double PartyMembershipLifetimeSeconds = 30.0;
        private const double DuplicateWindowSeconds = 300.0;

        private readonly DeepSimsPlugin _plugin;
        private readonly Random _random = new Random();
        private readonly Dictionary<string, DateTime> _fingerprints =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly List<EventConversationDecision> _recent = new List<EventConversationDecision>();
        private SocialEventCandidate _pending;
        private bool _pendingProbabilityAccepted;

        internal EventConversationDirector(DeepSimsPlugin plugin) { _plugin = plugin; }

        internal void NotePlayerConversation()
        {
            if (_plugin != null) _plugin.NoteSocialPlayerConversation();
        }

        // A verified event already owns the next social moment.  Ambient seed evaluation checks this
        // so the two paths cannot both spend the same opportunity; there remains one autonomous
        // winner per moment, arbitrated here and admitted by the single SocialBudget.
        internal bool HasPendingCandidate { get { return _pending != null; } }

        internal static bool ShouldPromoteCompletedEncounter(EncounterSnapshot encounter)
        {
            return encounter != null && (encounter.TotalKills > 0 || encounter.Deaths > 0 || encounter.CloseCalls > 0);
        }

        internal void Submit(SocialEventCandidate candidate)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.VerifiedContext)) return;
            DateTime now = DateTime.UtcNow;
            string fingerprint = Fingerprint(candidate);
            DateTime prior;
            if (_fingerprints.TryGetValue(fingerprint, out prior) &&
                (now - prior).TotalSeconds < DuplicateWindowSeconds)
            {
                Record(candidate, false, "recent event suppression", string.Empty, now, false);
                return;
            }
            _fingerprints[fingerprint] = now;
            PruneFingerprints(now);

            if (_pending == null)
            {
                _pending = candidate;
                _pendingProbabilityAccepted = false;
                return;
            }

            SocialPriority existingPriority = SocialPolicy.PriorityOf(_pending.Type, _pending.Importance);
            SocialPriority incomingPriority = SocialPolicy.PriorityOf(candidate.Type, candidate.Importance);
            bool incomingWins = incomingPriority > existingPriority ||
                (incomingPriority == existingPriority && candidate.Importance > _pending.Importance);

            if (incomingWins)
            {
                Record(_pending, false, "lost to higher-priority event", string.Empty, now, false);
                _pending = candidate;
                _pendingProbabilityAccepted = false;
            }
            else
            {
                Record(candidate, false, "lost to higher-priority event", string.Empty, now, false);
            }
        }

        internal void RejectObservedType(string type, string reason)
        {
            EventConversationDecision decision = new EventConversationDecision
            {
                Utc = DateTime.UtcNow,
                Type = string.IsNullOrWhiteSpace(type) ? "unknown" : type,
                Accepted = false,
                Reason = reason ?? "unsupported candidate",
                Speaker = string.Empty,
                Importance = 0,
                LlmRequested = false
            };
            _recent.Add(decision);
            while (_recent.Count > MaxRecentDecisions) _recent.RemoveAt(0);
            if (_plugin != null) _plugin.LogEventConversationDecision(
                decision.Type, false, decision.Reason, string.Empty);
        }

        internal void Tick(WorldSnapshot world, IList<SimSnapshot> active, bool inOrRecentCombat,
            DateTime partySettlingUntilUtc)
        {
            SocialEventCandidate candidate = _pending;
            if (candidate == null) return;
            DateTime now = DateTime.UtcNow;

            string reason;
            if (!Evaluate(candidate, active, partySettlingUntilUtc, now, out reason))
            {
                if (reason == "party settling") return;
                _pending = null;
                Record(candidate, false, reason, string.Empty, now, false);
                return;
            }

            List<string> available = new List<string>();
            for (int i = 0; active != null && i < active.Count; i++)
            {
                SimSnapshot sim = active[i];
                if (sim == null || string.IsNullOrWhiteSpace(sim.Name)) continue;
                if (!IsEligibleSpeaker(candidate, sim.Name)) continue;
                if (_plugin != null && _plugin.IsSocialSpeakerCoolingDown(sim.Name)) continue;
                available.Add(sim.Name);
            }
            if (available.Count == 0)
            {
                _pending = null;
                Record(candidate, false, "blocked by per-Sim cooldown/no present speaker",
                    string.Empty, now, false);
                return;
            }

            double chance = _plugin == null ? candidate.BaseChance :
                _plugin.GetEventReactionChance(candidate.Type, candidate.BaseChance);
            chance *= 0.55 + (candidate.Novelty * 0.45);
            chance *= 0.70 + ((candidate.Importance / 100.0) * 0.30);
            if (_plugin != null) chance *= _plugin.GetSocialOpportunityMultiplier();
            chance = Math.Max(0.0, Math.Min(1.0, chance));
            if (!_pendingProbabilityAccepted && _random.NextDouble() > chance)
            {
                _pending = null;
                _pendingProbabilityAccepted = false;
                Record(candidate, false, "probability gate", string.Empty, now, false);
                return;
            }
            _pendingProbabilityAccepted = true;

            SocialEventCandidate ready = new SocialEventCandidate(candidate.Type, candidate.ObservedUtc,
                candidate.InvolvedNames, available, candidate.VerifiedEntities, candidate.Trust,
                candidate.Importance, candidate.Novelty, candidate.CooldownCategory,
                candidate.VerifiedContext, candidate.BaseChance);

            SocialPriority priority = SocialPolicy.PriorityOf(candidate.Type, candidate.Importance);
            if (_plugin != null && !_plugin.TryAdmitAutonomousOpportunity(candidate.Type, priority,
                Fingerprint(candidate), inOrRecentCombat, out reason))
            {
                // A significant verified event supplies a bounded social-energy window. Temporary
                // player/combat/global ownership keeps the same candidate alive; it does not create
                // another scheduler, reroll its facts, or force a line.
                if (!ShouldRetryAfterTemporaryBlock(reason)) { _pending = null; _pendingProbabilityAccepted = false; }
                Record(candidate, false, reason, string.Empty, now, false);
                return;
            }

            string speaker = string.Empty;
            bool queued = _plugin != null && _plugin.QueueVerifiedEventConversation(ready, out speaker);
            _pending = null;
            _pendingProbabilityAccepted = false;
            if (!queued)
            {
                Record(candidate, false, "accepted but expression router produced no safe line",
                    string.Empty, now, false);
                return;
            }

            bool llm = _plugin.WillUseLlmForAutonomousEvent(candidate.Type);
            Record(candidate, true, "accepted", speaker, now, llm);
        }

        private bool Evaluate(SocialEventCandidate candidate, IList<SimSnapshot> active,
            DateTime partySettlingUntilUtc, DateTime now, out string reason)
        {
            reason = string.Empty;
            if (_plugin != null &&
                (!_plugin.DirectorEnabledConfig.Value || !_plugin.EventChatterConfig.Value))
            {
                reason = "event chatter disabled";
                return false;
            }
            if (active == null || active.Count == 0)
            {
                reason = "no active Deep Sims";
                return false;
            }
            if (candidate.Importance < 40)
            {
                reason = "below importance floor";
                return false;
            }
            if (!IsCandidateFresh(candidate, now))
            {
                reason = "expired";
                return false;
            }
            if (now < partySettlingUntilUtc)
            {
                reason = "party settling";
                return false;
            }
            return true;
        }

        internal string DescribeRecent()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (_plugin != null) sb.AppendLine("[DeepSims Social] " + _plugin.DescribeSocialBudget());
            if (_recent.Count == 0)
            {
                sb.Append("[DeepSims Events] No event-conversation candidates evaluated yet.");
                return sb.ToString();
            }

            sb.AppendLine("[DeepSims Events] Recent candidate decisions (newest first):");
            for (int i = _recent.Count - 1; i >= 0; i--)
            {
                EventConversationDecision d = _recent[i];
                sb.Append("- ").Append(d.Utc.ToLocalTime().ToString("HH:mm:ss")).Append(" ")
                    .Append(d.Type).Append(": ")
                    .Append(d.Accepted ? "accepted" : "suppressed")
                    .Append(" (").Append(d.Reason).Append(")")
                    .Append(" importance=").Append(d.Importance)
                    .Append(" llm=").Append(d.LlmRequested ? "yes" : "no");
                if (!string.IsNullOrWhiteSpace(d.Speaker))
                    sb.Append(" speaker=").Append(d.Speaker);
                if (i > 0) sb.AppendLine();
            }
            return sb.ToString();
        }

        internal static List<string> RunDeterministicSelfTests()
        {
            List<string> result = new List<string>();
            SocialEventCandidate a = new SocialEventCandidate("encounter_complete",
                new DateTime(2026, 1, 1), new[] { "A" }, new[] { "A", "B" },
                new[] { "goblin" }, SocialEventTrust.Experienced, 70, 1.0,
                "encounter", "Completed fight: 4 goblin kills.", 0.8);
            result.Add("candidate acceptance data: " +
                (a.Importance == 70 && a.Trust == SocialEventTrust.Experienced ? "PASS" : "FAIL"));
            result.Add("speaker eligibility accepts captured participant: " +
                (IsEligibleSpeaker(a, "B") ? "PASS" : "FAIL"));
            result.Add("stale participant excluded: " +
                (!IsEligibleSpeaker(a, "C") ? "PASS" : "FAIL"));
            result.Add("event thread hard cap: " +
                (ClampEventThreadLines(99) == 3 && ClampEventThreadLines(0) == 1 ? "PASS" : "FAIL"));

            WorldSnapshot world = new WorldSnapshot
            {
                Scene = "Brakke",
                Party = new List<SimSnapshot>
                {
                    new SimSnapshot { Name = "A", ClassName = "Paladin" },
                    new SimSnapshot { Name = "B", ClassName = "Druid" }
                }
            };
            List<ChatMessage> continuation = PromptBuilder.BuildVerifiedEventThread(
                world.Party[1], world, a,
                new List<ConversationLine>
                {
                    new ConversationLine("A", "we totally found a dragon")
                }, 2);
            string prompt = string.Empty;
            for (int i = 0; i < continuation.Count; i++) prompt += "\n" + continuation[i].content;
            result.Add("second reply retains original verified event: " +
                (prompt.Contains(a.VerifiedContext) && prompt.Contains("HEARD PARTY LINE") ? "PASS" : "FAIL"));
            result.Add("malformed/silent output has explicit stop contract: " +
                (prompt.Contains("NO_MESSAGE") ? "PASS" : "FAIL"));
            result.Add("empty encounter does not create chatter: " +
                (!ShouldPromoteCompletedEncounter(new EncounterSnapshot()) ? "PASS" : "FAIL"));
            result.Add("recorded kill makes encounter socially eligible: " +
                (ShouldPromoteCompletedEncounter(new EncounterSnapshot { TotalKills = 1 }) ? "PASS" : "FAIL"));
            result.Add("event social-energy window is bounded to 120 seconds: " +
                (IsCandidateFresh(new DateTime(2026, 1, 1, 0, 1, 59), new DateTime(2026, 1, 1)) &&
                 !IsCandidateFresh(new DateTime(2026, 1, 1, 0, 2, 1), new DateTime(2026, 1, 1)) ? "PASS" : "FAIL"));

            List<string> social = SocialTemplates.RunSelfTests();
            for (int i = 0; i < social.Count; i++) result.Add(social[i]);
            return result;
        }

        internal static bool IsEligibleSpeaker(SocialEventCandidate candidate, string speakerName)
        {
            return candidate != null && !string.IsNullOrWhiteSpace(speakerName) &&
                Contains(candidate.EligibleSpeakerNames, speakerName);
        }

        internal static bool IsWithinCooldown(DateTime now, DateTime last, double seconds)
        {
            return last != DateTime.MinValue &&
                (now - last).TotalSeconds < Math.Max(0.0, seconds);
        }

        internal static bool IsCandidateFresh(DateTime now, DateTime observedUtc)
        {
            return (now - observedUtc).TotalSeconds <= CandidateLifetimeSeconds;
        }

        internal static bool IsCandidateFresh(SocialEventCandidate candidate, DateTime now)
        {
            if (candidate == null) return false;
            double lifetime = IsPartyMembershipEvent(candidate.Type)
                ? PartyMembershipLifetimeSeconds : CandidateLifetimeSeconds;
            return (now - candidate.ObservedUtc).TotalSeconds <= lifetime;
        }

        internal static bool IsPartyMembershipEvent(string type)
        {
            return string.Equals(type, "party_join", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "party_leave", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldRetryAfterTemporaryBlock(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return false;
            return reason.IndexOf("combat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("player recently spoke", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("global cooldown", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("current conversation thread", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("social moment", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static int ClampEventThreadLines(int requested)
        {
            return Math.Max(1, Math.Min(3, requested));
        }

        private void Record(SocialEventCandidate candidate, bool accepted, string reason,
            string speaker, DateTime now, bool llmRequested)
        {
            _recent.Add(new EventConversationDecision
            {
                Utc = now,
                Type = candidate.Type,
                Accepted = accepted,
                Reason = reason,
                Speaker = speaker,
                Importance = candidate.Importance,
                LlmRequested = llmRequested
            });
            while (_recent.Count > MaxRecentDecisions) _recent.RemoveAt(0);
            if (_plugin != null)
                _plugin.LogEventConversationDecision(candidate.Type, accepted, reason, speaker);
        }

        private void PruneFingerprints(DateTime now)
        {
            if (_fingerprints.Count <= 24) return;
            List<string> remove = new List<string>();
            foreach (KeyValuePair<string, DateTime> pair in _fingerprints)
                if ((now - pair.Value).TotalSeconds > DuplicateWindowSeconds)
                    remove.Add(pair.Key);
            for (int i = 0; i < remove.Count; i++) _fingerprints.Remove(remove[i]);
        }

        internal static string Fingerprint(SocialEventCandidate candidate)
        {
            return (candidate.Type + "|" + candidate.CooldownCategory + "|" +
                candidate.VerifiedContext).Trim().ToLowerInvariant();
        }

        internal static bool Contains(IList<string> values, string value)
        {
            if (values == null || values.Count == 0 || string.IsNullOrWhiteSpace(value))
                return false;
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
