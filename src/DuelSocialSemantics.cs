using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ErenshorDeepSims
{
    // Fact-only representation of the optional Practice Duels contract.  This type is deliberately
    // independent of the Duel assembly so Deep Sims remains standalone and the deterministic test
    // harness can exercise all social/grounding decisions without Erenshor, Ollama, or Harmony.
    internal sealed class VerifiedDuelEvent
    {
        internal string Type;
        internal string Opponent;
        internal string Scope;
        internal string Decision;
        internal string Outcome;
        internal string Winner;
        internal string Yielded;
        internal string ReasonToken;
        internal string Reason;

        internal bool IsCompleted { get { return Type == "duel_completed"; } }
        internal bool IsHostileInterruption
        {
            get { return Type == "duel_cancelled" && ReasonToken == "hostile_interruption"; }
        }

        internal string SerializeTransport()
        {
            List<string> fields = new List<string>();
            Add(fields, "type", Type);
            Add(fields, "opponent", Opponent);
            Add(fields, "scope", Scope);
            Add(fields, "decision", Decision);
            Add(fields, "outcome", Outcome);
            Add(fields, "winner", Winner);
            Add(fields, "yielded", Yielded);
            Add(fields, "reason_token", ReasonToken);
            Add(fields, "reason", Reason);
            return string.Join("; ", fields.ToArray());
        }

        internal string VerifiedContext()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("DUEL_EVENT:");
            sb.AppendLine("type=" + Type);
            if (!string.IsNullOrWhiteSpace(Opponent)) sb.AppendLine("opponent=" + Opponent);
            if (!string.IsNullOrWhiteSpace(Scope)) sb.AppendLine("scope=" + Scope);
            if (!string.IsNullOrWhiteSpace(Decision)) sb.AppendLine("decision=" + Decision);
            if (!string.IsNullOrWhiteSpace(Outcome)) sb.AppendLine("outcome=" + Outcome);
            if (!string.IsNullOrWhiteSpace(Winner)) sb.AppendLine("winner=" + Winner);
            if (!string.IsNullOrWhiteSpace(Yielded)) sb.AppendLine("yielded=" + Yielded);
            if (!string.IsNullOrWhiteSpace(ReasonToken)) sb.AppendLine("reason_token=" + ReasonToken);
            // Reason is verified Duel text, but the stable token remains authoritative for social
            // policy.  Keep the text available only as bounded supporting context.
            if (!string.IsNullOrWhiteSpace(Reason)) sb.AppendLine("reason=" + Reason);
            sb.Append("EVENT LIMITS: this was a friendly practice duel. It does not establish a death, kill, loot, XP, reward, wager, lasting injury, or faction hostility");
            return sb.ToString();
        }

        internal string MemorySummary()
        {
            if (!IsCompleted) return string.Empty;
            string opponent = string.IsNullOrWhiteSpace(Opponent) ? "a nearby Sim" : Opponent;
            if (Outcome == "timeout")
                return "The player and " + opponent + " completed a friendly practice duel; it ended by timeout with no verified winner.";
            if (Outcome == "yield" && Yielded == "opponent")
                return "The player and " + opponent + " had a friendly practice duel; " + opponent + " yielded.";
            if (Outcome == "yield" && Yielded == "player")
                return "The player and " + opponent + " had a friendly practice duel; the player yielded and " + opponent + " won.";
            return "The player and " + opponent + " completed a friendly practice duel.";
        }

        internal string Fingerprint()
        {
            return Normalize(Type) + "|" + Normalize(Opponent) + "|" + Normalize(Scope) + "|" +
                Normalize(Decision) + "|" + Normalize(Outcome) + "|" + Normalize(Winner) + "|" +
                Normalize(Yielded) + "|" + Normalize(ReasonToken);
        }

        internal static bool TryCreate(string eventType, string opponent, string scope, string decision,
            string outcome, string winner, string yielded, string reasonToken, string reason,
            out VerifiedDuelEvent value)
        {
            value = new VerifiedDuelEvent
            {
                Type = CanonicalType(eventType),
                Opponent = CleanText(opponent, false),
                Scope = NormalizeToken(scope),
                Decision = NormalizeToken(decision),
                Outcome = NormalizeToken(outcome),
                Winner = CleanText(winner, false),
                Yielded = NormalizeToken(yielded),
                ReasonToken = NormalizeToken(reasonToken),
                Reason = CleanText(reason, false)
            };
            return Validate(value);
        }

        internal static bool TryParseTransport(string eventType, string description, out VerifiedDuelEvent value)
        {
            Dictionary<string, string> fields = ParseFields(description);
            string embeddedType;
            fields.TryGetValue("type", out embeddedType);
            string canonical = CanonicalType(string.IsNullOrWhiteSpace(eventType) ? embeddedType : eventType);

            // Older Deep Sims saw a generic friendly_duel completion.  Keep that compatibility
            // fact-safe: treat it only as a completed practice duel and never infer a winner or
            // reason from prose that is not a structured key/value field.
            if (canonical == "friendly_duel") canonical = "duel_completed";

            string opponent;
            string scope;
            string decision;
            string outcome;
            string winner;
            string yielded;
            string reasonToken;
            string reason;
            fields.TryGetValue("opponent", out opponent);
            fields.TryGetValue("scope", out scope);
            fields.TryGetValue("decision", out decision);
            fields.TryGetValue("outcome", out outcome);
            fields.TryGetValue("winner", out winner);
            fields.TryGetValue("yielded", out yielded);
            fields.TryGetValue("reason_token", out reasonToken);
            fields.TryGetValue("reason", out reason);

            if (canonical == "duel_completed" && string.IsNullOrWhiteSpace(outcome) &&
                string.Equals(eventType, "friendly_duel", StringComparison.OrdinalIgnoreCase))
                outcome = "completed";

            return TryCreate(canonical, opponent, scope, decision, outcome, winner, yielded,
                reasonToken, reason, out value);
        }

        internal static bool TryParseVerifiedContext(string context, out VerifiedDuelEvent value)
        {
            return TryParseTransport(string.Empty, context, out value);
        }

        private static bool Validate(VerifiedDuelEvent value)
        {
            if (value == null || !DuelSocialPolicy.IsCanonicalDuelType(value.Type)) return false;
            if (value.Scope != string.Empty && value.Scope != "party" && value.Scope != "nearby") return false;

            if (value.Type == "duel_accepted")
            {
                if (value.Decision == string.Empty) value.Decision = "accept";
                if (value.Decision != "accept") return false;
            }
            else if (value.Type == "duel_declined")
            {
                if (value.Decision == string.Empty) value.Decision = "decline";
                if (!DuelSocialPolicy.IsDeclineDecision(value.Decision)) return false;
            }
            else if (value.Type == "duel_completed")
            {
                if (value.Outcome == string.Empty) value.Outcome = "completed";
                if (value.Outcome != "yield" && value.Outcome != "timeout" && value.Outcome != "completed") return false;
                if (value.Yielded != string.Empty && value.Yielded != "player" && value.Yielded != "opponent") return false;
            }
            return true;
        }

        private static Dictionary<string, string> ParseFields(string description)
        {
            Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(description)) return fields;
            string normalized = description.Replace('\r', '\n').Replace(';', '\n');
            string[] parts = normalized.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string item = parts[i].Trim();
                if (item.Length == 0 || item.Equals("DUEL_EVENT:", StringComparison.OrdinalIgnoreCase)) continue;
                int equals = item.IndexOf('=');
                if (equals <= 0 || equals >= item.Length - 1) continue;
                string key = NormalizeToken(item.Substring(0, equals));
                string val = CleanText(item.Substring(equals + 1), false);
                if (key.Length > 0 && !fields.ContainsKey(key)) fields[key] = val;
            }
            return fields;
        }

        private static void Add(List<string> fields, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) fields.Add(key + "=" + CleanText(value, true));
        }

        internal static string CanonicalType(string value)
        {
            string t = NormalizeToken(value);
            if (t == "friendly_duel") return "friendly_duel";
            return t;
        }

        internal static string NormalizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string text = value.Trim().ToLowerInvariant();
            StringBuilder sb = new StringBuilder(text.Length);
            bool separator = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                    separator = false;
                }
                else if (!separator)
                {
                    sb.Append('_');
                    separator = true;
                }
            }
            return sb.ToString().Trim('_');
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }

        private static string CleanText(string value, bool transport)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string clean = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (transport) clean = clean.Replace(';', ',').Replace('=', ':');
            return clean.Length <= 180 ? clean : clean.Substring(0, 180);
        }
    }

    internal static class DuelSocialPolicy
    {
        internal static bool IsTransportType(string type)
        {
            string t = VerifiedDuelEvent.CanonicalType(type);
            return IsCanonicalDuelType(t) || t == "friendly_duel";
        }

        internal static bool IsCanonicalDuelType(string type)
        {
            string t = VerifiedDuelEvent.NormalizeToken(type);
            return t == "duel_challenge" || t == "duel_accepted" || t == "duel_declined" ||
                t == "duel_started" || t == "duel_completed" || t == "duel_cancelled";
        }

        internal static bool IsDeclineDecision(string decision)
        {
            string d = VerifiedDuelEvent.NormalizeToken(decision);
            return d == "decline" || d == "decline_low_health" || d == "decline_recent_duel" ||
                d == "decline_level_mismatch";
        }

        internal static int Importance(VerifiedDuelEvent value)
        {
            if (value == null) return 0;
            switch (value.Type)
            {
                case "duel_completed": return 80;
                case "duel_accepted": return 45;
                case "duel_declined": return 45;
                case "duel_cancelled": return value.IsHostileInterruption ? 55 : 25;
                case "duel_started": return 20;
                default: return 15;
            }
        }

        internal static SocialPriority Priority(VerifiedDuelEvent value)
        {
            if (value == null) return SocialPriority.Low;
            if (value.Type == "duel_completed") return SocialPriority.High;
            if (value.Type == "duel_accepted" || value.Type == "duel_declined" || value.IsHostileInterruption)
                return SocialPriority.Medium;
            return SocialPriority.Low;
        }

        internal static double ReactionChance(VerifiedDuelEvent value, bool opponentIsCurrentDeepSim)
        {
            if (value == null) return 0.0;
            switch (value.Type)
            {
                case "duel_accepted": return opponentIsCurrentDeepSim ? 0.18 : 0.0;
                case "duel_declined": return opponentIsCurrentDeepSim ? 0.30 : 0.0;
                case "duel_completed": return 0.70;
                case "duel_cancelled": return value.IsHostileInterruption ? 0.25 : 0.0;
                default: return 0.0;
            }
        }

        internal static bool ShouldPersistMemory(VerifiedDuelEvent value)
        {
            return value != null && value.Type == "duel_completed";
        }

        internal static bool OpponentIsCurrentDeepSim(VerifiedDuelEvent value, IList<SimSnapshot> active)
        {
            // Deep Sims currently owns only current party Sims.  Never promote a same-name nearby
            // non-party Sim into a persistent Deep Sim identity merely because Duel can challenge it.
            if (value == null || value.Scope != "party" || string.IsNullOrWhiteSpace(value.Opponent) || active == null)
                return false;
            for (int i = 0; i < active.Count; i++)
                if (active[i] != null && string.Equals(active[i].Name, value.Opponent, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        internal static List<string> EligibleSpeakers(VerifiedDuelEvent value, IList<SimSnapshot> active)
        {
            List<string> result = new List<string>();
            if (value == null || active == null) return result;
            bool opponentDeep = OpponentIsCurrentDeepSim(value, active);

            if (value.Type == "duel_accepted" || value.Type == "duel_declined")
            {
                if (opponentDeep) result.Add(value.Opponent);
                return result;
            }

            if (value.IsHostileInterruption)
            {
                // One possible voice at most. Prefer the challenged party Deep Sim when it exists;
                // otherwise use the first current Deep Sim and let the normal per-speaker cooldown
                // decide whether even that line is available.
                if (opponentDeep) result.Add(value.Opponent);
                else
                {
                    for (int i = 0; i < active.Count; i++)
                    {
                        SimSnapshot sim = active[i];
                        if (sim == null || string.IsNullOrWhiteSpace(sim.Name)) continue;
                        result.Add(sim.Name);
                        break;
                    }
                }
                return result;
            }

            if (value.Type != "duel_completed") return result;
            for (int i = 0; i < active.Count; i++)
            {
                SimSnapshot sim = active[i];
                if (sim == null || string.IsNullOrWhiteSpace(sim.Name)) continue;
                if (!Contains(result, sim.Name)) result.Add(sim.Name);
            }
            return result;
        }

        internal static bool PlayerWon(VerifiedDuelEvent value)
        {
            return value != null && value.Type == "duel_completed" && value.Outcome == "yield" &&
                (value.Yielded == "opponent" || string.Equals(value.Winner, "player", StringComparison.OrdinalIgnoreCase));
        }

        internal static bool OpponentWon(VerifiedDuelEvent value)
        {
            return value != null && value.Type == "duel_completed" && value.Outcome == "yield" &&
                value.Yielded == "player" && !string.IsNullOrWhiteSpace(value.Winner) &&
                !string.Equals(value.Winner, "player", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Contains(IList<string> values, string value)
        {
            for (int i = 0; values != null && i < values.Count; i++)
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    internal static class DuelTemplateRenderer
    {
        internal static bool TryRender(VerifiedDuelEvent value, SimSnapshot speaker, RelationshipTone tone, out string message)
        {
            message = string.Empty;
            if (value == null || speaker == null || string.IsNullOrWhiteSpace(speaker.Name)) return false;
            int seed = StableHash(value.Fingerprint() + "|" + speaker.Name);
            bool speakerIsOpponent = !string.IsNullOrWhiteSpace(value.Opponent) &&
                string.Equals(speaker.Name, value.Opponent, StringComparison.OrdinalIgnoreCase);
            bool rivalish = speaker.Rival || (tone != null && tone.Rivalry >= 0.35f);

            if (value.Type == "duel_accepted")
            {
                if (!speakerIsOpponent) return false;
                message = Pick(seed, rivalish
                    ? new string[] { "yeah, I'm in", "alright, let's spar", "sure, let's go" }
                    : new string[] { "sure, let's spar", "yeah, I'm in", "alright, let's go" });
                return true;
            }

            if (value.Type == "duel_declined")
            {
                if (!speakerIsOpponent) return false;
                if (value.Decision == "decline_low_health")
                    message = Pick(seed, new string[] { "need to recover first", "not at this health", "give me a bit to recover" });
                else if (value.Decision == "decline_recent_duel")
                    message = Pick(seed, new string[] { "we just sparred, give it a bit", "not again that fast", "give me a little time after the last spar" });
                else if (value.Decision == "decline_level_mismatch")
                    message = Pick(seed, new string[] { "I'll pass on that matchup", "not feeling that matchup", "I'll sit this one out" });
                else message = Pick(seed, new string[] { "I'll pass this one", "not this time", "nah, I'll sit this one out" });
                return true;
            }

            if (value.Type == "duel_completed")
            {
                if (value.Outcome == "timeout")
                {
                    message = Pick(seed, new string[] { "gg, no winner there", "gg, call that even", "nice spar" });
                    return true;
                }

                if (speakerIsOpponent && DuelSocialPolicy.PlayerWon(value))
                {
                    message = Pick(seed, rivalish
                        ? new string[] { "gg, you got me", "good one, I yield", "yeah yeah, gg" }
                        : new string[] { "gg, you got me", "good spar", "good one, I yield" });
                    return true;
                }
                if (speakerIsOpponent && DuelSocialPolicy.OpponentWon(value))
                {
                    message = Pick(seed, rivalish
                        ? new string[] { "gg", "good spar", "nice one" }
                        : new string[] { "gg", "good spar", "nice one" });
                    return true;
                }

                if (DuelSocialPolicy.OpponentWon(value) && !string.IsNullOrWhiteSpace(value.Winner))
                {
                    message = Pick(seed, new string[] { "gg, " + value.Winner, "nice spar", "gg" });
                    return true;
                }
                message = Pick(seed, new string[] { "gg", "nice spar", "good one" });
                return true;
            }

            if (value.IsHostileInterruption)
            {
                message = Pick(seed, new string[] { "real fight first", "spar can wait", "deal with the real fight first" });
                return true;
            }

            return false;
        }

        private static string Pick(int seed, string[] values)
        {
            if (values == null || values.Length == 0) return string.Empty;
            int index = seed == int.MinValue ? 0 : Math.Abs(seed) % values.Length;
            return values[index];
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string text = value ?? string.Empty;
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= char.ToLowerInvariant(text[i]);
                    hash *= 16777619u;
                }
                return (int)hash;
            }
        }
    }

    internal static class DuelGroundingPolicy
    {
        private static readonly Regex LethalOrReward = new Regex(
            @"\b(?:killed?|murdered?|dead|died|dying|nearly\s+died|almost\s+died|barely\s+survived|looted?|loot|xp|experience\s+points?|reward|wager|bet|gold\s+wager|faction\s+hostility|permanent\s+injur(?:y|ies))\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex UnsupportedStyle = new Regex(
            @"\b(?:paladin|reaver|druid|arcanist|stormcaller|windblade|fireball|spellwork|spell|heal|healing|sword|shield|pet|bleed|stun|root|rotation|combo|footwork|combat\s+style)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static bool IsGrounded(string reply, VerifiedDuelEvent value, SimMemory memory, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(reply) || value == null) return true;
            string text = reply.Trim();
            string lower = text.ToLowerInvariant();

            if (LethalOrReward.IsMatch(text))
            {
                reason = "friendly duel does not verify death, killing, loot, XP, rewards, wagers, faction hostility, or permanent injury";
                return false;
            }
            if (UnsupportedStyle.IsMatch(text))
            {
                reason = "duel event did not verify class-specific or combat-style details";
                return false;
            }
            if (Regex.IsMatch(lower, @"\b(?:always|every\s+time)\b"))
            {
                reason = "absolute duel-history claim is not supported by one event";
                return false;
            }
            if (Regex.IsMatch(lower, @"\b(?:again|last\s+time|before)\b") && value.Decision != "decline_recent_duel")
            {
                reason = "duel-history comparison was not verified by this event";
                return false;
            }

            if (value.Type == "duel_declined" && ImpliesAcceptance(lower))
            {
                reason = "contradicts authoritative duel decline";
                return false;
            }
            if (value.Type == "duel_accepted" && ImpliesRefusal(lower))
            {
                reason = "contradicts authoritative duel acceptance";
                return false;
            }

            if (value.Type == "duel_declined" && HasUnsupportedDeclineReason(lower, value.Decision))
            {
                reason = "invented duel-decline reason";
                return false;
            }

            bool speakerIsOpponent = memory != null && !string.IsNullOrWhiteSpace(memory.Name) &&
                !string.IsNullOrWhiteSpace(value.Opponent) &&
                string.Equals(memory.Name, value.Opponent, StringComparison.OrdinalIgnoreCase);
            if (value.Type == "duel_completed")
            {
                if (value.Outcome == "timeout" && Regex.IsMatch(lower,
                    @"\b(?:i|you|we)\s+(?:won|lost|yielded)|\bbeat\s+(?:me|you|them)\b"))
                {
                    reason = "timeout has no verified winner or yield";
                    return false;
                }
                if (speakerIsOpponent && DuelSocialPolicy.PlayerWon(value) && Regex.IsMatch(lower,
                    @"\b(?:i\s+won|i\s+beat\s+you|you\s+lost|you\s+yielded)\b"))
                {
                    reason = "contradicts verified player victory";
                    return false;
                }
                if (speakerIsOpponent && DuelSocialPolicy.OpponentWon(value) && Regex.IsMatch(lower,
                    @"\b(?:i\s+lost|i\s+yielded|you\s+won|you\s+beat\s+me|you\s+got\s+me)\b"))
                {
                    reason = "contradicts verified Sim victory";
                    return false;
                }
            }

            if (value.Type == "duel_cancelled" && value.ReasonToken != "hostile_interruption" &&
                Regex.IsMatch(lower, @"\b(?:real\s+fight|real\s+combat|hostile|enemy|mob)\b"))
            {
                reason = "ordinary duel cancellation does not verify hostile interruption";
                return false;
            }

            return true;
        }

        private static bool ImpliesAcceptance(string lower)
        {
            return Regex.IsMatch(lower,
                @"\b(?:i'm\s+in|im\s+in|let'?s\s+(?:fight|duel|spar|go)|bring\s+it|sure[,! ]+(?:let'?s|i'?ll)|yeah[,! ]+(?:let'?s|i'?ll)|alright[,! ]+(?:let'?s|i'?ll))\b");
        }

        private static bool ImpliesRefusal(string lower)
        {
            return Regex.IsMatch(lower,
                @"\b(?:i\s+decline|i'?ll\s+pass|i\s+pass|not\s+this\s+time|not\s+today|no\s+thanks|nah[,! ]+i'?ll\s+pass|can'?t\s+fight|cannot\s+fight|won'?t\s+fight)\b");
        }

        private static bool HasUnsupportedDeclineReason(string lower, string decision)
        {
            bool health = Regex.IsMatch(lower, @"\b(?:health|hp|hurt|injured|recover|healing|beat\s+up)\b");
            bool recent = Regex.IsMatch(lower, @"\b(?:just\s+(?:dueled|duelled|sparred)|recent|again|last\s+spar|last\s+duel)\b");
            bool level = Regex.IsMatch(lower, @"\b(?:level|matchup|too\s+strong|too\s+weak|outmatch)\b");
            bool invented = Regex.IsMatch(lower, @"\b(?:nervous|scared|afraid|busy|mana|endurance|stamina|bored|tired)\b");
            if (invented) return true;
            if (health && decision != "decline_low_health") return true;
            if (recent && decision != "decline_recent_duel") return true;
            if (level && decision != "decline_level_mismatch") return true;
            return false;
        }
    }

    internal sealed class DuelEventDeduplicator
    {
        private readonly Dictionary<string, DateTime> _recent = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly double _windowSeconds;

        internal DuelEventDeduplicator(double windowSeconds = 6.0)
        {
            _windowSeconds = Math.Max(1.0, windowSeconds);
        }

        internal void Clear()
        {
            _recent.Clear();
        }

        internal bool TryAccept(VerifiedDuelEvent value, DateTime now)
        {
            if (value == null) return false;
            string key = value.Fingerprint();
            DateTime prior;
            if (_recent.TryGetValue(key, out prior) && (now - prior).TotalSeconds < _windowSeconds) return false;
            _recent[key] = now;
            if (_recent.Count > 24)
            {
                List<string> remove = new List<string>();
                foreach (KeyValuePair<string, DateTime> pair in _recent)
                    if ((now - pair.Value).TotalSeconds >= _windowSeconds) remove.Add(pair.Key);
                for (int i = 0; i < remove.Count; i++) _recent.Remove(remove[i]);
            }
            return true;
        }
    }

    internal static class DuelSocialSemantics
    {
        internal static List<string> RunSelfTests()
        {
            List<string> results = new List<string>();
            SimSnapshot dancer = new SimSnapshot { Key = "dancer", Name = "Dancer", Rival = false };
            SimSnapshot phanty = new SimSnapshot { Key = "phanty", Name = "Phanty", Rival = false };
            List<SimSnapshot> party = new List<SimSnapshot> { dancer, phanty };
            SimMemory dancerMemory = new SimMemory { Name = "Dancer", VerifiedPracticeDuels = 0 };
            dancerMemory.Normalize();

            VerifiedDuelEvent declined;
            VerifiedDuelEvent.TryCreate("duel_declined", "Dancer", "party", "decline", "", "", "", "", "", out declined);
            Add(results, "authority/decline cannot imply acceptance",
                !Grounded("sure, let's fight", declined, dancerMemory));

            VerifiedDuelEvent accepted;
            VerifiedDuelEvent.TryCreate("duel_accepted", "Dancer", "party", "accept", "", "", "", "", "", out accepted);
            Add(results, "authority/accept cannot imply decline",
                !Grounded("not this time, I'll pass", accepted, dancerMemory));

            VerifiedDuelEvent nearbyAccepted;
            VerifiedDuelEvent.TryCreate("duel_accepted", "Dancer", "nearby", "accept", "", "", "", "", "", out nearbyAccepted);
            Add(results, "nonparty/no persistent Deep Sim promotion",
                !DuelSocialPolicy.OpponentIsCurrentDeepSim(nearbyAccepted, party) &&
                DuelSocialPolicy.EligibleSpeakers(nearbyAccepted, party).Count == 0);

            VerifiedDuelEvent nearbyCompleted;
            VerifiedDuelEvent.TryCreate("duel_completed", "Wanderer", "nearby", "", "yield", "player", "opponent", "", "", out nearbyCompleted);
            Add(results, "nonparty/current party observer remains eligible",
                DuelSocialPolicy.EligibleSpeakers(nearbyCompleted, party).Count == 2);

            VerifiedDuelEvent challenge;
            VerifiedDuelEvent.TryCreate("duel_challenge", "Dancer", "party", "", "", "", "", "", "", out challenge);
            VerifiedDuelEvent started;
            VerifiedDuelEvent.TryCreate("duel_started", "Dancer", "party", "", "", "", "", "", "", out started);
            VerifiedDuelEvent cancelled;
            VerifiedDuelEvent.TryCreate("duel_cancelled", "Dancer", "party", "", "", "", "", "manual_stop", "Practice duel stopped.", out cancelled);
            VerifiedDuelEvent hostile;
            VerifiedDuelEvent.TryCreate("duel_cancelled", "Dancer", "party", "", "", "", "", "hostile_interruption", "Real combat interrupted the duel.", out hostile);
            Add(results, "policy/challenge silent", DuelSocialPolicy.ReactionChance(challenge, true) == 0.0);
            Add(results, "policy/started structural silent", DuelSocialPolicy.ReactionChance(started, true) == 0.0);
            Add(results, "policy/completed higher social value", DuelSocialPolicy.ReactionChance(nearbyCompleted, false) > 0.5);
            Add(results, "policy/ordinary cancellation silent", DuelSocialPolicy.ReactionChance(cancelled, true) == 0.0);
            Add(results, "policy/hostile interruption bounded candidate", DuelSocialPolicy.ReactionChance(hostile, true) > 0.0 && DuelSocialPolicy.ReactionChance(hostile, true) < 0.5);

            DuelEventDeduplicator unloadDedup = new DuelEventDeduplicator(6.0);
            DateTime unloadNow = new DateTime(2026, 8, 13, 6, 0, 0, DateTimeKind.Utc);
            bool firstUnloadEvent = unloadDedup.TryAccept(accepted, unloadNow);
            bool duplicateUnloadEvent = unloadDedup.TryAccept(accepted, unloadNow.AddSeconds(1));
            unloadDedup.Clear();
            bool afterUnloadReset = unloadDedup.TryAccept(accepted, unloadNow.AddSeconds(2));
            Add(results, "lifecycle/dedup reset allows a fresh plugin instance",
                firstUnloadEvent && !duplicateUnloadEvent && afterUnloadReset);

            string line;
            Add(results, "template/accept", DuelTemplateRenderer.TryRender(accepted, dancer, null, out line) && Grounded(line, accepted, dancerMemory));
            Add(results, "template/decline generic", DuelTemplateRenderer.TryRender(declined, dancer, null, out line) && Grounded(line, declined, dancerMemory));

            VerifiedDuelEvent low;
            VerifiedDuelEvent.TryCreate("duel_declined", "Dancer", "party", "decline_low_health", "", "", "", "", "", out low);
            Add(results, "template/decline low health", DuelTemplateRenderer.TryRender(low, dancer, null, out line) && Grounded(line, low, dancerMemory));

            VerifiedDuelEvent recent;
            VerifiedDuelEvent.TryCreate("duel_declined", "Dancer", "party", "decline_recent_duel", "", "", "", "", "", out recent);
            Add(results, "template/decline recent duel", DuelTemplateRenderer.TryRender(recent, dancer, null, out line) && Grounded(line, recent, dancerMemory));

            VerifiedDuelEvent playerWin;
            VerifiedDuelEvent.TryCreate("duel_completed", "Dancer", "party", "", "yield", "player", "opponent", "", "", out playerWin);
            Add(results, "template/loss-yield", DuelTemplateRenderer.TryRender(playerWin, dancer, null, out line) && Grounded(line, playerWin, dancerMemory));
            Add(results, "template/observer win", DuelTemplateRenderer.TryRender(playerWin, phanty, null, out line) && Grounded(line, playerWin, new SimMemory { Name = "Phanty" }));

            VerifiedDuelEvent simWin;
            VerifiedDuelEvent.TryCreate("duel_completed", "Dancer", "party", "", "yield", "Dancer", "player", "", "", out simWin);
            Add(results, "template/victory", DuelTemplateRenderer.TryRender(simWin, dancer, null, out line) && Grounded(line, simWin, dancerMemory));

            VerifiedDuelEvent timeout;
            VerifiedDuelEvent.TryCreate("duel_completed", "Dancer", "party", "", "timeout", "", "", "", "", out timeout);
            Add(results, "template/timeout", DuelTemplateRenderer.TryRender(timeout, dancer, null, out line) && Grounded(line, timeout, dancerMemory));
            Add(results, "template/hostile interruption", DuelTemplateRenderer.TryRender(hostile, phanty, null, out line) && Grounded(line, hostile, new SimMemory { Name = "Phanty" }));

            Add(results, "grounding/friendly loss is not death", !Grounded("I nearly died there", playerWin, dancerMemory));
            Add(results, "grounding/victory gives no loot", !Grounded("nice, we got loot and XP for that", playerWin, dancerMemory));
            Add(results, "grounding/single duel does not support always", !Grounded("you always beat me", playerWin, dancerMemory));
            Add(results, "grounding/no unverified combat style", !Grounded("nice spellwork with that fireball", playerWin, dancerMemory));
            Add(results, "grounding/generic decline cannot invent nerves", !Grounded("I'm too nervous, so I'll pass", declined, dancerMemory));

            DuelEventDeduplicator dedup = new DuelEventDeduplicator(6.0);
            DateTime t = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
            string transport = playerWin.SerializeTransport();
            VerifiedDuelEvent fallbackCopy;
            VerifiedDuelEvent.TryParseTransport("duel_completed", transport, out fallbackCopy);
            Add(results, "dedup/structured plus fallback is one semantic event",
                dedup.TryAccept(playerWin, t) && !dedup.TryAccept(fallbackCopy, t.AddSeconds(1)));
            Add(results, "dedup/repeated completion is one candidate",
                !dedup.TryAccept(playerWin, t.AddSeconds(2)));

            Add(results, "memory/completion qualifies once", DuelSocialPolicy.ShouldPersistMemory(playerWin) && !string.IsNullOrWhiteSpace(playerWin.MemorySummary()));
            Add(results, "memory/challenge is not durable", !DuelSocialPolicy.ShouldPersistMemory(challenge) && string.IsNullOrWhiteSpace(challenge.MemorySummary()));
            Add(results, "memory/cancellation is not durable", !DuelSocialPolicy.ShouldPersistMemory(hostile) && string.IsNullOrWhiteSpace(hostile.MemorySummary()));

            return results;
        }

        private static bool Grounded(string line, VerifiedDuelEvent value, SimMemory memory)
        {
            string reason;
            if (memory != null) memory.Normalize();
            return DuelGroundingPolicy.IsGrounded(line, value, memory, out reason);
        }

        private static void Add(List<string> results, string name, bool pass)
        {
            results.Add("duel/" + name + ": " + (pass ? "PASS" : "FAIL"));
        }
    }
}
