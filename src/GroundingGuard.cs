using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ErenshorDeepSims
{
    internal enum SocialClaimKind { SubjectiveSocial, CheckableFactual, AiOrEngineeringLeak }

    internal static class SocialClaimClassifier
    {
        internal static SocialClaimKind Classify(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.Length == 0) return SocialClaimKind.SubjectiveSocial;
            if (GroundingGuard.HasInstructionLeak(value) || GroundingGuard.HasAssistantStyleLanguage(value) ||
                Regex.IsMatch(value, @"\b(?:as an ai|cannot verify|can't verify|based on (?:the )?(?:available )?evidence|insufficient information|according to (?:the )?(?:provided )?context|grounded|provenance)\b", RegexOptions.IgnoreCase))
                return SocialClaimKind.AiOrEngineeringLeak;
            if (Regex.IsMatch(value,
                @"\b(?:yesterday|earlier|last (?:night|time|week|session)|remember when|ago|today|just now)\b|" +
                @"\b(?:killed|slain|looted|dropped|gave|received|obtained|equipped|bought|sold|completed|finished the quest|leveled|visited|went to|said|told me|played with|joined|left the group|launched)\b|" +
                @"\b(?:xp|experience points?|gold|quest|drop rate|level\s*\d+|has (?:a|an|the)\s+\w+\s+(?:sword|staff|item|weapon|armor|armour)|mana\s+(?:is|was|at)|health\s+(?:is|was|at))\b|" +
                @"\b(?:nasa|spacex|openai|election|market|company)\b[^.!?]{0,50}\b(?:today|current|latest|launched|announced|released)\b",
                RegexOptions.IgnoreCase)) return SocialClaimKind.CheckableFactual;
            if (value.IndexOf('?') >= 0 || Regex.IsMatch(value,
                @"\b(?:like|love|hate|prefer|favorite|favourite|feel|think|guess|reckon|suppose|maybe|probably|possibly|idk|don't know|do not know|no idea|not sure|depends|would|could|should|wanna|want to|wants? to|let's|sounds?\s+(?:fun|rough|stressful|good|bad)|lol|lmao|haha|joking|kinda|sorta|always end up)\b",
                RegexOptions.IgnoreCase)) return SocialClaimKind.SubjectiveSocial;
            // Ambiguous declarative lines retain the full validator. Permissiveness is earned by
            // clear subjective/question/hypothetical framing, never merely by missing a keyword.
            return SocialClaimKind.CheckableFactual;
        }

        internal static bool IsPermissiveSocialLine(string text, out string reason)
        {
            SocialClaimKind kind = Classify(text);
            if (kind == SocialClaimKind.SubjectiveSocial) { reason = string.Empty; return true; }
            reason = kind == SocialClaimKind.AiOrEngineeringLeak
                ? "assistant/engineering language leakage"
                : "checkable factual claim requires provenance grounding";
            return false;
        }
    }

    internal static class GroundingGuard
    {
        internal static bool IsDirectReplyRelevant(string playerMessage, string reply, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(playerMessage) || string.IsNullOrWhiteSpace(reply) || !playerMessage.Contains("?")) return true;

            string trimmed = reply.Trim();
            if (Regex.IsMatch(trimmed,
                @"^(?:but\s+)?what\s+about\s+(?:me|you)(?:\s*,?\s*then)?[!?.]*$|^you\s+(?:go\s+)?first[!?.]*$",
                RegexOptions.IgnoreCase))
            {
                reason = "generic counter-question instead of answering the player's topic";
                return false;
            }
            if (!reply.Contains("?")) return true;
            if (Regex.Matches(trimmed, @"\b[\w']+\b").Count <= 2) return true;
            if (Regex.IsMatch(trimmed,
                @"\b(?:that|this|it|would|could|sounds?|yeah|nah|honestly|maybe|probably|imagine|mean|which|how\s+so)\b",
                RegexOptions.IgnoreCase)) return true;

            HashSet<string> playerTokens = DirectReplyTopicTokens(playerMessage);
            HashSet<string> replyTokens = DirectReplyTopicTokens(reply);
            foreach (string token in playerTokens)
                if (replyTokens.Contains(token)) return true;

            reason = "disconnected question instead of replying to the player's topic";
            return false;
        }

        private static HashSet<string> DirectReplyTopicTokens(string text)
        {
            string[] stops = new string[]
            {
                "about", "after", "again", "being", "could", "does", "getting", "have", "into",
                "just", "like", "really", "should", "some", "tell", "than", "that", "their", "them",
                "then", "there", "these", "they", "thing", "think", "this", "those", "what", "when",
                "where", "which", "with", "would", "your", "you're"
            };
            HashSet<string> ignored = new HashSet<string>(stops, StringComparer.OrdinalIgnoreCase);
            HashSet<string> tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MatchCollection matches = Regex.Matches(text ?? string.Empty, @"\b[a-z][a-z0-9']{2,}\b", RegexOptions.IgnoreCase);
            for (int i = 0; i < matches.Count; i++)
                if (!ignored.Contains(matches[i].Value)) tokens.Add(matches[i].Value);
            return tokens;
        }

        internal static bool IsGrounded(string reply, SimMemory memory, WorldSnapshot world, string verifiedSituation, out string reason)
        {
            return IsGrounded(reply, memory, world, verifiedSituation, null, out reason);
        }

        internal static bool IsGrounded(string reply, SimMemory memory, WorldSnapshot world, string verifiedSituation, string referenceCorpus, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(reply)) return true;
            if (HasInstructionLeak(reply))
            {
                reason = "prompt/instruction leak";
                return false;
            }

            if (HasAssistantStyleLanguage(reply))
            {
                reason = "assistant-style sympathy/engagement phrase";
                return false;
            }

            string guidanceReason;
            if (HasUnsupportedGeneratedGuidance(reply, verifiedSituation, referenceCorpus, out guidanceReason))
            {
                reason = guidanceReason;
                return false;
            }

            string verified = BuildVerifiedCorpus(memory, world, verifiedSituation);
            Match unsupportedTime = Regex.Match(reply, @"\b(yesterday|last night|earlier today|this morning)\b", RegexOptions.IgnoreCase);
            if (unsupportedTime.Success && verified.IndexOf(unsupportedTime.Value, StringComparison.OrdinalIgnoreCase) < 0)
            {
                reason = "unsupported precise temporal qualifier";
                return false;
            }
            if (!string.IsNullOrWhiteSpace(verifiedSituation) &&
                (verifiedSituation.StartsWith("Current situation:", StringComparison.OrdinalIgnoreCase) ||
                 verifiedSituation.StartsWith("Verified current-session observation:", StringComparison.OrdinalIgnoreCase)) &&
                Regex.IsMatch(reply, @"^\s*(?:hey|hi|hello|yo)\b", RegexOptions.IgnoreCase))
            {
                reason = "generic greeting instead of a spontaneous observation";
                return false;
            }

            string selfClassReason;
            if (TryFindSelfClassContradiction(reply, memory, world, out selfClassReason))
            {
                reason = selfClassReason;
                return false;
            }

            string selfGuildReason;
            if (TryFindSelfGuildClaimViolation(reply, memory, world, out selfGuildReason))
            {
                reason = selfGuildReason;
                return false;
            }

            string authorityReason;
            if (HasUnsupportedSocialAuthorityClaim(reply, verified, out authorityReason))
            {
                reason = authorityReason;
                return false;
            }

            string assertionReason;
            if (!HasSupportedRiskyAssertions(reply, verified, referenceCorpus, memory, world, out assertionReason))
            {
                reason = assertionReason;
                return false;
            }

            string sharedGuild = SharedKnownGuild(world);
            if (!string.IsNullOrWhiteSpace(sharedGuild) &&
                Regex.IsMatch(reply, @"\b(?:just\s+)?(?:a\s+)?bunch\s+of\s+randoms\b|\bnot\s+(?:really\s+)?in\s+(?:a\s+)?guild\b|\bno\s+(?:real\s+)?(?:guild|ranks?)\b", RegexOptions.IgnoreCase))
            {
                reason = "contradicts verified guild membership";
                return false;
            }

            if (world != null && world.Outing != null && world.Outing.TotalKills > 0 &&
                Regex.IsMatch(reply, @"\b(?:no\s+(?:new\s+)?kills?(?:\s+yet)?|haven't\s+killed\s+(?:anything|anyone)|have\s+not\s+killed\s+(?:anything|anyone)|zero\s+kills?)\b", RegexOptions.IgnoreCase))
            {
                reason = "contradicts verified outing kill count";
                return false;
            }

            if (HasUnsupportedCollectivePlan(reply))
            {
                reason = "unsupported collective plan or stopping decision";
                return false;
            }

            if (HasUnsupportedCausalGuess(reply, verified))
            {
                reason = "unsupported causal explanation for an unexplained event";
                return false;
            }

            if (world != null && world.Outing != null && !string.IsNullOrWhiteSpace(world.Outing.Activity) &&
                world.Outing.Activity.IndexOf("combat", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (Regex.IsMatch(reply, @"\b(?:pretty\s+quiet|quiet\s+(?:lately|today|right\s+now)|nothing\s+(?:is\s+)?(?:going\s+on|happening)|just\s+getting\s+started|haven't\s+started|have\s+not\s+started|waiting\s+around)\b", RegexOptions.IgnoreCase))
                {
                    reason = "contradicts verified live combat state";
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(world.Outing.CurrentCombatTarget) &&
                    Regex.IsMatch(reply, @"\b(?:not\s+sure|don't\s+know|do\s+not\s+know|no\s+idea)\b[^.!?]{0,45}\b(?:fight|fighting|target|enemy)\b", RegexOptions.IgnoreCase))
                {
                    reason = "claims ignorance of a verified current combat target";
                    return false;
                }
            }
            else if (world != null && world.Outing != null &&
                Regex.IsMatch(reply, @"\b(?:waiting\s+for|until)\s+(?:the\s+)?(?:battle|fight|combat)\s+(?:to\s+)?(?:finish|end|be\s+over)\b", RegexOptions.IgnoreCase))
            {
                reason = "claims a live battle while verified state is out of combat";
                return false;
            }

            if (world != null && world.Party != null)
            {
                string[] classes = new string[] { "Paladin", "Reaver", "Druid", "Arcanist", "Stormcaller", "Windblade" };
                for (int i = 0; i < world.Party.Count; i++)
                {
                    SimSnapshot member = world.Party[i];
                    if (member == null || string.IsNullOrWhiteSpace(member.Name) || string.IsNullOrWhiteSpace(member.ClassName)) continue;
                    if (reply.IndexOf(member.Name, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    for (int c = 0; c < classes.Length; c++)
                    {
                        string claimed = classes[c];
                        if (string.Equals(claimed, member.ClassName, StringComparison.OrdinalIgnoreCase)) continue;
                        if (Regex.IsMatch(reply, @"\b" + Regex.Escape(member.Name) + @"\b[^.!?]{0,55}\b" + Regex.Escape(claimed) + @"\b|\b" + Regex.Escape(claimed) + @"\b[^.!?]{0,55}\b" + Regex.Escape(member.Name) + @"\b", RegexOptions.IgnoreCase))
                        {
                            reason = "contradicts verified class identity for " + member.Name;
                            return false;
                        }
                    }
                }
            }

            string identityReason;
            if (TryFindIdentityContradiction(reply, world, out identityReason))
            {
                reason = identityReason;
                return false;
            }

            if (Regex.IsMatch(reply, @"\b(?:i|we|you|they|he|she)\b[^.!?]{0,45}\b(?:got|have|received|found|picked\s+up|ended\s+up\s+with)\b[^.!?]{0,30}\+\d\b", RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(verified, @"\b(?:got|received|found|looted|picked\s+up)\b[^.!?]{0,40}\+\d\b", RegexOptions.IgnoreCase))
            {
                reason = "unsupported enhanced-item ownership claim";
                return false;
            }

            if (Regex.IsMatch(reply, @"\b(?:i|we|you|they|he|she)\b[^.!?]{0,35}\b(?:leveled|levelled|dinged|reached\s+level)\b", RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(verified, @"\b(?:reached\s+level|leveled|levelled|dinged|level\s+up)\b", RegexOptions.IgnoreCase))
            {
                reason = "unsupported level-up claim";
                return false;
            }

            string relationshipReason;
            if (HasUnsupportedRelationshipClaim(reply, memory, out relationshipReason))
            {
                reason = relationshipReason;
                return false;
            }

            if (Regex.IsMatch(reply, @"\b(?:last\s+(?:run|time|session)|earlier\s+(?:run|today)|we\s+(?:finished|cleared|farmed|got|received|found|picked\s+up))\b", RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(verified, @"\b(?:last\s+(?:run|time|session)|finished|cleared|farmed|received|found|picked\s+up)\b", RegexOptions.IgnoreCase))
            {
                reason = "unsupported shared-history claim";
                return false;
            }

            // Same pattern as the shared-history/"again" guard above, applied forward instead of back:
            // a continuation must not invent a future shared plan or outing ("next run", "when we go
            // back") unless that plan is itself a VERIFIED fact.
            if (Regex.IsMatch(reply, @"\b(?:next\s+(?:run|time|trip|outing)|when\s+we\s+(?:go\s+back|come\s+back|return)|come\s+back\s+(?:here|to\s+this)|the\s+next\s+time\s+we)\b", RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(verified, @"\b(?:next\s+(?:run|time|trip|outing)|planned\s+return|scheduled\s+return)\b", RegexOptions.IgnoreCase))
            {
                reason = "unsupported future shared-plan reference";
                return false;
            }

            if (LooksLikeHistoricalExperienceClaim(reply))
            {
                string historical = BuildHistoricalCorpus(memory);
                if (!HasHistoricalAnchorOverlap(reply, historical))
                {
                    reason = "unsupported temporal/personal-history claim";
                    return false;
                }
            }

            string combatHistoryReason;
            if (HasUnsupportedNamedCombatHistory(reply, verified, out combatHistoryReason))
            {
                reason = combatHistoryReason;
                return false;
            }

            string entityReason;
            if (!HasEntitySpecificLifeDeathSupport(reply, verified, out entityReason))
            {
                reason = entityReason;
                return false;
            }

            if (HasSpeakerNarrationLeak(reply, world))
            {
                reason = "speaker/narration leak";
                return false;
            }

            Match leadingLabel = Regex.Match(reply, @"^\s*([A-Za-z][A-Za-z0-9'_-]{0,31}|you|player)\s*:\s*", RegexOptions.IgnoreCase);
            if (leadingLabel.Success && IsKnownSpeakerLabel(leadingLabel.Groups[1].Value, memory, world))
            {
                reason = "speaker prefix/impersonation leak";
                return false;
            }

            return true;
        }

        // Generated dialogue is expression, never a quest guide. Precise document locators and
        // actionable quest/map directions require matching verified or retrieved support; a bare
        // model invention such as "check page 4" must not cross the grounding boundary.
        internal static bool HasUnsupportedGeneratedGuidance(string reply, string verifiedSituation,
            string referenceCorpus, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(reply)) return false;
            Match locator = Regex.Match(reply, @"\b(?:page|chapter|section)\s+(\d{1,3}|[ivxlcdm]{1,8})\b", RegexOptions.IgnoreCase);
            bool actionableQuest = Regex.IsMatch(reply,
                @"\b(?:check|read|see|look\s+(?:at|in)|open|turn\s+to|go\s+to|talk\s+to|find)\b[^.!?]{0,55}\b(?:quest|questlog|quest\s+log|map|page|chapter|section)\b",
                RegexOptions.IgnoreCase);
            if (!locator.Success && !actionableQuest) return false;

            string support = (verifiedSituation ?? string.Empty) + "\n" + (referenceCorpus ?? string.Empty);
            if (locator.Success && support.IndexOf(locator.Value, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (actionableQuest && Regex.IsMatch(support,
                @"\b(?:check|read|see|look\s+(?:at|in)|open|turn\s+to|go\s+to|talk\s+to|find)\b[^.!?]{0,55}\b(?:quest|questlog|quest\s+log|map|page|chapter|section)\b",
                RegexOptions.IgnoreCase)) return false;

            reason = "unsupported quest/map/page guidance";
            return true;
        }

        private static bool IsKnownSpeakerLabel(string label, SimMemory memory, WorldSnapshot world)
        {
            if (string.IsNullOrWhiteSpace(label)) return false;
            if (string.Equals(label, "you", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(label, "player", StringComparison.OrdinalIgnoreCase)) return true;
            if (memory != null && !string.IsNullOrWhiteSpace(memory.Name) &&
                string.Equals(label, memory.Name, StringComparison.OrdinalIgnoreCase)) return true;
            if (world != null && world.Player != null && !string.IsNullOrWhiteSpace(world.Player.Name) &&
                string.Equals(label, world.Player.Name, StringComparison.OrdinalIgnoreCase)) return true;
            if (world != null && world.Party != null)
            {
                for (int i = 0; i < world.Party.Count; i++)
                {
                    SimSnapshot member = world.Party[i];
                    if (member != null && !string.IsNullOrWhiteSpace(member.Name) &&
                        string.Equals(label, member.Name, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        internal static List<string> RunSelfTests()
        {
            SimSnapshot phanty = new SimSnapshot();
            phanty.Name = "Phanty";
            phanty.ClassName = "Arcanist";
            phanty.GuildName = "Lantern";
            WorldSnapshot world = new WorldSnapshot();
            world.Party = new List<SimSnapshot>();
            world.Party.Add(phanty);
            world.Outing = new OutingSnapshot();
            world.Outing.Activity = "adventuring/downtime";
            world.Outing.TotalKills = 3;
            SimMemory memory = new SimMemory();
            memory.Normalize();
            memory.Name = "Phanty";

            List<string> results = new List<string>();
            AddSelfTestResult(results, "unsupported history", "Hope my pet does not bite me again.", false, memory, world);
            AddSelfTestResult(results, "wrong class", "Phanty is a Paladin.", false, memory, world);
            AddSelfTestResult(results, "wrong self class", "I'm a Druid.", false, memory, world);
            AddSelfTestResult(results, "general class discussion", "Paladins are sturdy.", true, memory, world);
            AddSelfTestResult(results, "wrong role", "Phanty is a plate tank.", false, memory, world);
            phanty.ClassName = "Paladin";
            phanty.RoleAssignmentsKnown = true;
            phanty.AssignedRoles = new List<string> { "Crowd Control" };
            AddSelfTestResult(results, "wrong exact Manage Roles assignment", "Phanty is our assigned Main Tank.", false, memory, world);
            AddSelfTestResult(results, "verified exact Manage Roles assignment", "Phanty is assigned Crowd Control.", true, memory, world);
            phanty.ClassName = "Arcanist";
            AddSelfTestResult(results, "wrong guild", "Phanty is in the Thunder guild.", false, memory, world);
            AddSelfTestResult(results, "verified self guild membership", "I'm with Lantern.", true, memory, world);
            AddSelfTestResult(results, "unknown recruitment authority", "I can get you into Lantern.", false, memory, world);
            AddSelfTestResult(results, "unknown invite authority", "I can invite you into the guild.", false, memory, world);
            AddSelfTestResult(results, "unknown bare invite authority", "I can invite you.", false, memory, world);
            AddSelfTestResult(results, "ordinary social invitation stays allowed", "I can invite you to dinner.", true, memory, world);
            AddSelfTestResult(results, "unknown promotion authority", "I'll promote you tomorrow.", false, memory, world);
            AddSelfTestResult(results, "unknown kick authority", "I can kick them from the guild.", false, memory, world);
            AddSelfTestResult(results, "harmless guild question has no authority claim", "Have you ever thought about joining a guild?", true, memory, world);
            AddSelfTestResult(results, "unknown item-give authority", "I'll give you a sword.", false, memory, world);
            AddSelfTestResult(results, "unknown item-acquisition authority", "I'll get you a sword.", false, memory, world);
            AddSelfTestResult(results, "unknown teaching authority", "I can teach you that spell.", false, memory, world);
            AddSelfTestResult(results, "ordinary future social language stays allowed", "I'll give you a hand.", true, memory, world);
            phanty.GuildName = string.Empty;
            AddSelfTestResult(results, "unknown self guild membership", "I'm with Dragon Food.", false, memory, world);
            phanty.GuildName = "Lantern";
            string capabilityReason;
            bool explicitCapability = IsGrounded("I can invite you into the guild.", memory, world, "[capability:guild_invite]", out capabilityReason);
            results.Add("[DeepSims Guard] explicit verified invite capability permits promise: " +
                (explicitCapability ? "PASS" : "FAIL") + " (" + (string.IsNullOrWhiteSpace(capabilityReason) ? "accepted" : capabilityReason) + ")");
            AddSelfTestResult(results, "false no-new-kills claim", "No new kills yet in Brakke today.", false, memory, world);
            AddSelfTestResult(results, "invented group plan", "Let's stay in Brakke and stop hunting for today.", false, memory, world);
            AddSelfTestResult(results, "verified identity", "Phanty is an Arcanist.", true, memory, world);
            AddSelfTestResult(results, "acknowledgement got it", "got it", true, memory, world);
            AddSelfTestResult(results, "acknowledgement got you", "I've got you", true, memory, world);
            AddSelfTestResult(results, "unverified acquired sword", "I got the sword", false, memory, world);
            AddSelfTestResult(results, "unverified named loot", "Ruby was looted.", false, memory, world);
            AddSelfTestResult(results, "lowercase numeric kill", "we killed three goblins", false, memory, world);
            AddSelfTestResult(results, "first-person loot", "i looted a sword", false, memory, world);
            AddSelfTestResult(results, "pronoun death", "he died in that fight", false, memory, world);
            AddSelfTestResult(results, "plural numeric wipe", "we wiped twice", false, memory, world);
            AddSelfTestResult(results, "deictic passive drop", "that boss dropped this", false, memory, world);
            AddSelfTestResult(results, "generic repeat acquisition", "got another one", false, memory, world);
            AddSelfTestResult(results, "generic prior clear", "we cleared it before", false, memory, world);
            AddSelfTestResult(results, "usual-history invention", "that hurt Phanty hard, just like usual", false, memory, world);
            AddSelfTestResult(results, "unverified quest count", "we completed two quests", false, memory, world);
            AddSelfTestResult(results, "unverified spell action", "i cast Regrowth on you", false, memory, world);
            AddSelfTestResult(results, "unverified gear ownership", "i have a +2 sword", false, memory, world);
            AddSelfTestResult(results, "harmless loot opinion", "loot is nice", true, memory, world);
            AddSelfTestResult(results, "harmless boss opinion", "bosses are rough", true, memory, world);
            AddSelfTestResult(results, "harmless healing opinion", "healing feels good", true, memory, world);
            AddSelfTestResult(results, "harmless gear opinion", "swords look great", true, memory, world);
            AddSelfTestResult(results, "unsupported current food claim", "I'm eating a hot stew right now.", false, memory, world);
            AddSelfTestResult(results, "unsupported munching claim", "I'm just munching on some snacks.", false, memory, world);
            AddSelfTestResult(results, "hypothetical food preference is allowed", "I'd go for something warm.", true, memory, world);
            AddSelfTestResult(results, "unsupported individual mana claim", "My mana is low right now.", false, memory, world);
            AddSelfTestResult(results, "hedged mana guess is allowed", "I'd rather save my mana for later.", true, memory, world);
            AddSelfTestResult(results, "unsupported causal guess for vanished party", "They probably respawned somewhere else.", false, memory, world);
            AddSelfTestResult(results, "unsupported causal guess for disconnect", "Another squad must have aggroed them.", false, memory, world);
            AddSelfTestResult(results, "plain reaction to unexplained event is allowed", "Yeah, that was weird.", true, memory, world);

            SimMemory established = new SimMemory();
            established.Normalize();
            established.Name = "Phanty";
            established.Familiarity = 0.85f;
            established.Rapport = 0.70f;
            established.Rivalry = 0.65f;
            established.Conversation.Add(new ChatMessage("assistant", "we are best friends"));
            AddSelfTestResult(results, "familiarity cannot fabricate an event", "Remember when we killed that dragon?", false, established, world);
            AddSelfTestResult(results, "rapport is not friendship evidence", "We're best friends.", false, established, world);
            AddSelfTestResult(results, "rivalry is not duel evidence", "You and I have dueled before.", false, established, world);
            AddSelfTestResult(results, "conversation cannot establish relationship fact", "We're friends.", false, established, world);
            SimMemory newlyMet = new SimMemory();
            newlyMet.Normalize();
            AddSelfTestResult(results, "new Sims cannot claim lifelong history", "We've known each other forever.", false, newlyMet, world);
            AddSelfTestResult(results, "established Sims may sound casual", "yo, good to see you", true, established, world);
            established.RecentEvents.Add(new MemoryEvent { type = "friendly_duel", text = "The player won a friendly practice duel against Phanty." });
            AddSelfTestResult(results, "verified duel permits prior duel reference", "You and I have dueled before.", true, established, world);
            memory.RecentEvents.Add(new MemoryEvent { type = "loot", text = "Aetheria was found after the fight." });
            AddSelfTestResult(results, "verified named loot", "Aetheria was looted.", true, memory, world);

            world.Outing.Facts = new List<string>();
            world.Outing.Facts.Add("Killed goblin x3.");
            world.Outing.Facts.Add("Phanty looted Bronze Sword.");
            world.Outing.Facts.Add("The party wiped 2 times.");
            world.Outing.Facts.Add("Phanty healed the player with Regrowth.");
            world.Outing.Facts.Add("Phanty cast Regrowth on the player.");
            world.Outing.Facts.Add("Completed 2 quests this outing.");
            world.Outing.Facts.Add("The player died 1 time this outing.");
            AddSelfTestResult(results, "exact numeric kill evidence", "we killed three goblins", true, memory, world);
            AddSelfTestResult(results, "exact first-person loot evidence", "i looted a bronze sword", true, memory, world);
            AddSelfTestResult(results, "exact wipe evidence", "we wiped twice", true, memory, world);
            AddSelfTestResult(results, "exact heal evidence", "Phanty healed the player with Regrowth", true, memory, world);
            AddSelfTestResult(results, "exact spell evidence", "i cast Regrowth on you", true, memory, world);
            AddSelfTestResult(results, "exact quest evidence", "we completed two quests", true, memory, world);
            AddSelfTestResult(results, "exact player death evidence", "you died once", true, memory, world);

            WikiResult knowledge = new WikiResult();
            knowledge.Found = true;
            knowledge.Title = "Aetheria";
            knowledge.Query = "where does Aetheria come from";
            knowledge.Extract = "Aetheria drops from the Lost Sea Giant in Azure Cove.";
            AddKnowledgeSelfTestResult(results, "knowledge exact drop relation", "Aetheria drops from the Lost Sea Giant.", true, memory, world, knowledge);
            AddKnowledgeSelfTestResult(results, "knowledge invented vendor", "Mira sells Aetheria in Brakke.", false, memory, world, knowledge);
            AddKnowledgeSelfTestResult(results, "knowledge invented location", "Aetheria is in Brakke.", false, memory, world, knowledge);
            AddKnowledgeSelfTestResult(results, "knowledge invented quantity", "Three Aetheria drop from the Lost Sea Giant.", false, memory, world, knowledge);
            AddKnowledgeSelfTestResult(results, "knowledge invented requirement", "Aetheria requires level 20.", false, memory, world, knowledge);
            AddKnowledgeSelfTestResult(results, "knowledge invented crafting method", "Aetheria is crafted in Brakke.", false, memory, world, knowledge);
            AddKnowledgeSelfTestResult(results, "knowledge invented reward relation", "Mira gives Aetheria.", false, memory, world, knowledge);
            results.AddRange(SessionTelemetry.RunAttributionSelfTests());
            results.Add("[DeepSims Guard] short acknowledgement dedupe: " + (!IsTooSimilar("nice", "nice") ? "PASS" : "FAIL"));
            return results;
        }

        internal static bool IsKnowledgeModeGrounded(string reply, SimMemory memory, WorldSnapshot world, WikiResult externalFacts, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(reply)) return true;
            if (externalFacts == null) return true;

            if (Regex.IsMatch(reply, @"\b(?:wiki|retrieval|lookup|source\s+page|matched\s+page|page\s+extract|wiki\s+examples?)\b", RegexOptions.IgnoreCase))
            {
                reason = "knowledge-source/meta reference";
                return false;
            }

            if (Regex.IsMatch(reply, @"\b(?:i(?:'ve| have)|we(?:'ve| have))\s+(?:been|already|personally)\b|\bsince\s+day\s+one\b|\bi\s+(?:remember|waited|was\s+waiting|got\s+mine|found\s+mine)\b", RegexOptions.IgnoreCase))
            {
                string verified = BuildVerifiedCorpus(memory, world, null);
                if (!Regex.IsMatch(verified, @"\b(?:remember|waited|waiting|got|found|received|since day one)\b", RegexOptions.IgnoreCase))
                {
                    reason = "unsupported personal-history claim in factual knowledge mode";
                    return false;
                }
            }

            // Provenance-aware boundary: externalFacts is already scoped to whichever corpus produced it
            // (Erenshor wiki/lore vs the external real-world news bundle). This check never compares an
            // ExternalNews claim against Erenshor game state - it only ever validates against the same
            // bundle that was retrieved, which is the correct trust boundary for EXTERNAL_NEWS scope.
            bool isExternalRealWorldNews = !string.IsNullOrWhiteSpace(externalFacts.SourceLabel) &&
                externalFacts.SourceLabel.IndexOf("external real-world news", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isExternalRealWorldNews && externalFacts.Found && ContradictsRetrievedNews(reply))
            {
                reason = "contradicted retrieved evidence";
                return false;
            }

            if (externalFacts.Found && !string.IsNullOrWhiteSpace(externalFacts.Extract) && !HasKnowledgeRelationSupport(reply, externalFacts))
            {
                reason = isExternalRealWorldNews
                    ? "answer combines or invents a relationship not present in a single retrieved news headline"
                    : "answer relationship/entities are not supported by the retrieved game facts";
                return false;
            }

            if (isExternalRealWorldNews && externalFacts.Found)
            {
                if (Regex.IsMatch(reply, @"\b(?:again|once more|still\s+\w+ing|another\s+time)\b", RegexOptions.IgnoreCase) &&
                    !Regex.IsMatch(externalFacts.Extract, @"\b(?:again|repeat|another|delay|previously|prior|second\s+time)\b", RegexOptions.IgnoreCase))
                {
                    reason = "implies unsupported repeated/historical real-world pattern not present in retrieved headlines";
                    return false;
                }
            }

            if (!externalFacts.Found)
            {
                if (!HasUncertaintyLanguage(reply))
                {
                    reason = "factual claim despite failed external lookup";
                    return false;
                }
            }
            return true;
        }

        internal static bool ContradictsRetrievedNews(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply)) return false;
            return Regex.IsMatch(reply,
                @"\bno\b[^.!?]{0,48}\b(?:news|headlines?|updates?)\b|\bnothing\s+(?:new|happening)\b|\b(?:isn['’]?t|aren['’]?t)\s+any\s+(?:news|update)\b|\bdoesn['’]?t\s+seem\s+to\s+be\s+any\s+(?:news|update)\b|\bi\s+(?:have not|haven['’]?t)\s+heard\s+(?:anything|any\s+news)\b",
                RegexOptions.IgnoreCase);
        }

        internal static string ExternalNewsEvidenceFallback(WikiResult facts)
        {
            if (facts == null || !facts.Found || string.IsNullOrWhiteSpace(facts.Extract))
                return "couldn't find anything relevant right now";
            string first = facts.Extract.Split(new string[] { " | " }, StringSplitOptions.RemoveEmptyEntries)[0];
            first = Regex.Replace(first, @"^SOURCE DISAGREEMENT POSSIBLE[^:]*:\s*", string.Empty, RegexOptions.IgnoreCase);
            first = Regex.Replace(first, @"^\[[^\]]{1,120}\]\s*", string.Empty).Trim();
            if (first.Length > 180) first = first.Substring(0, 177).TrimEnd() + "...";
            return string.IsNullOrWhiteSpace(first) ? "couldn't find anything relevant right now" : "looks like " + first;
        }

        internal static string KnowledgeCorrectionPrompt(string badReply, string reason)
        {
            return "Your previous factual answer was rejected because it contained " + reason + ". " +
                   "Answer the player's Erenshor question again using ONLY the UNVERIFIED REFERENCE TEXT and VERIFIED CURRENT FACTS already supplied. " +
                   "Do not invent personal history, ownership, prior experience, vendors, levels, requirements, locations, or methods not stated in those facts. " +
                   "Answer only what the player asked, preferably in one short sentence. Do not mention a wiki, source, lookup, retrieval, or page. If the facts are insufficient, say you are not sure. Return only the replacement chat line.\n" +
                   "Rejected draft: " + badReply;
        }

        // Retry corrective prompt specific to EXTERNAL_NEWS scope (Goal 3). The generic
        // KnowledgeCorrectionPrompt is reused for wiki/lore retries; this one calls out the exact
        // failure mode observed live - combining two separate headlines into one invented relationship
        // or implying unsupported prior history - and instructs the model to stick to one headline at a
        // time rather than drifting into generic party chatter. The caller must supply the SAME
        // ExternalNewsBundle-derived reference corpus on retry so the answer stays grounded in real
        // retrieved headlines instead of falling back to ungrounded small talk.
        internal static string ExternalNewsCorrectionPrompt(string badReply, string reason)
        {
            return "Your previous answer was rejected because it " + reason + ". " +
                   "Use only facts explicitly present in the supplied news items below. Do not combine two separate headlines into one invented relationship or cause. " +
                   "Choose one concrete supplied headline and state its actual news directly. Do not ask which headline the player means and do not call it a rumor, 'new thing', or vague news. " +
                   "Do not imply prior history or repetition ('again', 'still') unless the headline says so. Answer only what the player asked, in one short sentence. Do not mention a wiki, source, lookup, retrieval, or page. If the supplied headlines are not enough to answer, say so honestly instead of guessing. Return only the replacement chat line.\n" +
                   "Rejected draft: " + badReply;
        }

        internal static bool HasInstructionLeak(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] phrases = new string[]
            {
                "no new game event was observed",
                "start a short exchange",
                "harmless current topic",
                "visible party composition",
                "verified director situation",
                "verified current facts",
                "verified observed",
                "unverified prior group chat",
                "unverified recent private chat",
                "allowed flavor",
                "forbidden inventions",
                "truth / memory rules",
                "current class terminology",
                "silence is normal",
                "return exactly no_message",
                "manual test mode",
                "manual behavior test",
                "do not claim anyone",
                "hidden instructions",
                "context window",
                "you were not directly asked a question"
            };
            string lower = text.ToLowerInvariant();
            for (int i = 0; i < phrases.Length; i++)
                if (lower.IndexOf(phrases[i], StringComparison.Ordinal) >= 0) return true;

            if (Regex.IsMatch(text, @"\bdeep\s?sims\b[^.!?]{0,35}\b(?:asked|prompted|selected|instructed|told)\s+me\b", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(text, @"\b(?:asked|prompted|selected|instructed|told)\s+(?:me\s+)?by\s+deep\s?sims\b", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(text, @"\bdeep\s?sims\b[^.!?]{0,35}\bgenerated\s+(?:this|the)\s+(?:reply|response|message|line)\b", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(text, @"\b(?:use|using)\s+only\b.*\b(?:topic|facts?|context)\b", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(text, @"\bdo\s+not\s+(?:invent|claim|mention|repeat|quote)\b", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(text, @"\breturn\s+(?:only|exactly)\b.*\b(?:message|reply|chat|no_message)\b", RegexOptions.IgnoreCase)) return true;
            return false;
        }

        internal static bool HasUncertaintyLanguage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return Regex.IsMatch(text,
                @"\b(?:not(?:\s+too|\s+really)?\s+sure|don't know|do not know|couldn't find|could not find|no idea|unsure|haven't heard|have not heard|beats me|can't place|cannot place|couldn't tell you|could not tell you)\b",
                RegexOptions.IgnoreCase);
        }

        internal static bool IsSubjectiveDeflection(PartyReplyIntent intent, string text)
        {
            // Casual uncertainty is normal subjective speech. Assistant/engineering boilerplate is
            // rejected independently by the output guard and SocialClaimClassifier.
            return false;
        }

        internal static bool HasAssistantStyleLanguage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return Regex.IsMatch(text,
                @"\b(?:nice|good|great|glad|happy)\s+to\s+hear\s+(?:that\s+)?you(?:'re|\s+are|'ve|\s+have)\b|" +
                @"\bthanks\s+for\s+sharing\b|\bsounds\s+like\s+you(?:'re|\s+are)\b|" +
                @"\bi(?:'m|\s+am)\s+here\s+if\s+you\b|\bwhat(?:'s|\s+is)\s+on\s+your\s+mind\b|" +
                @"\bhow\s+can\s+i\s+(?:help|assist)\b|\bwhat\s+would\s+you\s+like\s+to\s+discuss\b",
                RegexOptions.IgnoreCase);
        }

        internal static bool IsTooSimilar(string first, string second)
        {
            string a = Normalize(first);
            string b = Normalize(second);
            if (a.Length == 0 || b.Length == 0) return false;
            if (WordCount(a) <= 3 || WordCount(b) <= 3) return false;
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            if (a.Length >= 16 && b.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (b.Length >= 16 && a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            HashSet<string> at = TokenSet(a);
            HashSet<string> bt = TokenSet(b);
            if (at.Count == 0 || bt.Count == 0) return false;
            int intersection = 0;
            foreach (string token in at) if (bt.Contains(token)) intersection++;
            int union = at.Count + bt.Count - intersection;
            if (union <= 0) return false;
            return ((double)intersection / (double)union) >= 0.58;
        }

        private static int WordCount(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            return Regex.Matches(value, @"[a-z0-9']+", RegexOptions.IgnoreCase).Count;
        }

        internal static string CorrectionPrompt(string badReply, string reason)
        {
            return "Your previous draft was rejected because it contained " + reason + ". " +
                   "Rewrite the answer from scratch using ONLY VERIFIED current facts/events already supplied. " +
                   "Keep self-identity consistent with verified class and guild facts. Do not claim guild membership when it is unknown. " +
                   "Do not promise recruitment/invites, promotions, kicking, items, acquisition, teaching, unlocking, or grants unless a VERIFIED CAPABILITY explicitly authorizes it. " +
                   "Do not add unverified party readiness/recovery, damage, deaths, loot state, plans, or repeated history. " +
                   "Do not mention raids, bosses, loot, drops, gear, quests, wipes, deaths, kills, or prior runs unless a VERIFIED section explicitly contains that subject. " +
                   "Do not refer to an earlier fight, named opponent, or personal history unless that exact event and name appear in VERIFIED facts. For ordinary small talk, a short mood/opinion or harmless preference is enough. For spontaneous talk, bring up the verified situation directly instead of greeting the party. " +
                   "Do not refer to yourself or another party member as '<name> says'. Return only the replacement chat line.\n" +
                   "Rejected draft: " + badReply;
        }

        internal static string BanterCorrectionPrompt(string firstSpeaker, string firstText, string badReply, string reason)
        {
            return "Your previous banter reply was rejected because it had " + reason + ". " +
                   "Reply naturally to " + firstSpeaker + "'s actual words, add a NEW thought, and do not quote or paraphrase most of their line. " +
                   "Do not invent events or history. If no grounded response fits, return exactly NO_MESSAGE.\n" +
                   firstSpeaker + " said: \"" + firstText + "\"\n" +
                   "Rejected draft: " + badReply;
        }

        internal static string SafePrivateFallback(string userMessage)
        {
            string value = (userMessage ?? string.Empty).ToLowerInvariant();
            if (value.Contains("current situation:") || value.Contains("verified current-session observation:"))
                return "pretty quiet right now";
            if (value.Contains("how are you") || value.Contains("how's it going") || value.Contains("hows it going") ||
                value.Contains("session going") || value.Contains("how is our session") || value.Contains("how's our session"))
                return "doing alright so far";
            return "not much to add yet";
        }

        internal static bool IsRiskyStyleExample(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return Regex.IsMatch(text, @"\b(?:raid|boss|loot|looted|drop|drops|gear|quest|quests|wipe|wiped|died|dead|killed|slain|received|got an item)\b", RegexOptions.IgnoreCase);
        }

        private static bool HasSupportedRiskyAssertions(string reply, string verified, string referenceCorpus, SimMemory memory, WorldSnapshot world, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(reply)) return true;
            string[] clauses = Regex.Split(reply, @"[.!?\r\n]+");
            for (int i = 0; i < clauses.Length; i++)
            {
                string clause = clauses[i].Trim();
                if (clause.Length == 0) continue;
                if (IsGeneralizationOpinion(clause)) continue;

                if (IsCollectiveReadinessAssertion(clause) && !HasCollectiveReadinessEvidence(verified))
                    return FailAssertion("party readiness/recovery", out reason);

                if (IsDamageAssertion(clause) && !HasDamageEvidence(clause, verified, memory, world))
                    return FailAssertion("damage/current injury", out reason);

                if (Regex.IsMatch(clause, @"\bwip(?:e|ed|ing)s?\b", RegexOptions.IgnoreCase) &&
                    !HasAssertionEvidence(clause, verified, memory, world, @"\bwip(?:e|ed|ing)s?\b", true))
                    return FailAssertion("wipe", out reason);

                if (Regex.IsMatch(clause, @"\b(?:killed|slain|defeated|beat|cleared)\b", RegexOptions.IgnoreCase) &&
                    !Regex.IsMatch(clause, @"\b(?:completed|finished|cleared)\b[^.!?]{0,45}\bquests?\b", RegexOptions.IgnoreCase) &&
                    !HasAssertionEvidence(clause, verified, memory, world, @"\b(?:kill(?:ed|s|ing)?|slain|defeat(?:ed|s|ing)?|beat|cleared)\b", false))
                    return FailAssertion("kill/clear", out reason);

                if (Regex.IsMatch(clause, @"\b(?:died|dead|death|deaths|respawned|revived)\b", RegexOptions.IgnoreCase) &&
                    !HasAssertionEvidence(clause, verified, memory, world, @"\b(?:died|dead|death|deaths|respawned|revived|defeated)\b", false) &&
                    !HasActorOnlyLifeEvidence(clause, verified, memory, world))
                    return FailAssertion("death/revive", out reason);

                if (IsCombatAssertion(clause) &&
                    !HasAssertionEvidence(clause, verified, memory, world, @"\b(?:fight(?:ing|s)?|fought|attack(?:ed|ing|s)?|pull(?:ed|ing|s)?|raid(?:ed|ing|s)?)\b", false))
                    return FailAssertion("combat", out reason);

                if (IsAcquisitionAssertion(clause) &&
                    !HasAssertionEvidence(clause, verified, memory, world, @"\b(?:loot(?:ed|ing)?|received|found|picked\s+up|got|obtained|gave|given)\b", false))
                    return FailAssertion("loot/acquisition", out reason);

                if (Regex.IsMatch(clause, @"\b(?:drop(?:s|ped|ping)?|comes?\s+from|drop\s+rate)\b", RegexOptions.IgnoreCase) &&
                    !HasAssertionEvidence(clause, verified, memory, world, @"\b(?:drop(?:s|ped|ping)?|comes?\s+from|source)\b", false) &&
                    !HasAssertionEvidence(clause, referenceCorpus, memory, world, @"\b(?:drop(?:s|ped|ping)?|comes?\s+from|source)\b", false))
                    return FailAssertion("drop/source relationship", out reason);

                if (IsGearOwnershipAssertion(clause) &&
                    !HasAssertionEvidence(clause, verified, memory, world, @"\b(?:loot(?:ed)?|received|found|picked\s+up|got|obtained|own(?:s|ed)?|equipp(?:ed|ing)|wear(?:s|ing)?|has|have|had)\b", false))
                    return FailAssertion("gear ownership", out reason);

                if (Regex.IsMatch(clause, @"\b(?:completed|finished|cleared)\b[^.!?]{0,45}\bquests?\b|\bquests?\b[^.!?]{0,45}\b(?:completed|finished|cleared)\b", RegexOptions.IgnoreCase) &&
                    !HasAssertionEvidence(clause, verified, memory, world, @"\b(?:completed|finished|cleared)\b[^.!?]{0,45}\bquests?\b|\bquests?\b[^.!?]{0,45}\b(?:completed|finished|cleared)\b", true))
                    return FailAssertion("quest completion", out reason);

                if (IsSpellOrHealAssertion(clause))
                {
                    string actionSupport = Regex.IsMatch(clause, @"\bcast(?:ing|s)?\b", RegexOptions.IgnoreCase) ? @"\bcast(?:ing|s)?\b" :
                        (Regex.IsMatch(clause, @"\bbuff(?:ed|ing|s)?\b", RegexOptions.IgnoreCase) ? @"\bbuff(?:ed|ing|s)?\b" :
                        (Regex.IsMatch(clause, @"\bassist(?:ed|ing|s)?\b", RegexOptions.IgnoreCase) ? @"\bassist(?:ed|ing|s)?\b" : @"\b(?:heal(?:ed|ing|s)?|saved)\b"));
                    if (!HasAssertionEvidence(clause, verified, memory, world, actionSupport, false))
                        return FailAssertion("spell/heal/action", out reason);
                }

                // Real-packet prompt labs (local-labs/4b-prompt-lab-v3..v5) repeatedly found the model
                // will state a current meal or an individual resource level as present-tense fact when
                // asked ("I'm eating stew", "my mana is low") even though neither was ever supplied as
                // evidence. A hypothetical/preference framing ("I'd go for something warm", "I'd rather
                // save my mana") is explicitly NOT flagged - only a present-tense claim of what is
                // actually happening right now requires support.
                if (IsCurrentFoodAssertion(clause) &&
                    !HasAssertionEvidence(clause, verified, memory, world, @"\b(?:eat(?:ing|s)?|meal|food|snack)\b", false))
                    return FailAssertion("current food/meal", out reason);

                if (IsIndividualResourceAssertion(clause) &&
                    !HasAssertionEvidence(clause, verified, memory, world, @"\b(?:mana|mp|hp|health|stamina)\b", false))
                    return FailAssertion("current resource/mana state", out reason);
            }
            return true;
        }

        // Present-tense first-person "I am currently eating/having X" - not a preference or hypothetical
        // ("I'd go for...", "I like...", "I'd rather have..."), an assertion about what is happening now.
        private static bool IsCurrentFoodAssertion(string clause)
        {
            if (!Regex.IsMatch(clause, @"\b(?:i'?m|i\s+am|we'?re|we\s+are)\s+(?:just\s+)?(?:currently\s+)?(?:eating|munching(?:\s+on)?|having|snacking(?:\s+on)?)\b", RegexOptions.IgnoreCase)) return false;
            return true;
        }

        // A present-tense first-person claim about current mana/health/stamina level. Hedged guesses
        // ("I'd rather save my mana", "I might be low on mana") are hypotheticals, not a state report.
        private static bool IsIndividualResourceAssertion(string clause)
        {
            if (!Regex.IsMatch(clause, @"\bmy\s+(?:mana|mp|hp|health|stamina)\s+(?:is|are)\b|\b(?:i'?m|i\s+am)\s+(?:out\s+of|low\s+on|full\s+on)\s+(?:mana|mp|hp|health|stamina)\b", RegexOptions.IgnoreCase)) return false;
            if (Regex.IsMatch(clause, @"\b(?:i'd|i\s+would|might|probably|maybe|i\s+think|i\s+guess)\b", RegexOptions.IgnoreCase)) return false;
            return true;
        }

        // Bounded distinction between a GENERAL OPINION/GENERALIZATION ("healing tanks can be rough
        // lately", "the RNG is usually bad at healing tanks") and a concrete ACTION FACT claim ("I
        // healed you with Regrowth"). A clause carrying a hedge/generalization marker and no
        // actor+past-tense action pairing is flavor talk, not an assertion that needs game-fact
        // evidence. This deliberately stays conservative: any concrete actor performing a specific past
        // action (i/we/you/he/she/they + healed/cast/killed/looted/...) still requires evidence.
        private static bool IsGeneralizationOpinion(string clause)
        {
            if (string.IsNullOrWhiteSpace(clause)) return false;
            if (!Regex.IsMatch(clause,
                @"\b(?:usually|generally|often|typically|tends?\s+to|can\s+be|seems?\s+to\s+be|lately|these\s+days|sometimes|always\s+get(?:s)?\s+blamed)\b",
                RegexOptions.IgnoreCase)) return false;
            if (Regex.IsMatch(clause,
                @"\b(?:i|we|you|he|she|they)\b[^.!?]{0,20}\b(?:healed|heals|cast|casted|buffed|assisted|saved|killed|slain|looted|found|received|died|dead|wiped)\b",
                RegexOptions.IgnoreCase)) return false;
            if (Regex.IsMatch(clause, @"\b(?:again|before|last\s+time|remember|used\s+to)\b", RegexOptions.IgnoreCase)) return false;
            return true;
        }

        private static bool FailAssertion(string name, out string reason)
        {
            reason = "unsupported " + name + " assertion";
            return false;
        }

        private static bool IsCollectiveReadinessAssertion(string clause)
        {
            if (string.IsNullOrWhiteSpace(clause)) return false;
            bool collective = Regex.IsMatch(clause, @"\b(?:party|group|we|everyone|everybody|all\s+of\s+us)\b", RegexOptions.IgnoreCase);
            if (!collective) return false;
            return Regex.IsMatch(clause,
                @"\b(?:fully\s+recovered|recovered\s+now|ready\s+to\s+go|good\s+to\s+go|full\s+(?:health|hp|mana|mp)|topped\s+off|all\s+healed|fully\s+healed|back\s+to\s+full)\b",
                RegexOptions.IgnoreCase);
        }

        private static bool HasCollectiveReadinessEvidence(string verified)
        {
            if (string.IsNullOrWhiteSpace(verified)) return false;
            return Regex.IsMatch(verified,
                @"\b(?:party|group|everyone|all\s+of\s+us|we)\b[^.!?\r\n]{0,70}\b(?:fully\s+recovered|recovered|ready\s+to\s+go|good\s+to\s+go|full\s+(?:health|hp|mana|mp)|topped\s+off|all\s+healed|fully\s+healed|back\s+to\s+full)\b",
                RegexOptions.IgnoreCase);
        }

        private static bool IsDamageAssertion(string clause)
        {
            if (string.IsNullOrWhiteSpace(clause)) return false;
            if (Regex.IsMatch(clause, @"^\s*(?:damage|getting\s+hit|taking\s+damage)\s+(?:is|feels|can\s+be)\b", RegexOptions.IgnoreCase)) return false;
            return Regex.IsMatch(clause,
                @"\b(?:hurt|hurts|got\s+hit|was\s+hit|were\s+hit|took\s+(?:a\s+)?(?:big\s+)?hit|took\s+damage|took\s+\d+\s+damage|was\s+damaged|were\s+damaged|hit\s+(?:me|you|him|her|them|us|[A-Za-z][A-Za-z0-9'_-]*)\s+hard)\b",
                RegexOptions.IgnoreCase);
        }

        private static bool HasDamageEvidence(string claim, string verified, SimMemory memory, WorldSnapshot world)
        {
            if (string.IsNullOrWhiteSpace(verified)) return false;
            string[] clauses = Regex.Split(verified, @"[.!?\r\n]+");
            for (int i = 0; i < clauses.Length; i++)
            {
                string evidence = clauses[i].Trim();
                if (!Regex.IsMatch(evidence, @"\b(?:hurt|damage|damaged|hit|health|hp|low\s+health)\b", RegexOptions.IgnoreCase)) continue;
                if (KnownActorOverlap(claim, evidence, memory, world)) return true;
            }
            return false;
        }

        private static bool KnownActorOverlap(string claim, string evidence, SimMemory memory, WorldSnapshot world)
        {
            if (world != null && world.Player != null && !string.IsNullOrWhiteSpace(world.Player.Name) &&
                claim.IndexOf(world.Player.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                return evidence.IndexOf(world.Player.Name, StringComparison.OrdinalIgnoreCase) >= 0 ||
                       Regex.IsMatch(evidence, @"\b(?:player|you)\b", RegexOptions.IgnoreCase);

            if (world != null && world.Party != null)
            {
                for (int i = 0; i < world.Party.Count; i++)
                {
                    SimSnapshot sim = world.Party[i];
                    if (sim == null || string.IsNullOrWhiteSpace(sim.Name)) continue;
                    if (claim.IndexOf(sim.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                        return evidence.IndexOf(sim.Name, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }

            if (memory != null && !string.IsNullOrWhiteSpace(memory.Name) &&
                Regex.IsMatch(claim, @"\b(?:i|me|my)\b", RegexOptions.IgnoreCase))
                return evidence.IndexOf(memory.Name, StringComparison.OrdinalIgnoreCase) >= 0;

            // A deictic "that hurt" still asserts a current/recent damage event. Permit it only if
            // some verified damage fact exists in the supplied context.
            return !Regex.IsMatch(claim, @"\b(?:[A-Z][A-Za-z0-9'_-]{2,}|i|me|my|you|he|she|they|we|us)\b", RegexOptions.IgnoreCase);
        }

        private static bool IsCombatAssertion(string clause)
        {
            if (!Regex.IsMatch(clause, @"\b(?:fought|fight(?:ing)?|attack(?:ed|ing)?|pull(?:ed|ing)?|raid(?:ed|ing)?)\b", RegexOptions.IgnoreCase)) return false;
            return !Regex.IsMatch(clause, @"^\s*(?:fighting|pulling)\s+(?:is|feels|can\s+be)\b", RegexOptions.IgnoreCase);
        }

        private static bool IsAcquisitionAssertion(string clause)
        {
            if (Regex.IsMatch(clause, @"\bgotcha\b", RegexOptions.IgnoreCase)) return false;
            if (Regex.IsMatch(clause,
                @"^\s*(?:(?:okay|ok|alright|right|yep|yeah)\s*,?\s*)?(?:i(?:'ve|\s+have)?\s+)?got\s+(?:it|you)(?:\s*,?\s*(?:wait(?:\s+here)?|hold|stay|okay|ok|thanks|thank\s+you|sure|coming|right\s+here|one\s+sec(?:ond)?))?\s*$",
                RegexOptions.IgnoreCase)) return false;

            if (Regex.IsMatch(clause, @"\b(?:looted|received|picked\s+up|obtained|got)\b|\bgave\s+(?:me|us|you|him|her|them)\b", RegexOptions.IgnoreCase))
            {
                if (Regex.IsMatch(clause, @"\bgot\s+(?:tired|lost|ready|better|worse|hurt|stuck|here|there)\b", RegexOptions.IgnoreCase)) return false;
                return true;
            }
            if (!Regex.IsMatch(clause, @"\bfound\b", RegexOptions.IgnoreCase)) return false;
            return !Regex.IsMatch(clause, @"\bfound\s+(?:that|this|it)\s+(?:funny|nice|helpful|interesting|annoying|easy|hard)\b", RegexOptions.IgnoreCase);
        }

        private static bool IsGearOwnershipAssertion(string clause)
        {
            string gear = @"(?:gear|equipment|items?|armor|armour|weapons?|sword|axe|mace|staff|wand|bow|dagger|blade|spear|shield|helm|robe|mail|boots|gloves|ring|amulet|cloak|\+\d)";
            return Regex.IsMatch(clause, @"\b(?:i|we|you|they|he|she|[A-Za-z][A-Za-z0-9'_-]*)\b[^.!?]{0,35}\b(?:have|has|had|own(?:s|ed)?|equipp(?:ed|ing)|wear(?:s|ing)?|using)\b[^.!?]{0,35}\b" + gear + @"\b", RegexOptions.IgnoreCase);
        }

        private static bool IsSpellOrHealAssertion(string clause)
        {
            if (!Regex.IsMatch(clause, @"\b(?:heal(?:ed|ing|s)?|cast(?:ing|s)?|buff(?:ed|ing|s)?|assist(?:ed|ing|s)?|saved)\b", RegexOptions.IgnoreCase)) return false;
            // "I'd try healing" and "I might roll healer" are preferences/hypotheticals, not a
            // report that an action happened.  Keep concrete past/present action claims guarded.
            if (Regex.IsMatch(clause, @"\b(?:i(?:'d| would)|we(?:'d| would)|you(?:'d| would)|might|could|probably|rather|want to|wanna|would try|if i rerolled|if we rerolled)\b", RegexOptions.IgnoreCase)) return false;
            return !Regex.IsMatch(clause, @"^\s*(?:healing|casting|buffing|assisting)\s+(?:is|feels|can\s+be)\b", RegexOptions.IgnoreCase);
        }

        private static bool HasAssertionEvidence(string claim, string verified, SimMemory memory, WorldSnapshot world, string supportPattern, bool allowGenericCollective)
        {
            if (string.IsNullOrWhiteSpace(verified)) return false;
            HashSet<string> claimAnchors = AssertionTokens(claim);
            List<int> quantities = AssertionQuantities(claim);
            string[] evidenceClauses = Regex.Split(verified, @"[.!?\r\n]+");
            for (int i = 0; i < evidenceClauses.Length; i++)
            {
                string evidence = evidenceClauses[i].Trim();
                if (evidence.Length == 0 || !Regex.IsMatch(evidence, supportPattern, RegexOptions.IgnoreCase)) continue;
                if (!QuantitiesSupported(quantities, evidence)) continue;
                if (!ActorSupported(claim, evidence, memory, world)) continue;

                HashSet<string> evidenceTokens = AssertionTokens(evidence);
                bool allAnchors = claimAnchors.Count > 0;
                foreach (string token in claimAnchors)
                    if (!evidenceTokens.Contains(token)) { allAnchors = false; break; }
                if (allAnchors) return true;

                if (claimAnchors.Count == 0 && allowGenericCollective &&
                    Regex.IsMatch(claim, @"\b(?:we|party|group|quests?)\b", RegexOptions.IgnoreCase)) return true;
            }
            return false;
        }

        private static bool ActorSupported(string claim, string evidence, SimMemory memory, WorldSnapshot world)
        {
            if (Regex.IsMatch(claim, @"\b(?:he|she|they)\b", RegexOptions.IgnoreCase)) return false;
            if (Regex.IsMatch(claim, @"\bi\b(?!\s+(?:think|guess|feel|hope|bet)\b)", RegexOptions.IgnoreCase))
                return memory != null && !string.IsNullOrWhiteSpace(memory.Name) &&
                       Regex.IsMatch(evidence, @"\b" + Regex.Escape(memory.Name) + @"\b", RegexOptions.IgnoreCase);
            if (Regex.IsMatch(claim, @"\byou\b", RegexOptions.IgnoreCase))
            {
                if (Regex.IsMatch(evidence, @"\b(?:you|player)\b", RegexOptions.IgnoreCase)) return true;
                return world != null && world.Player != null && !string.IsNullOrWhiteSpace(world.Player.Name) &&
                       Regex.IsMatch(evidence, @"\b" + Regex.Escape(world.Player.Name) + @"\b", RegexOptions.IgnoreCase);
            }
            return true;
        }

        private static bool HasActorOnlyLifeEvidence(string claim, string verified, SimMemory memory, WorldSnapshot world)
        {
            bool resolvable = Regex.IsMatch(claim, @"\b(?:you|the\s+player)\b", RegexOptions.IgnoreCase) ||
                (Regex.IsMatch(claim, @"\bi\b", RegexOptions.IgnoreCase) && memory != null && !string.IsNullOrWhiteSpace(memory.Name));
            if (!resolvable || Regex.IsMatch(claim, @"\b(?:he|she|they|we)\b", RegexOptions.IgnoreCase)) return false;
            string[] evidenceClauses = Regex.Split(verified ?? string.Empty, @"[.!?\r\n]+");
            List<int> quantities = AssertionQuantities(claim);
            for (int i = 0; i < evidenceClauses.Length; i++)
            {
                string evidence = evidenceClauses[i];
                if (!Regex.IsMatch(evidence, @"\b(?:died|dead|death|deaths|respawned|revived|defeated)\b", RegexOptions.IgnoreCase)) continue;
                if (QuantitiesSupported(quantities, evidence) && ActorSupported(claim, evidence, memory, world)) return true;
            }
            return false;
        }

        private static HashSet<string> AssertionTokens(string value)
        {
            string normalized = NormalizeNumberWords(value);
            HashSet<string> tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] parts = Regex.Split(normalized.ToLowerInvariant(), @"[^a-z0-9+']+");
            for (int i = 0; i < parts.Length; i++)
            {
                string token = CanonicalAssertionToken(parts[i]);
                if (token.Length < 3 || AssertionStopWords.Contains(token) || Regex.IsMatch(token, @"^\d+$")) continue;
                tokens.Add(token);
            }
            return tokens;
        }

        private static readonly HashSet<string> AssertionStopWords = new HashSet<string>(new string[]
        {
            "the", "and", "that", "this", "these", "those", "with", "from", "into", "after", "before", "during", "since",
            "just", "already", "another", "again", "still", "here", "there", "today", "time", "times", "once", "twice", "thrice",
            "was", "were", "are", "is", "been", "being", "have", "has", "had", "got", "found", "received", "obtained", "picked",
            "loot", "looted", "looting", "drop", "drops", "dropped", "dropping", "gear", "item", "items", "quest", "quests",
            "kill", "killed", "killing", "defeat", "defeated", "slain", "beat", "clear", "cleared", "fight", "fights", "fighting", "fought",
            "attack", "attacked", "attacking", "pull", "pulled", "pulling", "raid", "raided", "raiding", "died", "dead", "death", "deaths", "wiped", "wipe",
            "heal", "healed", "healing", "cast", "casting", "buff", "buffed", "buffing", "assist", "assisted", "assisting", "saved",
            "player", "party", "group", "boss", "enemy", "enemies", "one", "someone", "something", "anything", "itself", "current", "outing",
            "our", "your", "their", "mine", "ours", "yours", "theirs", "you", "they", "she", "him", "her", "them", "who", "what", "where", "when", "which"
        }, StringComparer.OrdinalIgnoreCase);

        private static string CanonicalAssertionToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return string.Empty;
            string value = token.Trim().ToLowerInvariant();
            if (value.EndsWith("ies", StringComparison.Ordinal) && value.Length > 4) return value.Substring(0, value.Length - 3) + "y";
            if (value.EndsWith("s", StringComparison.Ordinal) && !value.EndsWith("ss", StringComparison.Ordinal) && value.Length > 4)
                return value.Substring(0, value.Length - 1);
            return value;
        }

        private static List<int> AssertionQuantities(string value)
        {
            List<int> result = new List<int>();
            string normalized = NormalizeNumberWords(value);
            MatchCollection matches = Regex.Matches(normalized, @"\bx\s*(\d+)\b|\b(\d+)\b", RegexOptions.IgnoreCase);
            for (int i = 0; i < matches.Count; i++)
            {
                int quantity;
                string raw = matches[i].Groups[1].Success ? matches[i].Groups[1].Value : matches[i].Groups[2].Value;
                if (int.TryParse(raw, out quantity) && !result.Contains(quantity)) result.Add(quantity);
            }
            return result;
        }

        private static bool QuantitiesSupported(List<int> quantities, string evidence)
        {
            if (quantities == null || quantities.Count == 0) return true;
            List<int> supported = AssertionQuantities(evidence);
            for (int i = 0; i < quantities.Count; i++) if (!supported.Contains(quantities[i])) return false;
            return true;
        }

        private static string NormalizeNumberWords(string value)
        {
            string result = value ?? string.Empty;
            string[] words = new string[] { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "once", "twice", "thrice" };
            int[] numbers = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 1, 2, 3 };
            for (int i = 0; i < words.Length; i++) result = Regex.Replace(result, @"\b" + words[i] + @"\b", numbers[i].ToString(), RegexOptions.IgnoreCase);
            return result;
        }

        private static bool HasEntitySpecificLifeDeathSupport(string reply, string verified, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(reply)) return true;

            Match namedDeath = Regex.Match(reply, @"\b([A-Z][A-Za-z0-9'_-]{2,}(?:\s+[A-Z][A-Za-z0-9'_-]{2,}){0,2})\s+(?:is|was|just\s+)?(?:dead|died|defeated|slain)\b", RegexOptions.CultureInvariant);
            if (namedDeath.Success)
            {
                string name = namedDeath.Groups[1].Value.Trim();
                if (!VerifiedEntityEvent(verified, name, @"\b(?:dead|died|death|defeated|slain|killed)\b"))
                {
                    reason = "unsupported named death/defeat claim about " + name;
                    return false;
                }
            }

            Match killedEntity = Regex.Match(reply, @"\b(?:we|i|you|they|he|she)\s+(?:just\s+)?(?:killed|defeated|beat)\s+([A-Z][A-Za-z0-9'_-]{2,}(?:\s+[A-Z][A-Za-z0-9'_-]{2,}){0,3})\b", RegexOptions.CultureInvariant);
            if (killedEntity.Success)
            {
                string name = killedEntity.Groups[1].Value.Trim();
                if (!VerifiedEntityEvent(verified, name, @"\b(?:killed|defeated|slain|beat)\b"))
                {
                    reason = "unsupported named kill/defeat claim about " + name;
                    return false;
                }
            }
            return true;
        }

        private static bool VerifiedEntityEvent(string verified, string entity, string eventPattern)
        {
            if (string.IsNullOrWhiteSpace(verified) || string.IsNullOrWhiteSpace(entity)) return false;
            string escaped = Regex.Escape(entity);
            if (Regex.IsMatch(verified, escaped + @"[^.!?\r\n]{0,90}" + eventPattern, RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(verified, eventPattern + @"[^.!?\r\n]{0,90}" + escaped, RegexOptions.IgnoreCase)) return true;
            return false;
        }

        private static bool HasKnowledgeRelationSupport(string reply, WikiResult facts)
        {
            if (facts == null || !facts.Found || string.IsNullOrWhiteSpace(facts.Extract)) return true;
            if (HasUncertaintyLanguage(reply)) return true;

            string sourceText = facts.Extract + " " + (facts.Title ?? string.Empty);
            string[] answerClauses = Regex.Split(reply, @"[.!?\r\n]+");
            // External-news extracts concatenate independent headlines with " | " (see
            // ExternalNewsClient.BuildBundle), not sentence punctuation, so a plain sentence split would
            // treat two unrelated headlines as one blob and let a relation anchor from headline A pair
            // with an entity that only appears in headline B (the NASA eclipse/fire-storm bug). Split on
            // the headline delimiter first so each retrieved item is validated as its own isolated unit;
            // ordinary wiki/lore extracts have no " | " delimiter and fall back to sentence splitting.
            string[] sourceItems = facts.Extract.IndexOf(" | ", StringComparison.Ordinal) >= 0
                ? facts.Extract.Split(new string[] { " | " }, StringSplitOptions.RemoveEmptyEntries)
                : new string[] { facts.Extract };
            List<string> sourceClauseList = new List<string>();
            for (int i = 0; i < sourceItems.Length; i++)
            {
                string[] pieces = Regex.Split(sourceItems[i], @"[.!?\r\n]+");
                for (int p = 0; p < pieces.Length; p++) sourceClauseList.Add(pieces[p]);
            }
            string[] sourceClauses = sourceClauseList.ToArray();
            string[] relationPatterns = new string[]
            {
                @"\b(?:sold|sells|buy|bought|purchase(?:d)?|vendor|merchant)\b",
                @"\b(?:located|location|spawns?|appears?|available|found)\b[^.!?]{0,55}\b(?:in|at|near|inside)\b|\bis\s+(?:in|at|near|inside)\b",
                @"\b(?:requires?|requirements?|needs?|must|only|minimum|level|class)\b",
                @"\b(?:drop(?:s|ped|ping)?|comes?\s+from|source|drop\s+rate)\b",
                @"\b(?:craft(?:ed|ing|s)?|forge(?:d|s)?|combine(?:d|s)?|turn\s+in|use(?:d)?\s+on)\b",
                @"\b(?:gives?|given|rewards?|rewarded|provides?|teaches?|taught)\b",
                @"\b(?:costs?|priced?|price)\b",
                @"\b(?:leads?|connects?|travels?|route)\b",
                @"\b(?:is|are|has|have)\b"
            };

            bool sawContent = false;
            for (int i = 0; i < answerClauses.Length; i++)
            {
                string answerClause = answerClauses[i].Trim();
                if (answerClause.Length == 0) continue;
                sawContent = true;
                string relation = string.Empty;
                for (int r = 0; r < relationPatterns.Length; r++)
                    if (Regex.IsMatch(answerClause, relationPatterns[r], RegexOptions.IgnoreCase)) { relation = relationPatterns[r]; break; }

                if (relation.Length == 0)
                {
                    if (KnowledgeCommonTokenCount(answerClause, sourceText) < 2) return false;
                    continue;
                }

                HashSet<string> anchors = KnowledgeRelationTokens(answerClause);
                if (anchors.Count == 0) return false;
                List<int> quantities = AssertionQuantities(answerClause);
                bool supported = false;
                for (int s = 0; s < sourceClauses.Length; s++)
                {
                    string sourceClause = sourceClauses[s].Trim();
                    if (sourceClause.Length == 0 || !Regex.IsMatch(sourceClause, relation, RegexOptions.IgnoreCase)) continue;
                    if (!QuantitiesSupported(quantities, sourceClause)) continue;
                    // Only fold the combined Title in for a single-item source (e.g. one wiki page,
                    // where Title is just that page's name). With multiple retrieved news headlines the
                    // combined Title carries every headline's subject at once and would let a relation
                    // anchor from one headline pair with an entity that only appears in another headline.
                    string titleContext = sourceItems.Length <= 1 ? (facts.Title ?? string.Empty) : string.Empty;
                    HashSet<string> sourceTokens = KnowledgeRelationTokens(sourceClause + " " + titleContext);
                    bool all = true;
                    foreach (string anchor in anchors) if (!sourceTokens.Contains(anchor)) { all = false; break; }
                    if (all) { supported = true; break; }
                }
                if (!supported) return false;
            }
            return sawContent;
        }

        private static int KnowledgeCommonTokenCount(string answer, string source)
        {
            HashSet<string> sourceTokens = KnowledgeTokens(source);
            HashSet<string> answerTokens = KnowledgeTokens(answer);
            int common = 0;
            foreach (string token in answerTokens) if (sourceTokens.Contains(token)) common++;
            return common;
        }

        private static HashSet<string> KnowledgeRelationTokens(string value)
        {
            HashSet<string> result = KnowledgeTokens(value);
            string[] relationWords = new string[]
            {
                "sold", "sells", "buy", "bought", "purchase", "purchased", "vendor", "merchant",
                "located", "location", "spawns", "spawn", "appears", "available", "found", "inside", "near",
                "requires", "require", "requirements", "needs", "need", "must", "only", "minimum", "level", "class",
                "drop", "drops", "dropped", "dropping", "comes", "source", "rate", "crafted", "crafting", "crafts",
                "forge", "forged", "combines", "combined", "combine", "turn", "used", "use", "gives", "give", "given",
                "rewards", "reward", "rewarded", "provides", "provide", "teaches", "teach", "taught", "costs", "cost", "priced",
                "price", "leads", "lead", "connects", "connect", "travels", "travel", "route", "is", "are", "has", "have"
            };
            for (int i = 0; i < relationWords.Length; i++) result.Remove(relationWords[i]);
            return result;
        }

        private static HashSet<string> KnowledgeTokens(string value)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(value)) return result;
            string[] parts = Regex.Split(value.ToLowerInvariant(), @"[^a-z0-9+']+");
            string[] stop = new string[] { "the", "and", "for", "with", "that", "this", "from", "your", "you", "are", "can", "get", "how", "what", "where", "into", "have", "has", "was", "were", "not", "just", "item", "items" };
            HashSet<string> stops = new HashSet<string>(stop, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < parts.Length; i++)
            {
                string token = parts[i].Trim();
                if (token.Length < 3 || stops.Contains(token)) continue;
                result.Add(token);
            }
            return result;
        }

        private static string SharedKnownGuild(WorldSnapshot world)
        {
            if (world == null || world.Party == null || world.Party.Count == 0) return string.Empty;
            string guild = string.Empty;
            for (int i = 0; i < world.Party.Count; i++)
            {
                SimSnapshot sim = world.Party[i];
                if (sim == null || string.IsNullOrWhiteSpace(sim.GuildName)) continue;
                if (string.IsNullOrWhiteSpace(guild)) guild = sim.GuildName.Trim();
                else if (!string.Equals(guild, sim.GuildName.Trim(), StringComparison.OrdinalIgnoreCase)) return string.Empty;
            }
            return guild;
        }

        private static bool TryFindSelfClassContradiction(string reply, SimMemory memory, WorldSnapshot world, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(reply) || memory == null || string.IsNullOrWhiteSpace(memory.Name) || world == null || world.Party == null) return false;
            string verifiedClass = string.Empty;
            for (int i = 0; i < world.Party.Count; i++)
            {
                SimSnapshot member = world.Party[i];
                if (member == null || string.IsNullOrWhiteSpace(member.Name) || string.IsNullOrWhiteSpace(member.ClassName)) continue;
                if (string.Equals(member.Name, memory.Name, StringComparison.OrdinalIgnoreCase))
                {
                    verifiedClass = member.ClassName.Trim();
                    break;
                }
            }
            if (verifiedClass.Length == 0) return false;

            Match claim = Regex.Match(reply,
                @"\b(?:(?:i(?:'m|\s+am)\s+(?:just\s+)?(?:a|an|the)?\s*)|(?:as\s+(?:a|an|the)\s+))(?<class>arcanist|druid|paladin|reaver|stormcaller|windblade|duelist)\b",
                RegexOptions.IgnoreCase);
            if (!claim.Success)
                claim = Regex.Match(reply,
                    @"\b(?<class>arcanist|druid|paladin|reaver|stormcaller|windblade|duelist)\b[^.!?]{0,30}\b(?:like|including)\s+me\b",
                    RegexOptions.IgnoreCase);
            if (!claim.Success) return false;
            string claimedClass = claim.Groups["class"].Value;
            if (string.Equals(claimedClass, verifiedClass, StringComparison.OrdinalIgnoreCase)) return false;

            // A hypothetical/preference answer ("if I weren't a Paladin I'd probably be a Druid") is not
            // a factual self-identity claim and needs no VERIFIED provenance; only flag a real, present-
            // tense identity claim as a contradiction.
            int contextStart = Math.Max(0, claim.Index - 40);
            string context = reply.Substring(contextStart, claim.Index - contextStart);
            if (Regex.IsMatch(context, @"\b(?:if\s+i|i'd|i\s+would|would\s+probably|probably|maybe\s+i'd|wish\s+i|honestly\s+if)\b", RegexOptions.IgnoreCase))
                return false;

            reason = "contradicts verified self class identity for " + memory.Name;
            return true;
        }

        private static bool TryFindSelfGuildClaimViolation(string reply, SimMemory memory, WorldSnapshot world, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(reply) || memory == null || string.IsNullOrWhiteSpace(memory.Name) || world == null || world.Party == null) return false;

            SimSnapshot speaker = null;
            for (int i = 0; i < world.Party.Count; i++)
            {
                SimSnapshot member = world.Party[i];
                if (member != null && string.Equals(member.Name, memory.Name, StringComparison.OrdinalIgnoreCase)) { speaker = member; break; }
            }
            if (speaker == null) return false;

            string liveGuild = string.IsNullOrWhiteSpace(speaker.GuildName) ? string.Empty : speaker.GuildName.Trim();
            bool claimsAnyGuild = Regex.IsMatch(reply,
                @"\b(?:i(?:'m|\s+am)\s+(?:currently\s+)?in\s+(?:a|the)\s+guild|my\s+guild\s+is|i\s+belong\s+to\s+(?:a|the)\s+guild)\b",
                RegexOptions.IgnoreCase);

            Match named = Regex.Match(reply,
                @"\b(?:my\s+guild\s+is|i(?:'m|\s+am)\s+in(?:\s+the)?|i\s+belong\s+to(?:\s+the)?)\s+(?<guild>[A-Za-z0-9'_-]+(?:\s+[A-Za-z0-9'_-]+){0,3})\s+guild\b",
                RegexOptions.IgnoreCase);
            if (!named.Success)
            {
                // "I'm with Dragon Food" is normal speech, but only treat it as a guild-membership
                // assertion when it looks like a named organization rather than "I'm with you".
                named = Regex.Match(reply,
                    @"\b(?:I(?:'m|\s+am)\s+with)\s+(?:the\s+)?(?<guild>[A-Z][A-Za-z0-9'_-]*(?:\s+[A-Z][A-Za-z0-9'_-]*){0,3})\b");
            }

            if (!claimsAnyGuild && !named.Success) return false;
            if (liveGuild.Length == 0)
            {
                reason = "unsupported self guild-membership claim";
                return true;
            }
            if (named.Success && !string.Equals(Normalize(named.Groups["guild"].Value), Normalize(liveGuild), StringComparison.OrdinalIgnoreCase))
            {
                reason = "contradicts verified self guild membership";
                return true;
            }
            return false;
        }

        private static bool HasUnsupportedSocialAuthorityClaim(string reply, string verified, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(reply)) return false;

            string capability = string.Empty;
            string actor = @"(?:i\s+can|i(?:'ll|\s+will)|let\s+me)";
            if (Regex.IsMatch(reply, @"\b" + actor + @"\s+(?:get\s+(?:you|him|her|them)\s+into|recruit\s+(?:you|him|her|them)|invite\s+(?:you|him|her|them)\s+(?:into|to)\s+(?:the\s+|my\s+|our\s+|a\s+)?guild)\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(reply, @"\b" + actor + @"\s+invite\s+(?:you|him|her|them)\s*[.!?]?$", RegexOptions.IgnoreCase))
                capability = "guild_invite";
            else if (Regex.IsMatch(reply, @"\b" + actor + @"\s+promote\s+(?:you|him|her|them)\b", RegexOptions.IgnoreCase))
                capability = "guild_promote";
            else if (Regex.IsMatch(reply, @"\b" + actor + @"\s+kick\s+(?:you|him|her|them)\s+(?:from|out\s+of)\b", RegexOptions.IgnoreCase))
                capability = "guild_kick";
            else if (LooksLikeTransferPromise(reply, actor, "give"))
                capability = "item_give";
            else if (LooksLikeTransferPromise(reply, actor, "get"))
                capability = "item_acquire";
            else if (Regex.IsMatch(reply, @"\b" + actor + @"\s+teach\s+(?:you|him|her|them)\b[^.!?]{0,28}\b(?:spell|skill|ability|recipe|technique)\b", RegexOptions.IgnoreCase) ||
                     Regex.IsMatch(reply, @"\b" + actor + @"\s+(?:unlock|grant)\b[^.!?]{0,32}\b(?:access|quest|spell|skill|ability|recipe|reward|rank|title)\b", RegexOptions.IgnoreCase))
                capability = "grant_or_teach";

            if (capability.Length == 0) return false;
            if (HasVerifiedCapabilityMarker(verified, capability)) return false;

            reason = "unsupported social authority/capability claim: " + capability;
            return true;
        }

        private static bool LooksLikeTransferPromise(string reply, string actorPattern, string verb)
        {
            Match transfer = Regex.Match(reply,
                @"\b" + actorPattern + @"\s+" + verb + @"\s+(?:you|him|her|them)\s+(?:an?|the|some|that|this|another)\s+(?<thing>[A-Za-z][A-Za-z'-]*)\b",
                RegexOptions.IgnoreCase);
            if (!transfer.Success) return false;
            string thing = transfer.Groups["thing"].Value.ToLowerInvariant();
            // Common harmless social idioms are not item/capability promises.
            return thing != "hand" && thing != "chance" && thing != "time" && thing != "moment" &&
                   thing != "space" && thing != "advice" && thing != "answer" && thing != "look" &&
                   thing != "warning" && thing != "idea" && thing != "break";
        }

        // The current game adapters do not expose a verified guild-rank/recruitment authority surface.
        // This marker seam lets a future native/current capability reader opt a claim in explicitly
        // without ever treating guild membership, authored persona, or generated prose as authority.
        private static bool HasVerifiedCapabilityMarker(string verified, string capability)
        {
            if (string.IsNullOrWhiteSpace(verified) || string.IsNullOrWhiteSpace(capability)) return false;
            return Regex.IsMatch(verified,
                @"(?:\[\s*capability\s*:\s*" + Regex.Escape(capability) + @"\s*\]|\bverified\s+capability\s+" + Regex.Escape(capability) + @"\b)",
                RegexOptions.IgnoreCase);
        }

        private static bool TryFindIdentityContradiction(string reply, WorldSnapshot world, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(reply) || world == null || world.Party == null) return false;
            for (int i = 0; i < world.Party.Count; i++)
            {
                SimSnapshot member = world.Party[i];
                if (member == null || string.IsNullOrWhiteSpace(member.Name)) continue;
                string name = Regex.Escape(member.Name.Trim());
                if (!string.IsNullOrWhiteSpace(member.ClassName))
                {
                    if (!ClassCanTank(member.ClassName) && Regex.IsMatch(reply, @"\b" + name + @"\b[^.!?]{0,45}\b(?:plate\s+)?tank\b", RegexOptions.IgnoreCase) &&
                        !Regex.IsMatch(reply, @"\b" + name + @"\b[^.!?]{0,25}\bnot\s+(?:a\s+)?(?:plate\s+)?tank\b", RegexOptions.IgnoreCase))
                    {
                        reason = "contradicts verified role capability for " + member.Name;
                        return true;
                    }
                    if (!ClassCanHeal(member.ClassName) && Regex.IsMatch(reply, @"\b" + name + @"\b[^.!?]{0,45}\bhealer\b", RegexOptions.IgnoreCase) &&
                        !Regex.IsMatch(reply, @"\b" + name + @"\b[^.!?]{0,25}\bnot\s+(?:a\s+)?healer\b", RegexOptions.IgnoreCase))
                    {
                        reason = "contradicts verified role capability for " + member.Name;
                        return true;
                    }
                }

                if (member.RoleAssignmentsKnown)
                {
                    string[] exactRoles = new string[] { "Main Tank", "Main Assist", "Puller", "Crowd Control", "Healing/Mana" };
                    string[] patterns = new string[] { "main\\s+tank", "main\\s+assist", "puller", "crowd\\s+control", "(?:healing\\s*/\\s*mana|healing\\s+role)" };
                    for (int r = 0; r < exactRoles.Length; r++)
                    {
                        if (!Regex.IsMatch(reply, @"\b" + name + @"\b[^.!?]{0,50}\b(?:is|as|assigned|our)\b[^.!?]{0,20}\b" + patterns[r] + @"\b|\b(?:our|the|assigned)\s+" + patterns[r] + @"\b[^.!?]{0,35}\b" + name + @"\b", RegexOptions.IgnoreCase)) continue;
                        if (HasAssignedRole(member, exactRoles[r])) continue;
                        reason = "contradicts verified Manage Roles assignment for " + member.Name;
                        return true;
                    }
                }

                if (string.IsNullOrWhiteSpace(member.GuildName)) continue;
                Match guildClaim = Regex.Match(reply, @"\b" + name + @"\b\s+(?:is|was)\s+(?:in|from)\s+(?:the\s+)?([A-Za-z0-9'_-]+(?:\s+[A-Za-z0-9'_-]+){0,3})\s+guild\b", RegexOptions.IgnoreCase);
                if (!guildClaim.Success)
                    guildClaim = Regex.Match(reply, @"\b" + name + @"(?:'s|\s+is)\s+guild\s+is\s+(?:the\s+)?([A-Za-z0-9'_-]+(?:\s+[A-Za-z0-9'_-]+){0,3})\b", RegexOptions.IgnoreCase);
                if (guildClaim.Success && !string.Equals(Normalize(guildClaim.Groups[1].Value), Normalize(member.GuildName), StringComparison.OrdinalIgnoreCase))
                {
                    reason = "contradicts verified guild identity for " + member.Name;
                    return true;
                }
            }
            return false;
        }

        private static bool HasAssignedRole(SimSnapshot sim, string role)
        {
            if (sim == null || sim.AssignedRoles == null) return false;
            for (int i = 0; i < sim.AssignedRoles.Count; i++)
                if (string.Equals(sim.AssignedRoles[i], role, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool HasUnsupportedCollectivePlan(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply)) return false;
            if (Regex.IsMatch(reply, @"\b(?:let'?s|we\s+should|we(?:'re|\s+are)\s+going\s+to|we(?:'ll|\s+will))\s+(?:stay|chill|stop|quit|keep\s+moving|head|go|hunt|farm|leave|call\s+it)\b", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(reply, @"\b(?:we(?:'re|\s+are)\s+done\s+for\s+today|not\s+worry\s+about\s+anything\s+else\s+today|instead\s+of\s+hunting|stop\s+hunting|quit\s+hunting)\b", RegexOptions.IgnoreCase)) return true;
            return false;
        }

        // Plausibility is not verification. The real-packet labs found the model readily offers a
        // specific, confident-sounding cause for something it never actually observed - "they probably
        // respawned", "another squad must have aggroed them", "they disconnected" - for an unexplained
        // event like a party vanishing. A plain reaction ("yeah, that was weird") remains fully allowed;
        // only a stated CAUSE is gated, and only when the verified corpus does not already establish it.
        private static bool HasUnsupportedCausalGuess(string reply, string verified)
        {
            if (string.IsNullOrWhiteSpace(reply)) return false;
            Match causal = Regex.Match(reply,
                @"\b(?:probably|must\s+(?:have|'ve)|maybe\s+(?:they|it)|i\s+bet(?:\s+they)?)\s+(?:respawned|disconnected|d\/?c'?d|logged\s+off|aggro(?:ed)?|got\s+(?:aggro|pulled|wiped|disconnected)|rage\s*quit|crashed|lagged\s+out)\b|" +
                @"\bthey\s+(?:must\s+have|probably)\s+[a-z]+ed\b|" +
                @"\b(?:another\s+(?:squad|group|party)\s+(?:must\s+have|probably)\s+aggroed\s+them)\b",
                RegexOptions.IgnoreCase);
            if (!causal.Success) return false;
            return string.IsNullOrWhiteSpace(verified) || verified.IndexOf(causal.Value, StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool HasUnsupportedRelationshipClaim(string reply, SimMemory memory, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(reply)) return false;

            if (Regex.IsMatch(reply,
                @"\b(?:best|close|oldest|lifelong)\s+friends?\b|\bfriends?\s+forever\b|\b(?:we(?:'re|\s+are)|you(?:'re|\s+are))\s+(?:my\s+)?friends?\b|\blike\s+family\b|\bknown\s+(?:you|each\s+other)\s+forever\b|\bgo\s+way\s+back\b|\bsince\s+we\s+(?:first\s+)?met\b",
                RegexOptions.IgnoreCase))
            {
                reason = "relationship tone is not evidence of friendship or lifelong history";
                return true;
            }

            if (Regex.IsMatch(reply, @"\b(?:always\s+been\s+rivals?|rivals?\s+since|our\s+rivalry\s+(?:goes|dates)\s+back)\b", RegexOptions.IgnoreCase))
            {
                reason = "rivalry tone is not evidence of a rivalry history";
                return true;
            }

            bool priorDuelClaim = Regex.IsMatch(reply,
                @"\b(?:duel(?:ed|led)?|sparred|fought\s+each\s+other)\b[^.!?]{0,35}\b(?:before|again|last\s+time)\b|\b(?:our|that)\s+(?:last|previous)\s+(?:practice\s+)?duel\b",
                RegexOptions.IgnoreCase);
            if (!priorDuelClaim) return false;
            string historical = BuildHistoricalCorpus(memory);
            if (Regex.IsMatch(historical, @"\b(?:friendly|practice)\s+duel\b", RegexOptions.IgnoreCase)) return false;
            reason = "rivalry does not prove a previous practice duel";
            return true;
        }

        private static bool ClassCanTank(string className)
        {
            return string.Equals(className, "Paladin", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(className, "Reaver", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ClassCanHeal(string className)
        {
            return string.Equals(className, "Druid", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddSelfTestResult(List<string> results, string name, string reply, bool expectedGrounded, SimMemory memory, WorldSnapshot world)
        {
            string reason;
            bool grounded = IsGrounded(reply, memory, world, string.Empty, out reason);
            results.Add("[DeepSims Guard] " + name + ": " + (grounded == expectedGrounded ? "PASS" : "FAIL") +
                " (" + (string.IsNullOrWhiteSpace(reason) ? "accepted" : reason) + ")");
        }

        private static void AddKnowledgeSelfTestResult(List<string> results, string name, string reply, bool expectedGrounded, SimMemory memory, WorldSnapshot world, WikiResult facts)
        {
            string reason;
            bool grounded = IsKnowledgeModeGrounded(reply, memory, world, facts, out reason);
            results.Add("[DeepSims Guard] " + name + ": " + (grounded == expectedGrounded ? "PASS" : "FAIL") +
                " (" + (string.IsNullOrWhiteSpace(reason) ? "accepted" : reason) + ")");
        }

        private static bool LooksLikeHistoricalExperienceClaim(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply)) return false;
            if (Regex.IsMatch(reply, @"\b(?:last\s+time|remember\s+when|used\s+to|previously|same\s+as\s+before|back\s+here|back\s+there|as\s+usual|like\s+usual|just\s+like\s+usual|same\s+old|like\s+before)\b", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(reply, @"\b(?:got|found|looted|killed|cleared|finished|visited|fought|died|wiped|happened)\b[^.!?]{0,45}\bbefore\b", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(reply, @"\b(?:got|found|looted|killed|cleared|picked\s+up)\b[^.!?]{0,35}\banother\s+one\b", RegexOptions.IgnoreCase)) return true;
            // Only a personal pronoun subject ("you always...", "he always...") asserts a real prior
            // pattern about someone specific. A general-noun subject ("tanks always get blamed") is an
            // obvious MMO generalization/joke, not a claim of shared history, and must stay allowed.
            if (Regex.IsMatch(reply, @"\b(?:you|we|they|he|she)\s+always\b", RegexOptions.IgnoreCase)) return true;
            string experiential = @"(?:bit|bite|bites|killed|died|got|found|went|been|back|fought|dueled|duelled|sparred|farmed|ran|visited|saw|did|happened|happen|hurt|saved|healed|dropped|try|tried|trying)";
            return Regex.IsMatch(reply, @"\b" + experiential + @"\b[^.!?]{0,55}\bagain\b|\bagain\b[^.!?]{0,55}\b" + experiential + @"\b", RegexOptions.IgnoreCase);
        }

        private static bool HasUnsupportedNamedCombatHistory(string reply, string verified, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(reply)) return false;

            Match temporal = Regex.Match(reply,
                @"\b(?:after|before|during|since|from)\s+(?:that|the|our|a|an)?\s*(?:fight|battle|encounter|run|trip)\b(?<tail>[^.!?]{0,90})",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!temporal.Success) return false;

            if (string.IsNullOrWhiteSpace(verified) ||
                !Regex.IsMatch(verified, @"\b(?:fight|battle|encounter|combat)\b", RegexOptions.IgnoreCase))
            {
                reason = "unsupported historical combat reference";
                return true;
            }

            Match named = Regex.Match(temporal.Groups["tail"].Value,
                @"\b(?:with|against)\s+(?<names>[A-Z][A-Za-z0-9'_-]*(?:\s+(?:and|the)\s+[A-Z][A-Za-z0-9'_-]*){0,3})",
                RegexOptions.CultureInvariant);
            if (!named.Success) return false;

            string[] names = Regex.Split(named.Groups["names"].Value, @"\s+(?:and|the)\s+|\s+");
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i].Trim();
                if (name.Length < 3 || string.Equals(name, "the", StringComparison.OrdinalIgnoreCase)) continue;
                if (verified.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    reason = "unsupported named combat history for " + name;
                    return true;
                }
            }
            return false;
        }

        private static string BuildHistoricalCorpus(SimMemory memory)
        {
            if (memory == null) return string.Empty;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            AppendAuthoritativeIdentityAndStructuredMemory(sb, memory, false);
            if (memory.ImportantMemories != null)
                for (int i = 0; i < memory.ImportantMemories.Count; i++) sb.AppendLine(memory.ImportantMemories[i] ?? string.Empty);
            if (memory.OutingSummaries != null)
                for (int i = 0; i < memory.OutingSummaries.Count; i++) sb.AppendLine(VerifiedOutingHistoryPolicy.SanitizeForPrompt(memory.OutingSummaries[i]));
            if (memory.RecentEvents != null)
                for (int i = 0; i < memory.RecentEvents.Count; i++)
                {
                    MemoryEvent evt = memory.RecentEvents[i];
                    if (evt == null) continue;
                    if (string.Equals(evt.type, "deep_group_chat", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(evt.type, "conversation", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(evt.type, "group_join", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(evt.type, "group_leave", StringComparison.OrdinalIgnoreCase)) continue;
                    sb.AppendLine(evt.text ?? string.Empty);
                }
            return sb.ToString();
        }

        private static void AppendAuthoritativeIdentityAndStructuredMemory(System.Text.StringBuilder sb, SimMemory memory, bool includeCorePersonality)
        {
            if (sb == null || memory == null) return;
            IdentitySchema.Normalize(memory);
            AuthoredIdentityProfile a = memory.AuthoredIdentity;
            if (a != null)
            {
                if (includeCorePersonality && !string.IsNullOrWhiteSpace(a.CorePersonality)) sb.AppendLine(a.CorePersonality);
                if (!string.IsNullOrWhiteSpace(a.PersonalBackground)) sb.AppendLine(a.PersonalBackground);
                if (!string.IsNullOrWhiteSpace(a.ErenshorPersona)) sb.AppendLine(a.ErenshorPersona);
                if (!string.IsNullOrWhiteSpace(a.RelationshipToPlayer)) sb.AppendLine(a.RelationshipToPlayer);
                AppendOwnedRecords(sb, memory, a.SharedHistory);
                AppendOwnedRecords(sb, memory, a.PinnedMemories);
            }
            AppendOwnedRecords(sb, memory, memory.StructuredMemories);
        }

        private static void AppendOwnedRecords(System.Text.StringBuilder sb, SimMemory memory, List<StructuredMemoryRecord> records)
        {
            if (records == null) return;
            SimSnapshot owner = new SimSnapshot { Key = memory.SimKey, Name = memory.Name };
            for (int i = 0; i < records.Count; i++)
            {
                StructuredMemoryRecord record = records[i];
                if (record != null && MemoryKnowledgePolicy.CanUse(record, owner)) sb.AppendLine(record.Text ?? string.Empty);
            }
        }

        private static bool HasHistoricalAnchorOverlap(string reply, string historical)
        {
            if (string.IsNullOrWhiteSpace(historical)) return false;
            if (Regex.IsMatch(reply, @"\b(?:duel(?:ed|led)?|sparred)\b", RegexOptions.IgnoreCase) &&
                Regex.IsMatch(historical, @"\b(?:friendly|practice)\s+duel\b", RegexOptions.IgnoreCase)) return true;
            HashSet<string> historyTokens = TokenSet(Normalize(historical));
            if (historyTokens.Count == 0) return false;
            HashSet<string> replyTokens = TokenSet(Normalize(reply));
            string[] generic = new string[] { "again", "last", "time", "remember", "when", "used", "previously", "really", "just", "think", "hope", "party", "group", "today", "here", "there", "good", "bad", "doing", "going", "want", "need", "with", "have", "been", "back", "usual", "like" };
            HashSet<string> ignore = new HashSet<string>(generic, StringComparer.OrdinalIgnoreCase);
            foreach (string token in replyTokens)
            {
                if (token.Length < 4 || ignore.Contains(token)) continue;
                if (historyTokens.Contains(token)) return true;
            }
            return false;
        }

        private static string BuildVerifiedCorpus(SimMemory memory, WorldSnapshot world, string situation)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(situation)) sb.AppendLine(situation);
            if (world != null)
            {
                if (!string.IsNullOrWhiteSpace(world.Scene)) sb.AppendLine(world.Scene);
                if (world.Party != null)
                {
                    for (int i = 0; i < world.Party.Count; i++)
                    {
                        SimSnapshot sim = world.Party[i];
                        if (sim == null) continue;
                        sb.AppendLine((sim.Name ?? string.Empty) + " " + (sim.ClassName ?? string.Empty) + " level " + sim.Level +
                            (string.IsNullOrWhiteSpace(sim.GuildName) ? string.Empty : " guild " + sim.GuildName) +
                            (string.IsNullOrWhiteSpace(sim.CombatRole) ? string.Empty : " class role " + sim.CombatRole) +
                            (string.IsNullOrWhiteSpace(sim.CurrentAction) ? string.Empty : " currently " + sim.CurrentAction));
                    }
                }
                if (world.Outing != null)
                {
                    if (!string.IsNullOrWhiteSpace(world.Outing.Activity)) sb.AppendLine("activity " + world.Outing.Activity);
                    if (!string.IsNullOrWhiteSpace(world.Outing.CurrentCombatTarget)) sb.AppendLine("current combat target " + world.Outing.CurrentCombatTarget);
                    if (world.Outing.Facts != null)
                    {
                        for (int i = 0; i < world.Outing.Facts.Count; i++) sb.AppendLine(world.Outing.Facts[i] ?? string.Empty);
                    }
                }
            }
            if (memory != null)
            {
                AppendAuthoritativeIdentityAndStructuredMemory(sb, memory, true);
                if (memory.ImportantMemories != null)
                    for (int i = 0; i < memory.ImportantMemories.Count; i++) sb.AppendLine(memory.ImportantMemories[i] ?? string.Empty);
                if (memory.OutingSummaries != null)
                    for (int i = 0; i < memory.OutingSummaries.Count; i++) sb.AppendLine(VerifiedOutingHistoryPolicy.SanitizeForPrompt(memory.OutingSummaries[i]));
                if (memory.RecentEvents != null)
                    for (int i = 0; i < memory.RecentEvents.Count; i++)
                    {
                        MemoryEvent evt = memory.RecentEvents[i];
                        if (evt == null) continue;
                        if (string.Equals(evt.type, "deep_group_chat", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(evt.type, "conversation", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(evt.type, "group_join", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(evt.type, "group_leave", StringComparison.OrdinalIgnoreCase)) continue;
                        sb.AppendLine(evt.text ?? string.Empty);
                    }
            }
            return sb.ToString();
        }

        private static bool HasSpeakerNarrationLeak(string reply, WorldSnapshot world)
        {
            if (world == null || world.Party == null) return false;
            for (int i = 0; i < world.Party.Count; i++)
            {
                SimSnapshot sim = world.Party[i];
                if (sim == null || string.IsNullOrWhiteSpace(sim.Name)) continue;
                string pattern = @"\b" + Regex.Escape(sim.Name) + @"\s+(?:says|said|tells|asks|replies)\b";
                if (Regex.IsMatch(reply, pattern, RegexOptions.IgnoreCase)) return true;
            }
            return false;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string text = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9\s]", " ");
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return text;
        }

        private static HashSet<string> TokenSet(string value)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] parts = value.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.Length <= 2) continue;
                set.Add(p);
            }
            return set;
        }
    }

    // Small, bounded guard against a model returning a truncated fragment ("I'm just a Windblade
    // trying out the") verbatim. This is not a general grammar checker: it only looks for a handful of
    // clearly-incomplete endings so short valid MMO fragments ("nah", "probably healer", "not a chance
    // lol") are never flagged.
    internal static class ReplyCompletenessGuard
    {
        private static readonly string[] TrailingArticles = { "a", "an", "the" };
        private static readonly string[] TrailingConjunctions = { "and", "but", "or", "because", "so", "nor", "yet" };
        private static readonly string[] TrailingPrepositions = { "to", "of", "in", "on", "at", "for", "with", "about", "from", "into", "onto" };

        internal static bool IsIncomplete(string reply, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(reply)) return false;
            string text = reply.Trim();

            if (HasUnmatchedOpener(text)) { reason = "incomplete_unmatched_opener"; return true; }

            if (Regex.IsMatch(text, @"\b(?:trying|going|about|starting|planning|hoping|about)\s+to\s*$", RegexOptions.IgnoreCase))
            { reason = "incomplete_unfinished_clause"; return true; }

            if (Regex.IsMatch(text, @"\?\s*(?:you|we|they|he|she)\s*$", RegexOptions.IgnoreCase))
            { reason = "incomplete_dangling_question_subject"; return true; }

            if (Regex.IsMatch(text, @"\b(?:and|but|or|because|so)\.\.\.\s*$", RegexOptions.IgnoreCase))
            { reason = "incomplete_truncated_conjunction"; return true; }

            string trimmedEnd = text.TrimEnd('.', '!', '?', ' ', ',');
            if (trimmedEnd.Length == 0) return false;
            string[] words = Regex.Split(trimmedEnd, @"\s+");
            if (words.Length == 0) return false;
            string last = words[words.Length - 1].ToLowerInvariant().Trim('\'', '"');
            if (last.Length == 0) return false;

            // A sentence-final period/!/? after the last word is a strong signal the model considered
            // the thought finished; a bare trailing article/conjunction/preposition without one is far
            // more likely a genuine cutoff, so only the no-terminal-punctuation case is flagged here.
            bool hasTerminalPunctuation = text.Length > 0 && (text[text.Length - 1] == '.' || text[text.Length - 1] == '!' || text[text.Length - 1] == '?');
            if (hasTerminalPunctuation) return false;

            if (Array.IndexOf(TrailingArticles, last) >= 0) { reason = "incomplete_trailing_article"; return true; }
            if (Array.IndexOf(TrailingConjunctions, last) >= 0) { reason = "incomplete_trailing_conjunction"; return true; }
            if (Array.IndexOf(TrailingPrepositions, last) >= 0) { reason = "incomplete_trailing_preposition"; return true; }
            return false;
        }

        internal static bool IsOverlong(string reply, int maxWords, int maxCharacters, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(reply)) return false;
            string text = reply.Trim();
            if (maxCharacters > 0 && text.Length > maxCharacters)
            {
                reason = "overlong_characters_" + text.Length;
                return true;
            }
            if (maxWords > 0)
            {
                int words = Regex.Matches(text, @"\S+").Count;
                if (words > maxWords)
                {
                    reason = "overlong_words_" + words;
                    return true;
                }
            }
            return false;
        }

        // Deterministic replacement for the old "send an already-accepted, already-grounded reply back
        // through the model to make it shorter/prettier" pass. That LLM rewrite could - and once did in
        // practice - drift the subject of an otherwise valid, grounded line while satisfying the length/
        // voice checks (they check length and voice, not fidelity to the original claim). An overlong
        // reply is instead trimmed to the largest whole-sentence prefix that fits the budget; if even the
        // first sentence alone does not fit, or the reply is not overlong for a purely length reason
        // (incomplete/voice-invalid), no safe deterministic edit exists and the caller must fall back to
        // NO_MESSAGE rather than spend a second LLM call.
        internal static bool TryDeterministicallyShorten(string reply, int maxWords, int maxCharacters, out string shortened)
        {
            shortened = string.Empty;
            if (string.IsNullOrWhiteSpace(reply)) return false;
            string text = reply.Trim();

            MatchCollection sentenceEnds = Regex.Matches(text, @"[.!?]+(?:\s|$)");
            string best = string.Empty;
            int cursor = 0;
            for (int i = 0; i < sentenceEnds.Count; i++)
            {
                int end = sentenceEnds[i].Index + sentenceEnds[i].Length;
                string candidate = text.Substring(0, end).Trim();
                if (candidate.Length == 0) continue;
                string reasonUnused;
                if (IsOverlong(candidate, maxWords, maxCharacters, out reasonUnused))
                {
                    if (best.Length == 0) { cursor = end; break; }
                    break;
                }
                best = candidate;
                cursor = end;
            }
            if (best.Length > 0)
            {
                shortened = best;
                return true;
            }

            // No complete sentence fits the budget at all (or the reply has no sentence punctuation).
            // Do not guess at a mid-clause cut - that is exactly the kind of edit that can change what a
            // reply claims. There is nothing safe left to trim deterministically.
            return false;
        }

        private static bool HasUnmatchedOpener(string text)
        {
            int quotes = 0;
            for (int i = 0; i < text.Length; i++) if (text[i] == '"') quotes++;
            if (quotes % 2 != 0) return true;
            int parenDepth = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                // ASCII MMO emoticons are intentional output in Erenshor and use ')' as a face,
                // not as a grammatical closer: :), :-), ;) and ;-).
                if ((c == ':' || c == ';') && i + 1 < text.Length)
                {
                    int close = text[i + 1] == '-' ? i + 2 : i + 1;
                    if (close < text.Length && (text[close] == ')' || text[close] == '('))
                    {
                        i = close;
                        continue;
                    }
                }
                if (c == '(') parenDepth++;
                else if (c == ')') parenDepth--;
            }
            return parenDepth != 0;
        }

        internal static List<string> RunSelfTests()
        {
            List<string> results = new List<string>();
            AddResult(results, "clear truncated fragment rejected", "I'm just a Windblade trying out the", true);
            AddResult(results, "valid short fragment accepted", "probably healer", false);
            AddResult(results, "valid slangy fragment accepted", "nah lol", false);
            AddResult(results, "trailing conjunction rejected", "yeah but", true);
            AddResult(results, "valid fragment with negation accepted", "not a chance lol", false);
            AddResult(results, "valid fragment with dependent clause accepted", "depends on the dungeon", false);
            AddResult(results, "unfinished trying-to clause rejected", "I was trying to", true);
            AddResult(results, "unmatched opening quote rejected", "he said \"watch out", true);
            AddResult(results, "complete sentence with terminal punctuation accepted", "the loot table for that boss is rough.", false);
            AddResult(results, "ascii smile accepted", "hey brinon :)", false);
            AddResult(results, "ascii grin accepted", "lol :D", false);
            AddResult(results, "inline emoticons accepted", "Hi!:) Brinon:)", false);
            AddResult(results, "unfinished name question rejected", "Brinon? You", true);
            string overlongReason;
            results.Add("[DeepSims ReplyQuality] overlong prose rejected: " +
                (IsOverlong("that sounds like a pretty interesting idea but i think we should probably keep discussing every possible part of it", 12, 180, out overlongReason) ? "PASS" : "FAIL"));
            return results;
        }

        private static void AddResult(List<string> results, string name, string reply, bool expectedIncomplete)
        {
            string reason;
            bool incomplete = IsIncomplete(reply, out reason);
            results.Add("[DeepSims ReplyQuality] " + name + ": " + (incomplete == expectedIncomplete ? "PASS" : "FAIL") +
                " (" + (string.IsNullOrWhiteSpace(reason) ? "accepted" : reason) + ")");
        }
    }
}
