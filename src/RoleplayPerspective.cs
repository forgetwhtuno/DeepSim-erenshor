using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ErenshorDeepSims
{
    // Perspective is ORTHOGONAL to ExpressionMode. ExpressionMode answers "how is a line produced"
    // (Auto / LLM / Templates / Off). Perspective answers "who is speaking" -- a simulated MMO player,
    // or the adventurer that SimPlayer represents. Neither one may be expressed in terms of the other,
    // and perspective never changes gameplay authority, the social budget, or what is verified.
    internal enum SocialPerspectiveMode
    {
        Mmo,
        Roleplay
    }

    internal static class SocialPerspective
    {
        internal const SocialPerspectiveMode Default = SocialPerspectiveMode.Mmo;

        // Existing installs must keep MMO behavior, so anything unparseable resolves to MMO rather
        // than silently switching a player's Sims into character.
        internal static SocialPerspectiveMode Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Default;
            string v = value.Trim().ToLowerInvariant();
            if (v == "roleplay" || v == "rp" || v == "in-world" || v == "inworld") return SocialPerspectiveMode.Roleplay;
            return SocialPerspectiveMode.Mmo;
        }

        internal static bool TryParseStrict(string value, out SocialPerspectiveMode mode)
        {
            mode = Default;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string v = value.Trim().ToLowerInvariant();
            if (v == "roleplay" || v == "rp" || v == "in-world" || v == "inworld") { mode = SocialPerspectiveMode.Roleplay; return true; }
            if (v == "mmo" || v == "player" || v == "off") { mode = SocialPerspectiveMode.Mmo; return true; }
            return false;
        }

        internal static string Describe(SocialPerspectiveMode mode)
        {
            return mode == SocialPerspectiveMode.Roleplay ? "Roleplay" : "MMO";
        }

        internal static bool IsRoleplay(SocialPerspectiveMode mode) { return mode == SocialPerspectiveMode.Roleplay; }
    }

    // The active perspective for this session. Held centrally because every prompt builder and every
    // expression path needs the same answer, and because a mod has exactly one active configuration.
    // Set from config at startup and by /dsroleplay. Defaults to MMO so an existing install is
    // unaffected until the player opts in.
    internal static class SocialPerspectiveState
    {
        private static SocialPerspectiveMode _current = SocialPerspective.Default;

        internal static SocialPerspectiveMode Current
        {
            get { return _current; }
            set { _current = value; }
        }

        internal static bool RoleplayActive { get { return _current == SocialPerspectiveMode.Roleplay; } }

        internal static void ResetForTests() { _current = SocialPerspective.Default; }
    }

    // ------------------------------------------------------------------------------------------
    // PROMPT CONTRACT
    // ------------------------------------------------------------------------------------------
    // One coherent identity block per perspective. The Roleplay block is NOT appended beneath the
    // MMO block: telling a Sim it is "a simulated human player typing in an MMO" and then telling it
    // to "speak as someone who inhabits Erenshor" produces incoherent output. The caller selects
    // exactly one.
    internal static class RoleplayPromptContract
    {
        // Perspective-neutral rules (truth, memory, grounding, output length) stay in PromptBuilder and
        // apply to BOTH perspectives. Only identity/voice differs here.
        internal static string BuildIdentityBlock(SocialPerspectiveMode mode, string simName)
        {
            string name = string.IsNullOrWhiteSpace(simName) ? "this adventurer" : simName.Trim();
            StringBuilder sb = new StringBuilder();

            if (mode != SocialPerspectiveMode.Roleplay)
            {
                // MMO identity is reproduced by PromptBuilder itself; this branch exists so callers can
                // ask for either block from one place without duplicating the MMO wording here.
                return string.Empty;
            }

            sb.AppendLine("You are " + name + ". Speak as yourself: an adventurer who lives in Erenshor.");
            sb.AppendLine("Speak as a person travelling with these companions right now.");
            sb.AppendLine("Stay entirely in-world. Never describe an out-of-world framing, software interface, or external controller.");
            sb.AppendLine("Speak only from your own first-person lived perspective.");
            sb.AppendLine("You are NOT an assistant, narrator, storyteller, quest-giver, or a source of world lore. Never offer generic help.");
            sb.AppendLine("Speak only as yourself, out loud, to the companions with you.");
            sb.AppendLine("Output only spoken words. No stage directions, no action narration, no asterisks, no brackets, no describing your own face, gestures, or movements.");
            sb.AppendLine("Do not invent history, lore, prophecy, gods, factions, places, biography, family, or rumours. If you do not know something, say so plainly.");
            sb.AppendLine("Do not speak in archaic or theatrical fantasy language. No 'hark', 'verily', 'thee', 'thou', 'yon', 'mine own', 'tis', or declamatory speeches.");
            sb.AppendLine("Talk like a real person: plain, current, direct. Short. Usually one sentence, often only a few words.");
            sb.AppendLine("Dry humour, bluntness, worry, curiosity, and simply saying nothing are all natural.");
            sb.AppendLine("You do not decide what anyone does. Never turn dialogue into an instruction that controls gameplay.");
            sb.AppendLine("Describe abilities and choices as lived preferences or fighting habits, never with out-of-world shorthand such as 'playstyle', 'build', 'spec', 'meta', 'rotation', 'main', 'alt', or 'toon'.");
            return sb.ToString();
        }

        // Sim-to-Sim continuation rules for Roleplay. Mirrors the MMO thread contract's *behaviour*
        // (one short reply to the newest line, topic ownership, no invented history, NO_MESSAGE when
        // there is nothing to add) without its "one MMO player in party chat" framing.
        internal const string ThreadRules =
            "CURRENT THREAD RULES: You are one of the companions travelling together, answering out loud in a " +
            "conversation the others can hear. You are not an assistant and not a narrator. Read the recent lines " +
            "below and reply to the MOST RECENT one specifically - the newest line - not the subject that first " +
            "started the exchange; do not summarize the conversation. One short reply is preferred, usually a " +
            "single sentence. It is fine to disagree, joke, tease, ask a short question, or say nothing. If you " +
            "agree or disagree, make it unambiguous what about. Do not introduce an unexplained \"it\" or \"that\" " +
            "unless its antecedent is actually present in the visible lines below. Do not pretend an event happened " +
            "unless a VERIFIED fact given to you says it happened. Opinions and harmless preferences are allowed; " +
            "do not invent shared history. Never invent a future shared plan and never say something happened " +
            "'again' unless a VERIFIED fact supports it. What was said in this exchange is unverified; VERIFIED " +
            "facts remain authoritative. Speak only spoken words: no stage directions, no action narration, no " +
            "describing your own gestures. Do not use archaic or theatrical fantasy phrasing. If you have no clear, " +
            "on-topic reply to the newest line, return exactly NO_MESSAGE.";

        // Words that betray the MMO/out-of-world frame when a Sim speaks on its own initiative.
        // Deliberately small and perspective-specific rather than a universal blacklist: the same
        // words are legitimate when the player explicitly asks a mechanics or meta question.
        private static readonly string[] MetaTerms = new string[]
        {
            "xp", "exp", "ding", "reroll", "respec", "build", "dps", "aggro", "threat", "respawn",
            "drop rate", "droprate", "bis", "afk", "log out", "logout", "logged in", "patch", "wiki",
            "server", "queue", "quest log", "hotbar", "cooldown timer", "grinding spot",
            "the game", "this game", "in game", "in-game", "gameplay", "player character",
            "my character", "your character", "npc", "npcs", "sim", "sims", "simplayer",
            "mob", "mobs", "loot table", "spawn timer", "nerf", "buffed", "meta", "endgame"
        };

        private static readonly Regex StageDirection = new Regex(
            @"(\*[^*]{1,80}\*)" +                       // *looks around*
            @"|(\[[^\]]{1,80}\])" +                     // [sighs]
            @"|(^\s*\([^)]{1,80}\)\s*$)" +              // (shrugs) as the whole line
            @"|(<[^>]{1,80}>)",                         // <smiles>
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Third-person self-narration such as "Phanty smiles." or "Phanty draws his sword."
        private static readonly Regex NarrationVerb = new Regex(
            @"\b(smiles|smiled|grins|grinned|nods|nodded|shrugs|shrugged|sighs|sighed|laughs|laughed|" +
            @"frowns|frowned|chuckles|chuckled|winks|winked|glances|glanced|draws|drew|raises|raised|" +
            @"turns|turned|steps|stepped|looks|looked|gestures|gestured|leans|leaned|nodding|smiling)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Typed-chat texture: correct for an MMO player at a keyboard, wrong for someone speaking
        // aloud. Kept separate from MetaTerms because these are not "meta" words -- they are artifacts
        // of the delivery medium, and Erenshor's own PersonalizeString can append them AFTER the
        // Roleplay guard has already accepted a line.
        private static readonly Regex ChatTexture = new Regex(
            @"(^|[\s.,!?])(lol+|lmao|rofl|xd+|heh+|haha+|brb|afk|ty|thx|np|gg|wb|o7|ftw|imo|irl)([\s.,!?]|$)" +
            @"|(:\)|:\(|:D|:P|:p|;\)|:3|=\)|\^\^|<3|:'\(|:o|:O)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal static bool ContainsChatTexture(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return ChatTexture.IsMatch(text);
        }

        // Strips typed-chat texture from ANYWHERE in a line, not only texture newly introduced by
        // native personalization (see KeepSpokenStyle). Used by RoleplayOutputGuard so an LLM/template
        // line that already contains "lol"/"heh"/":D" in its first draft gets the same salvage
        // treatment as one where the game's own typing style appended it afterward.
        internal static string StripChatTexture(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (!ChatTexture.IsMatch(text)) return text;
            string result = ChatTexture.Replace(text, delegate(Match m)
            {
                string val = m.Value;
                if (val.Length > 0)
                {
                    char last = val[val.Length - 1];
                    if (last == '.' || last == '!' || last == '?') return last.ToString();
                }
                return " ";
            });
            result = Regex.Replace(result, @"\s{2,}", " ");
            result = Regex.Replace(result, @"\s+([.,!?])", "$1");
            result = Regex.Replace(result, @"^[.,!?\s]+", "");
            return result.Trim();
        }

        // Applied to the result of native typing personalization in Roleplay only. Harmless native
        // traits (casing, punctuation shape, typos, third-person quirks) are kept; if the transform
        // introduced typed-chat texture that the accepted line did not have, the accepted line wins.
        // MMO perspective never calls this, so vanilla typing behavior there is untouched.
        internal static string KeepSpokenStyle(string styled, string accepted)
        {
            if (string.IsNullOrWhiteSpace(styled)) return accepted;
            if (string.IsNullOrWhiteSpace(accepted)) return styled;
            if (!ContainsChatTexture(styled)) return styled;
            // Only revert when the texture is genuinely new; never punish a line that already had it.
            if (ContainsChatTexture(accepted)) return styled;
            return accepted;
        }

        internal static bool ContainsStageDirection(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return StageDirection.IsMatch(text);
        }

        // "Phanty smiles." -- a narration sentence about the speaker rather than something spoken.
        // Requires the speaker's own name so ordinary sentences like "she smiles a lot" are untouched.
        internal static bool ContainsSelfNarration(string text, string speakerName)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(speakerName)) return false;
            string name = Regex.Escape(speakerName.Trim());
            Regex leading = new Regex(@"(^|[.!?]\s+|""\s*)" + name + @"\s+\w+", RegexOptions.IgnoreCase);
            Match m = leading.Match(text);
            if (!m.Success) return false;
            return NarrationVerb.IsMatch(m.Value);
        }

        // Autonomous roleplay speech is held strictly. A direct player question about mechanics is an
        // explicitly out-of-character turn and must still be answerable with real game words.
        internal static bool ViolatesRoleplayVoice(string text, string speakerName, bool autonomous, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            if (ContainsStageDirection(text)) { reason = "stage_direction"; return true; }
            if (ContainsSelfNarration(text, speakerName)) { reason = "self_narration"; return true; }
            if (!autonomous) return false;

            string lower = " " + Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9 \-]", " ") + " ";
            lower = Regex.Replace(lower, @"\s+", " ");
            for (int i = 0; i < MetaTerms.Length; i++)
            {
                string term = MetaTerms[i];
                if (lower.IndexOf(" " + term + " ", StringComparison.Ordinal) >= 0)
                {
                    reason = "meta_language:" + term;
                    return true;
                }
            }
            return false;
        }
    }

    // ------------------------------------------------------------------------------------------
    // CENTRAL ROLEPLAY OUTPUT GUARD
    // ------------------------------------------------------------------------------------------
    // RoleplayPromptContract.ViolatesRoleplayVoice/MetaTerms only fires its full meta-vocabulary
    // check when the caller marks a line "autonomous" -- correct for the ambient/autonomous path,
    // because a directly-asked mechanics question is allowed to use real game words. But that same
    // narrow trigger meant every DIRECTLY-ADDRESSED emission path (group replies, whisper, thread
    // continuations, verified-event reactions) never ran ANY roleplay-specific content check at all,
    // so live MMO/internet-chat texture ("online", "hit me up", "lol", "heh", ":D") reached the
    // player unfiltered while the diagnostic log reported roleplayGuardApplied=False.
    //
    // This is the ONE function every Roleplay-mode emission path must run immediately before a line
    // is queued/shown, regardless of which backend produced it (LLM first try, LLM retry, deterministic
    // template, event-thread reply). It is content-focused, not merely a "sounds spoken" style pass:
    // KeepSpokenStyle only reverts texture that PersonalizeString newly introduced after acceptance;
    // this guard also catches texture and out-of-world vocabulary present in the FIRST draft.
    //
    // Two tiers:
    //   1. Texture (RoleplayPromptContract.ChatTexture) - stripped in place; the sentence survives.
    //   2. Core-content meta vocabulary (RejectCoreWords/RejectCorePhrases) - the CONCEPT itself is
    //      out-of-world and cannot be safely reworded by deleting a token (e.g. "online" is not
    //      texture on a sentence, it IS the sentence's claim), so the whole candidate is rejected.
    // Stage direction and third-person self-narration reuse the existing detectors and are rejected
    // outright (Erenshor Sims cannot narrate their own actions, and there is no safe partial fix).
    internal static class RoleplayOutputGuard
    {
        // Data, not scattered logic: extend these arrays to teach the guard a new out-of-world
        // concept. Word entries are matched with \b boundaries; phrase entries match with flexible
        // internal whitespace. Kept deliberately narrow and perspective-specific. MMO perspective
        // never runs this guard. Roleplay does run it for every final spoken candidate, including
        // direct replies, so ambiguous ordinary words are rejected only in structural meta phrases.
        internal static readonly string[] RejectCoreWords = new string[]
        {
            // Keep this list to concepts that are unambiguously out-of-world by themselves.
            // Ordinary words such as "game", "session", and "player" are deliberately NOT here:
            // an adventurer can play a dice game, attend a training session, or call a musician a
            // player. Structural meta uses of those words are handled by phrases below.
            "server", "online", "offline", "dps",
            "nes", "playstation", "xbox", "nintendo", "wifi", "internet",
            // Gaming-meta character vocabulary. "playstyle" and "minmax(ing)" are unambiguous by
            // themselves -- no ordinary in-world sentence uses these words -- unlike "build", "spec",
            // "meta", "rotation", "main", or "alt", which all have legitimate in-world senses
            // (construct/create, keg tap, coin, a physical turn, a chief consideration, a second
            // weapon) and are deliberately left out of this word list. Those are handled instead by
            // the prompt-level guidance above and, where the combination is itself unambiguous, by
            // the structural phrases below.
            "playstyle", "minmax", "min-max", "minmaxing",
            // These abbreviations/nouns are unambiguously interface/game framing in spoken Roleplay.
            "npc", "npcs", "ui"
        };

        internal static readonly string[] RejectCorePhrases = new string[]
        {
            "hit me up", "add me", "friend request",
            "log in", "log out", "logged in", "logged out", "logging in", "logging out",
            "my character", "your character", "this character", "this erenshor character", "erenshor character", "player character",
            "i'm an npc", "i am an npc", "you're the player", "you are the player",
            "this game", "the game", "video game", "in this game", "in game", "game server", "gameplay", "game mechanics",
            "the devs", "the developers", "user interface",
            "this session", "login session", "play session",
            "on discord", "discord server", "discord channel",
            "on steam", "steam account", "steam client",
            "chat ate", "chat window", "chat box", "party chat", "group chat",
            // Structural combinations that are unambiguously gaming-meta even though their individual
            // words ("build", "spec", "meta", "rotation", "main", "alt", "toon") are not: "meta build"
            // and "dps rotation" only ever mean the game-mechanics concept, and "my/your toon" and
            // "my/your alt [character]" are gaming-only usages of otherwise rare/absent in-world words.
            "meta build", "dps rotation",
            "my toon", "your toon", "my alt character", "your alt character", "on my alt"
        };

        private static readonly Regex RejectCoreRegex = BuildRejectRegex();

        private static Regex BuildRejectRegex()
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < RejectCoreWords.Length; i++)
                parts.Add(@"\b" + Regex.Escape(RejectCoreWords[i]) + @"\b");
            for (int i = 0; i < RejectCorePhrases.Length; i++)
            {
                // Regex.Escape() already escapes whitespace itself (turning a plain space into the
                // two-character sequence "\ "), so escaping the whole phrase first and then trying
                // to replace its (now-escaped-away) spaces with "\s+" never finds a bare space to
                // replace and instead corrupts the pattern (each "\ " becomes "\\s+", i.e. a literal
                // backslash followed by one-or-more literal 's' characters, which cannot match real
                // text). Escaping each word independently and joining with a literal \s+ avoids that
                // trap entirely and is what silently disabled every multi-word phrase below (e.g.
                // "this game", "on steam", "my character", "hit me up").
                string[] words = RejectCorePhrases[i].Split(' ');
                string[] escapedWords = new string[words.Length];
                for (int w = 0; w < words.Length; w++) escapedWords[w] = Regex.Escape(words[w]);
                parts.Add(@"(?<!\w)" + string.Join(@"\s+", escapedWords) + @"(?!\w)");
            }
            return new Regex(string.Join("|", parts.ToArray()), RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        internal static bool ContainsRejectableCore(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return RejectCoreRegex.IsMatch(text);
        }

        internal const string Rejected = "NO_MESSAGE";

        // Runs the whole boundary. `changed` is true when the text was altered but survived (texture
        // stripped, stage direction removed); `rejected` is true when nothing safe could be salvaged
        // and the caller must fall back to silence or a separate deterministic line. Only ever called
        // while Roleplay is active -- MMO perspective never reaches this function.
        internal static string Enforce(string candidate, string speakerName, out bool changed, out bool rejected)
        {
            changed = false;
            rejected = false;
            if (string.IsNullOrWhiteSpace(candidate)) return candidate;
            string trimmed = candidate.Trim();
            if (DialogueControlSentinel.IsNoMessage(trimmed)) return candidate;

            // Core out-of-world content: not fixable by deleting a word, reject the whole line.
            if (ContainsRejectableCore(candidate) ||
                RoleplayPromptContract.ContainsSelfNarration(candidate, speakerName))
            {
                rejected = true;
                return Rejected;
            }

            string working = candidate;

            if (RoleplayPromptContract.ContainsStageDirection(working))
            {
                string withoutStageDirection = StageDirectionOnly.Replace(working, " ");
                withoutStageDirection = Regex.Replace(withoutStageDirection, @"\s{2,}", " ").Trim();
                if (string.IsNullOrWhiteSpace(withoutStageDirection)) { rejected = true; return Rejected; }
                working = withoutStageDirection;
            }

            string stripped = RoleplayPromptContract.StripChatTexture(working);
            if (!string.Equals(stripped, working, StringComparison.Ordinal)) working = stripped;

            if (string.IsNullOrWhiteSpace(working)) { rejected = true; return Rejected; }

            changed = !string.Equals(working, candidate, StringComparison.Ordinal);
            return working;
        }

        // Mirrors RoleplayPromptContract's stage-direction detector but is used destructively here
        // (removal) rather than only as a yes/no gate.
        private static readonly Regex StageDirectionOnly = new Regex(
            @"(\*[^*]{1,80}\*)|(\[[^\]]{1,80}\])|(<[^>]{1,80}>)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    // ------------------------------------------------------------------------------------------
    // DIRECT-REPLY FALLBACK
    // ------------------------------------------------------------------------------------------
    // The autonomous/ambient path already refuses to fall back to MMO-flavored templates in
    // Roleplay (see RoleplayExpressionRouter above). The directly-addressed reply path
    // (party chat and whisper) needs the same guarantee: when a directly-addressed question's LLM
    // answer is rejected twice by the grounding guard -- which happens routinely for subjective
    // "what do you think about X" turns, since GroundingGuard.IsSubjectiveDeflection treats an
    // uncertain-sounding LLM answer as a rejected deflection -- SOME line still has to be shown.
    // SocialTemplates' fillers ("i don't know that one", "beats me on that one") are worded as an
    // MMO player typing in party chat and must never be shown while Roleplay is active, or the
    // perspective toggle silently does nothing on exactly the turns a player is most likely to
    // test: an addressed question the model could not verify.
    internal static class RoleplayFallback
    {
        internal static string RenderUnknownFact(string playerMessage, SimSnapshot speaker)
        {
            string m = (playerMessage ?? string.Empty).ToLowerInvariant();
            int seed = RoleplayTemplates.StableHash((speaker == null ? string.Empty : speaker.Name ?? string.Empty) + "|rpunknown|" + m);
            return RoleplayTemplates.Pick(seed, new string[]
            {
                "I don't know that.", "Can't say I know.", "That's not something I've learned.", "No idea, honestly."
            });
        }

        // A directly-addressed subjective/opinion question about the speaker's own identity (class,
        // preferences) is exactly what RoleplayAffinity's cultural-interest lines exist for, so try
        // that first; otherwise fall back to a fact-free, perspective-correct deflection.
        internal static string RenderIdentityFact(string playerMessage, SimSnapshot speaker, SimMemory memory)
        {
            if (speaker == null) return "I can only speak for myself.";
            string m = (playerMessage ?? string.Empty).ToLowerInvariant();
            AuthoredIdentityProfile authored = memory == null ? null : memory.AuthoredIdentity;
            if (authored != null) authored.Normalize();
            string persona = authored == null ? string.Empty : RenderAuthoredSelfDescription(authored.ErenshorPersona);
            string personality = authored == null ? string.Empty : RenderAuthoredPersonality(authored.CorePersonality);

            // Birthplace/origin is never inferred from class, faction, scene, or a default template.
            // Authored persona remains in the prompt for the LLM path, but the deterministic fallback
            // refuses to parse arbitrary prose into a birthplace claim.
            if (Regex.IsMatch(m, @"\b(?:where are you from|where did you grow up|where were you born|your birthplace|your home town|your hometown)\b", RegexOptions.IgnoreCase))
                return "I don't talk much about where I came from.";

            if (Regex.IsMatch(m, @"\b(?:who are you|tell me about yourself)\b", RegexOptions.IgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(persona)) return "I'm " + speaker.Name + ". " + persona;
                if (!string.IsNullOrWhiteSpace(speaker.ClassName)) return "I'm " + speaker.Name + ", a " + speaker.ClassName + ".";
                return "I'm " + speaker.Name + ".";
            }

            if (Regex.IsMatch(m, @"\b(?:what do you do|what did you do before this|before adventuring)\b", RegexOptions.IgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(persona)) return persona;
                if (!string.IsNullOrWhiteSpace(speaker.ClassName)) return "These days, I make my way as a " + speaker.ClassName + ".";
                return "These days, I travel where the road takes me.";
            }

            if (Regex.IsMatch(m, @"\b(?:what are you like|what kind of person are you)\b", RegexOptions.IgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(personality)) return personality;
                return "You'll get a better sense of me by travelling together.";
            }

            if (!string.IsNullOrWhiteSpace(persona)) return "I'm " + speaker.Name + ". " + persona;
            return !string.IsNullOrWhiteSpace(speaker.ClassName)
                ? "I'm " + speaker.Name + ", a " + speaker.ClassName + "."
                : "I'm " + speaker.Name + ".";
        }

        private static string RenderAuthoredSelfDescription(string value)
        {
            string text = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length == 0) return string.Empty;
            if (text.Length > 220) text = text.Substring(0, 220).Trim();
            string lower = text.ToLowerInvariant();
            if (lower.StartsWith("i'm ") || lower.StartsWith("i am ") || lower.StartsWith("i serve ") || lower.StartsWith("i belong "))
                return EnsureSentence(text);
            if (Regex.IsMatch(text, @"^(?:a|an|the|member|initiate|student|scholar|apprentice|veteran|traveler|traveller)\b", RegexOptions.IgnoreCase))
                return EnsureSentence("I'm " + text);
            return string.Empty;
        }

        private static string RenderAuthoredPersonality(string value)
        {
            string text = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length == 0) return string.Empty;
            if (text.Length > 160) text = text.Substring(0, 160).Trim();
            if (Regex.IsMatch(text, @"^[A-Za-z][A-Za-z ,'-]{1,150}$"))
                return EnsureSentence("I'd say I'm " + text);
            return string.Empty;
        }

        private static string EnsureSentence(string value)
        {
            string text = (value ?? string.Empty).Trim();
            if (text.Length == 0) return text;
            char last = text[text.Length - 1];
            return last == '.' || last == '!' || last == '?' ? text : text + ".";
        }

        internal static bool TryRenderSubjective(string playerMessage, SimSnapshot speaker, out string message)
        {
            message = string.Empty;
            if (speaker == null) return false;
            string m = (playerMessage ?? string.Empty).ToLowerInvariant();
            long affinitySeed = RoleplayTemplates.StableHash((speaker.Name ?? string.Empty) + "|rpaffinity|" + m);
            if (RoleplayAffinity.TryRenderCulturalInterest(speaker.ClassName, speaker.Name, affinitySeed, out message)) return true;
            int seed = RoleplayTemplates.StableHash((speaker.Name ?? string.Empty) + "|rpsubj|" + m);
            message = RoleplayTemplates.Pick(seed, new string[]
            {
                "Hard to say, honestly.", "I'd rather show you than talk about it.", "Ask me again once we know each other better."
            });
            return true;
        }
    }

    // Which deterministic template backend owns a line. Perspective decides the backend; it never
    // decides WHETHER speech happens -- SocialBudget still owns that.
    internal enum ExpressionBackend
    {
        None,           // Off
        MmoTemplates,
        RoleplayTemplates,
        Llm
    }

    internal static class RoleplayExpressionRouter
    {
        // The whole mode matrix in one pure function so all eight combinations are testable without
        // a live game. Templates are a first-class Roleplay backend, not an error path: Auto with an
        // unhealthy Ollama resolves to the same RP template backend as explicit Templates mode.
        internal static ExpressionBackend Resolve(SocialPerspectiveMode perspective, SocialExpressionMode expression, bool ollamaHealthy)
        {
            if (expression == SocialExpressionMode.Off) return ExpressionBackend.None;
            bool roleplay = perspective == SocialPerspectiveMode.Roleplay;
            if (expression == SocialExpressionMode.Templates)
                return roleplay ? ExpressionBackend.RoleplayTemplates : ExpressionBackend.MmoTemplates;
            if (expression == SocialExpressionMode.Llm) return ExpressionBackend.Llm;
            // Auto
            if (ollamaHealthy) return ExpressionBackend.Llm;
            return roleplay ? ExpressionBackend.RoleplayTemplates : ExpressionBackend.MmoTemplates;
        }

        // Ambient/seeded subject. In Roleplay there is deliberately NO fallthrough to the MMO pool:
        // an MMO template would answer an in-world subject with "reroll", "grinding", or "lol".
        internal static bool TryRenderAmbientSeed(string topicKey, string verifiedFact, long opportunityId,
            SimSnapshot speaker, out string message)
        {
            message = string.Empty;
            if (speaker == null) return false;
            if (!SocialPerspectiveState.RoleplayActive)
                return SocialTemplates.TryRenderAmbientSeed(topicKey, verifiedFact, opportunityId, speaker, out message);

            // Class interest is routed here because this is where the speaker's verified class is
            // known. No affinity, or nothing safe to render, means silence rather than a substitute.
            if (string.Equals(topicKey, "rp_class_interest", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(verifiedFact)) return false;
                if (!RoleplayAffinity.TryRenderCulturalInterest(speaker.ClassName, speaker.Name, opportunityId, out message))
                {
                    message = string.Empty;
                    return false;
                }
                return PassesAutonomousGuard(message, speaker.Name);
            }

            bool greedy = speaker.Greed >= 60 || speaker.GearChase >= 60;
            if (!RoleplayTemplates.TryRenderAmbient(topicKey, verifiedFact, opportunityId, speaker.Name,
                    speaker.Patience, speaker.Rival, greedy, out message))
            {
                message = string.Empty;
                return false;
            }
            return PassesAutonomousGuard(message, speaker.Name);
        }

        internal static bool TryRenderEvent(string eventType, SimSnapshot speaker, long opportunityId, out string message)
        {
            message = string.Empty;
            if (speaker == null) return false;
            if (!SocialPerspectiveState.RoleplayActive) return false; // MMO path handled by its own caller
            if (!RoleplayTemplates.TryRenderEvent(eventType, speaker.Name, speaker.Rival, opportunityId, out message))
            {
                message = string.Empty;
                return false;
            }
            return PassesAutonomousGuard(message, speaker.Name);
        }

        // Final autonomous Roleplay output boundary. A deterministic template should never trip this,
        // but routing every RP line through one guard means a future template or an LLM line cannot
        // bypass it.
        internal static bool PassesAutonomousGuard(string message, string speakerName)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            string reason;
            if (RoleplayPromptContract.ViolatesRoleplayVoice(message, speakerName, true, out reason)) return false;
            return true;
        }

        internal const string NoMessage = "NO_MESSAGE";

        // THE autonomous generated-output boundary. DeepSimsPlugin calls exactly this after grounding,
        // so the deterministic tests exercise the same code path the runtime uses rather than a copy.
        // MMO perspective returns the line untouched; Roleplay gets one salvage attempt, then silence.
        internal static string GuardGeneratedAutonomousLine(string line, string topicKey, long opportunityId,
            SimSnapshot speaker, bool roleplayActive)
        {
            if (!roleplayActive) return line;
            if (speaker == null || string.IsNullOrWhiteSpace(line)) return line;
            if (DialogueControlSentinel.IsNoMessage(line)) return line;
            string salvaged;
            if (TrySalvageAutonomousLine(line, topicKey, opportunityId, speaker, out salvaged)) return salvaged;
            return NoMessage;
        }

        // An autonomous LLM line that leaks the MMO frame gets ONE deterministic rescue, then silence.
        // No repeated retries.
        internal static bool TrySalvageAutonomousLine(string llmLine, string topicKey, long opportunityId,
            SimSnapshot speaker, out string message)
        {
            message = string.Empty;
            if (speaker == null) return false;
            if (PassesAutonomousGuard(llmLine, speaker.Name)) { message = llmLine; return true; }
            return TryRenderAmbientSeed(topicKey, null, opportunityId, speaker, out message);
        }
    }

    // ------------------------------------------------------------------------------------------
    // ROLEPLAY AFFINITY
    // ------------------------------------------------------------------------------------------
    // Four concepts kept strictly apart:
    //   A. actual game faction state  -- verified from Erenshor (WorldFaction values)
    //   B. cultural affinity          -- a character tendency; NOT encoded in V1, see below
    //   C. faction attitude           -- interpretation of verified exposure
    //   D. factual membership         -- never invented, and V1 has no source for it at all
    internal enum RoleplayFactionAttitude
    {
        Unknown,
        Wary,
        Neutral,
        Sympathetic,
        Loyal
    }

    internal static class RoleplayAffinity
    {
        // Reputation movement proves the party interacted with a faction. It does not prove warmth,
        // history, or belief, so the mapping is deliberately shallow and caps at Sympathetic.
        // Loyal is never produced automatically in V1: nothing currently available distinguishes
        // "sustained, repeated positive history" from "one large positive turn-in".
        internal const float ExposureEpsilon = 0.01f;

        internal static RoleplayFactionAttitude AttitudeFor(bool known, float currentValue, float defaultValue)
        {
            if (!known) return RoleplayFactionAttitude.Unknown;
            float delta = currentValue - defaultValue;
            if (Math.Abs(delta) <= ExposureEpsilon) return RoleplayFactionAttitude.Unknown; // seen it exists, no dealings
            if (delta < 0f) return RoleplayFactionAttitude.Wary;
            return RoleplayFactionAttitude.Sympathetic;
        }

        internal static bool IsExposed(float currentValue, float defaultValue)
        {
            return Math.Abs(currentValue - defaultValue) > ExposureEpsilon;
        }

        // Membership is a factual claim. Nothing in current Erenshor exposes "this Sim belongs to that
        // faction", so this always answers false. It exists so callers ask the question explicitly
        // rather than inferring membership from an attitude or a class.
        internal static bool ClaimsMembership(RoleplayFactionAttitude attitude)
        {
            return false;
        }

        // CULTURAL AFFINITY (not membership).
        //
        // Source boundary: these are semantic keys only. No wiki prose is stored, quoted, or adapted.
        // The mapping records "this tradition is culturally relevant to this class" as documented by
        // current official Erenshor class/deity material; it records nothing about any individual Sim.
        // Assembly-CSharp does NOT encode these links (Character.Faction is a coarse combat-alignment
        // enum; Solunarian_Buff / Braxonian_Buff are SpellLine spell schools), which is precisely why
        // this stays affinity and never becomes membership.
        //
        // Reaver and Stormcaller are deliberately absent: no equivalent direct class-to-tradition
        // evidence was found, so they receive no automatic affinity.
        internal const string AffinityBrax = "brax";
        internal const string AffinityFernalla = "fernalla";
        internal const string AffinitySoluna = "soluna";
        internal const string AffinityVitheo = "vitheo";

        internal static string CulturalAffinityFor(string className)
        {
            if (string.IsNullOrWhiteSpace(className)) return string.Empty;
            string c = className.Trim().ToLowerInvariant();
            if (c == "arcanist") return AffinityBrax;
            if (c == "druid") return AffinityFernalla;
            if (c == "paladin") return AffinitySoluna;
            // Legacy internal data calls Windblade "Duelist"; both map to the same affinity.
            if (c == "windblade" || c == "duelist") return AffinityVitheo;
            return string.Empty;
        }

        // Windblade's link rests on ability naming and the legacy Duelist association rather than an
        // explicit class-deity statement, so callers can weight it lower. Confidence never upgrades
        // affinity into membership.
        internal static bool IsWeakAffinity(string className)
        {
            string affinity = CulturalAffinityFor(className);
            return affinity == AffinityVitheo;
        }

        internal static bool HasCulturalAffinity(string className)
        {
            return !string.IsNullOrEmpty(CulturalAffinityFor(className));
        }

        // Cultural affinity is a tendency, never an affiliation. This is a hard false so no caller can
        // turn "Paladin" into "I serve in the Solunarian Brotherhood".
        internal static bool AffinityClaimsMembership(string className)
        {
            return false;
        }

        // Occasional class-coloured interest lines. They express what catches this character's
        // attention. They assert no membership, office, upbringing, family, or belief history.
        internal static bool TryRenderCulturalInterest(string className, string speakerName, long opportunityId, out string message)
        {
            message = string.Empty;
            string affinity = CulturalAffinityFor(className);
            if (string.IsNullOrEmpty(affinity) || string.IsNullOrWhiteSpace(speakerName)) return false;
            int seed = RoleplayTemplates.StableHash(affinity + "|" + speakerName + "|" + opportunityId);

            if (affinity == AffinityBrax)
                message = RoleplayTemplates.Pick(seed, new string[] { "Old magic is worth understanding.", "Someone wrote all this down once.", "I'd rather know how it works than fear it." });
            else if (affinity == AffinityFernalla)
                message = RoleplayTemplates.Pick(seed, new string[] { "Death by itself isn't what bothers me.", "Something still grows here.", "Left alone, this place would recover." });
            else if (affinity == AffinitySoluna)
                message = RoleplayTemplates.Pick(seed, new string[] { "Faith and an order aren't the same thing.", "Doing right doesn't need an audience.", "I'd rather be useful than righteous." });
            else if (affinity == AffinityVitheo)
                message = RoleplayTemplates.Pick(seed, new string[] { "Hesitation gets people hurt.", "Footwork matters more than strength.", "Whoever taught that stance knew their work." });
            else return false;

            return !string.IsNullOrEmpty(message);
        }
    }

    // Bounded snapshot of verified faction exposure for the current moment. The plugin refreshes this
    // from RoleplayKnowledgeReader on a slow cadence; the pure seed/template code only reads it, so no
    // game type leaks into the deterministic layer. Holds ONE faction, never a database.
    internal static class RoleplayFactionContext
    {
        private static string _name;
        private static RoleplayFactionAttitude _attitude = RoleplayFactionAttitude.Unknown;
        private static bool _has;

        internal static bool HasExposedFaction { get { return _has; } }
        internal static string FactionName { get { return _name; } }
        internal static RoleplayFactionAttitude Attitude { get { return _attitude; } }

        internal static void Set(string factionName, RoleplayFactionAttitude attitude)
        {
            if (string.IsNullOrWhiteSpace(factionName)) { Clear(); return; }
            // Unknown carries no stance worth a subject; treating it as exposure would let a Sim open
            // a conversation about a faction it has nothing to say about.
            if (attitude == RoleplayFactionAttitude.Unknown) { Clear(); return; }
            _name = factionName.Trim();
            _attitude = attitude;
            _has = true;
        }

        internal static void Clear()
        {
            _name = null;
            _attitude = RoleplayFactionAttitude.Unknown;
            _has = false;
        }
    }

    // True when at least one currently active Deep Sim has a class cultural affinity, so the
    // rp_class_interest subject is only offered when somebody present could actually speak it.
    // Per-speaker eligibility is still re-checked at render time.
    internal static class RoleplayClassContext
    {
        private static bool _any;
        internal static bool AnyAffinityPresent { get { return _any; } }
        internal static void Set(bool any) { _any = any; }
        internal static void Clear() { _any = false; }
    }

    // ------------------------------------------------------------------------------------------
    // TEMPLATE FACT SAFETY
    // ------------------------------------------------------------------------------------------
    // A fact-free renderer must never be handed a historical or reference subject and left to invent
    // the detail. Categories are explicit so the router can refuse rather than improvise.
    internal enum RoleplayTemplateSafety
    {
        FactFree,
        EventBound,
        ReferenceBound,
        MemoryBound
    }

    internal static class RoleplayTemplates
    {
        // Roleplay topic catalog. Deliberately separate from the MMO seed topics rather than a rewrite
        // of them: an in-world adventurer has no opinion about rerolling or grinding spots.
        internal static readonly string[] TopicCatalog = new string[]
        {
            "rp_place", "rp_curiosity", "rp_danger", "rp_adventure",
            "rp_downtime", "rp_tease", "rp_companions", "rp_belief"
        };

        internal static RoleplayTemplateSafety ClassifyTopic(string topicKey)
        {
            if (string.IsNullOrWhiteSpace(topicKey)) return RoleplayTemplateSafety.FactFree;
            string t = topicKey.Trim().ToLowerInvariant();
            if (t.StartsWith("memory:", StringComparison.Ordinal) || t.StartsWith("outing:", StringComparison.Ordinal) ||
                t.StartsWith("callback_", StringComparison.Ordinal)) return RoleplayTemplateSafety.MemoryBound;
            if (t.StartsWith("reference:", StringComparison.Ordinal) || t.StartsWith("lore:", StringComparison.Ordinal))
                return RoleplayTemplateSafety.ReferenceBound;
            if (t.StartsWith("event:", StringComparison.Ordinal)) return RoleplayTemplateSafety.EventBound;
            return RoleplayTemplateSafety.FactFree;
        }

        // Fact-free ambient roleplay. Same inputs always produce the same line.
        // Returns false (=> NO_MESSAGE is valid) rather than inventing anything for a bound subject.
        internal static bool TryRenderAmbient(string topicKey, string verifiedFact, long opportunityId,
            string speakerName, int patience, bool rival, bool greedy, out string message)
        {
            message = string.Empty;
            if (string.IsNullOrWhiteSpace(topicKey) || string.IsNullOrWhiteSpace(speakerName)) return false;

            // A supplied verified fact means the subject is bound; paraphrasing it safely is the
            // grounded path's job, not a fact-free template's.
            if (!string.IsNullOrWhiteSpace(verifiedFact)) return false;
            if (ClassifyTopic(topicKey) != RoleplayTemplateSafety.FactFree) return false;

            string topic = topicKey.Trim().ToLowerInvariant();
            int seed = StableHash(topic + "|" + speakerName + "|" + opportunityId);

            if (topic == "rp_place")
                message = Pick(seed, new string[] { "I don't like the feel of this place.", "Quiet here.", "I've seen friendlier places.", "Not somewhere I'd want to sleep." });
            else if (topic == "rp_curiosity")
                message = Pick(seed, new string[] { "Wonder who built this.", "Any idea what this was?", "I'd like to know what happened here.", "Does that mean anything to you?" });
            else if (topic == "rp_danger")
                message = Pick(seed, new string[] { "Stay sharp.", "Something feels wrong.", "I'd rather know what's ahead.", "Keep close." });
            else if (topic == "rp_adventure")
                message = greedy
                    ? Pick(seed, new string[] { "If there's anything worth taking here, I'd like to see it.", "I want to see what's further in.", "Turning back already?" })
                    : Pick(seed, new string[] { "Turning back already?", "I want to see what's further in.", "We came this far." });
            else if (topic == "rp_downtime")
                message = patience >= 60
                    ? Pick(seed, new string[] { "Good to sit for a moment.", "No hurry.", "I needed the quiet." })
                    : Pick(seed, new string[] { "How long are we staying here?", "I needed the quiet.", "Not too long, I hope." });
            else if (topic == "rp_tease")
                message = rival
                    ? Pick(seed, new string[] { "Try to keep up.", "You're enjoying this, aren't you?", "Try not to find trouble for five minutes." })
                    : Pick(seed, new string[] { "Try not to find trouble for five minutes.", "This is suspiciously peaceful.", "You're enjoying this, aren't you?" });
            else if (topic == "rp_companions")
                message = Pick(seed, new string[] { "You're taking point?", "Someone has to keep you alive.", "Give me a moment." });
            else if (topic == "rp_belief")
                message = Pick(seed, new string[] { "I'm not sure I believe that.", "People say a lot of things.", "I'd want to see it myself." });
            else if (topic == "rp_faction_opinion")
            {
                // Requires verified exposure. The attitude is derived from live standing movement, and
                // the line asserts a stance only -- never membership, motive, or history.
                if (!RoleplayFactionContext.HasExposedFaction) return false;
                if (!TryRenderFactionAttitude(RoleplayFactionContext.Attitude, speakerName, opportunityId, out message)) return false;
            }
            else if (topic == "rp_faction_uncertainty")
            {
                if (!RoleplayFactionContext.HasExposedFaction) return false;
                if (!TryRenderFactionAttitude(RoleplayFactionAttitude.Unknown, speakerName, opportunityId, out message)) return false;
            }
            else return false;

            return !string.IsNullOrEmpty(message);
        }

        // Event reactions. The event type is the verified premise; the template adds only tone.
        internal static bool TryRenderEvent(string eventType, string speakerName, bool rival, long opportunityId, out string message)
        {
            message = string.Empty;
            if (string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(speakerName)) return false;
            string type = eventType.Trim().ToLowerInvariant();
            int seed = StableHash(type + "|" + speakerName + "|" + opportunityId);

            if (type == "player_level_up" || type == "sim_level_up")
                message = rival ? Pick(seed, new string[] { "Not bad.", "You're getting stronger.", "Catching up, then." })
                                : Pick(seed, new string[] { "You're getting stronger.", "Not bad.", "Well earned." });
            else if (type == "player_death" || type == "sim_death")
                message = Pick(seed, new string[] { "That looked painful.", "Careful.", "That was too close." });
            else if (type == "player_revive")
                message = Pick(seed, new string[] { "Good to have you back.", "Back on your feet.", "Take a moment." });
            else if (type == "duel_completed")
                message = Pick(seed, new string[] { "Well fought.", "That was worth watching.", "Good match." });
            else if (type == "zone_arrival")
                message = Pick(seed, new string[] { "We made it.", "So this is the place.", "Here we are, then." });
            else if (type == "party_join")
                message = Pick(seed, new string[] { "Good to have you.", "Well met.", "You're with us, then." });
            else if (type == "party_leave")
                message = Pick(seed, new string[] { "Safe travels.", "Until next time.", "Take care." });
            else return false;

            return !string.IsNullOrEmpty(message);
        }

        // Deterministic response to the player's short MMO-style ritual input while the Sim is
        // speaking in Roleplay perspective. The PLAYER may type "gg"/"brb"/"lol"; the Sim's
        // reply still has to be spoken as the in-world adventurer. This deliberately does not mutate
        // SocialTemplates, so MMO perspective keeps its existing party-chat texture.
        internal static bool TryRenderPlayerRitual(string playerMessage, SimSnapshot speaker, out string message)
        {
            message = string.Empty;
            if (speaker == null || !SocialPolicy.IsRitualPlayerMessage(playerMessage)) return false;
            string m = playerMessage.Trim().ToLowerInvariant().Trim('.', '!', '?', ' ');
            int seed = StableHash(m + "|rpritual|" + (speaker.Name ?? string.Empty));

            if (m == "ding") message = Pick(seed, new string[] { "Well done.", "Nicely done." });
            else if (m == "grats" || m == "gz" || m == "congrats") message = Pick(seed, new string[] { "Thank you.", "Much appreciated." });
            else if (m == "gg") message = Pick(seed, new string[] { "Well fought.", "Good fight." });
            else if (m == "inc" || m == "incoming") message = "Ready.";
            else if (m == "ready") message = "Ready.";
            else if (m == "brb") message = Pick(seed, new string[] { "I'll wait.", "Take your time." });
            else if (m == "wb") message = Pick(seed, new string[] { "Good.", "There you are." });
            else if (m == "nice") message = Pick(seed, new string[] { "Agreed.", "That went well." });
            else if (m == "ouch" || m == "rip") message = Pick(seed, new string[] { "Careful.", "That hurt." });
            else if (m == "lol" || m == "lmao") message = Pick(seed, new string[] { "Fair.", "You enjoyed that." });
            else if (m == "ty" || m == "thanks") message = Pick(seed, new string[] { "Of course.", "Any time." });
            return !string.IsNullOrWhiteSpace(message);
        }

        // Roleplay counterpart to SocialTemplates.TryRenderThreadReply. It handles only the small
        // deterministic topic set that Templates mode already knows how to continue and returns false
        // for everything else. Native verified class may colour a response; no class is inferred.
        internal static bool TryRenderThreadReply(string latestText, SimSnapshot speaker, out string message)
        {
            message = string.Empty;
            if (speaker == null || string.IsNullOrWhiteSpace(latestText)) return false;
            string m = latestText.Trim().ToLowerInvariant();
            int seed = StableHash(m + "|rpthread|" + (speaker.Name ?? string.Empty));
            string cls = string.IsNullOrWhiteSpace(speaker.ClassName) ? string.Empty : speaker.ClassName.Trim();
            string clsLower = cls.ToLowerInvariant();

            if (m.Contains("tank") && (m.Contains("hard") || m.Contains("job") || m.Contains("harder")))
            {
                message = clsLower == "druid"
                    ? Pick(seed, new string[] { "Keeping them standing isn't easy either.", "I worry more about keeping everyone alive." })
                    : Pick(seed, new string[] { "Holding the front takes nerve.", "I wouldn't call it easy." });
                return true;
            }
            if (m.Contains("heal") && (m.Contains("hard") || m.Contains("job")))
            {
                message = Pick(seed, new string[] { "Keeping everyone standing isn't easy.", "Watching everyone at once takes focus." });
                return true;
            }
            if (m.Contains("favorite") && m.Contains("class"))
            {
                message = string.IsNullOrWhiteSpace(cls)
                    ? Pick(seed, new string[] { "I care more about how someone fights.", "Hard to choose without knowing them better." })
                    : Pick(seed, new string[] { "I'm comfortable as a " + cls + ".", "Being a " + cls + " suits me." });
                return true;
            }
            if (m.EndsWith("?", StringComparison.Ordinal) && (m.Contains("agree") || m.Contains("right")))
            {
                message = Pick(seed, new string[] { "I do.", "That sounds right." });
                return true;
            }
            return false;
        }

        internal static string Pick(int seed, string[] options)
        {
            if (options == null || options.Length == 0) return string.Empty;
            int index = Math.Abs(seed) % options.Length;
            return options[index];
        }

        // Fact-free attitude lines. They express a stance toward a faction the party has verifiably
        // interacted with. They never assert membership, motive, history, religion, or quest facts.
        internal static bool TryRenderFactionAttitude(RoleplayFactionAttitude attitude, string speakerName,
            long opportunityId, out string message)
        {
            message = string.Empty;
            if (string.IsNullOrWhiteSpace(speakerName)) return false;
            int seed = StableHash(attitude.ToString() + "|" + speakerName + "|" + opportunityId);

            if (attitude == RoleplayFactionAttitude.Wary)
                message = Pick(seed, new string[] { "I don't trust them yet.", "I'd keep an eye on them.", "Something about them puts me on edge." });
            else if (attitude == RoleplayFactionAttitude.Sympathetic)
                message = Pick(seed, new string[] { "They may have a point.", "I'd hear them out.", "I'm not ready to write them off." });
            else if (attitude == RoleplayFactionAttitude.Neutral)
                message = Pick(seed, new string[] { "I haven't made up my mind.", "We'll see what they do.", "I care more about what they do than what they call themselves." });
            else if (attitude == RoleplayFactionAttitude.Unknown)
                message = Pick(seed, new string[] { "I don't know enough about them.", "I'd hear them out first.", "Hard to judge people we barely know." });
            else return false; // Loyal is unreachable in V1; see RoleplayAffinity.
            return !string.IsNullOrEmpty(message);
        }

        internal static int StableHash(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            unchecked
            {
                int hash = 23;
                for (int i = 0; i < value.Length; i++) hash = hash * 31 + value[i];
                return hash;
            }
        }
    }
}
