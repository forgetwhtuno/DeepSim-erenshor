using System;

namespace ErenshorDeepSims
{
    internal static class ChatCommandParser
    {

        internal static bool TryParsePartyChat(string raw, out string message)
        {
            message = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            string[] prefixes = new string[] { "/p", "/party", "/group" };
            for (int i = 0; i < prefixes.Length; i++)
            {
                string prefix = prefixes[i];
                if (t.Length <= prefix.Length) continue;
                if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!char.IsWhiteSpace(t[prefix.Length])) continue;
                message = t.Substring(prefix.Length).Trim();
                return message.Length > 0;
            }
            return false;
        }

        internal static bool TryParseWhisper(string raw, out string target, out string message)
        {
            return TryParseTargeted(raw, new string[] { "/whisper", "/tell", "/w", "/t" }, out target, out message);
        }

        internal static bool TryParseForceAi(string raw, out string target, out string message)
        {
            return TryParseTargeted(raw, new string[] { "/dwhisper", "/dw", "/dsay" }, out target, out message);
        }

        internal static bool TryParseForceVanilla(string raw, out string target, out string message)
        {
            return TryParseTargeted(raw, new string[] { "/vwhisper", "/vw" }, out target, out message);
        }

        internal static bool IsStatus(string raw)
        {
            string t = Trim(raw);
            return Eq(t, "/aistatus") || Eq(t, "/dsstatus");
        }

        internal static bool IsSessionStatus(string raw)
        {
            string t = Trim(raw);
            return Eq(t, "/dssession") || Eq(t, "/dsouting");
        }

        internal static bool TryParseSession(string raw, out string argument)
        {
            return TryParseSimpleArgument(raw, "/dssession", out argument);
        }

        internal static bool TryParseThread(string raw, out string argument)
        {
            return TryParseSimpleArgument(raw, "/dsthread", out argument);
        }

        internal static bool IsPerfStatus(string raw)
        {
            string t = Trim(raw);
            return Eq(t, "/dsperf") || Eq(t, "/dsperformance");
        }

        internal static bool TryParseEventSettings(string raw, out string argument)
        {
            argument = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            const string prefix = "/dsevents";
            if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (t.Length == prefix.Length) { argument = string.Empty; return true; }
            if (!char.IsWhiteSpace(t[prefix.Length])) return false;
            argument = t.Substring(prefix.Length).Trim();
            return true;
        }

        internal static bool TryParseSeeds(string raw, out string argument)
        {
            argument = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            const string prefix = "/dsseeds";
            if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (t.Length == prefix.Length) { argument = string.Empty; return true; }
            if (!char.IsWhiteSpace(t[prefix.Length])) return false;
            argument = t.Substring(prefix.Length).Trim();
            return true;
        }

        // Developer diagnostic only: /dspromptcapture [on|off|status|mark <label>].
        internal static bool TryParsePromptCapture(string raw, out string argument)
        {
            return TryParseSimpleArgument(raw, "/dspromptcapture", out argument);
        }

        internal static bool TryParseCamp(string raw, out string argument)
        {
            argument = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            const string prefix = "/dscamp";
            if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (t.Length == prefix.Length) { argument = string.Empty; return true; }
            if (!char.IsWhiteSpace(t[prefix.Length])) return false;
            argument = t.Substring(prefix.Length).Trim();
            return true;
        }

        private static bool TryParseSimpleArgument(string raw, string prefix, out string argument)
        {
            argument = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (t.Length == prefix.Length) { argument = string.Empty; return true; }
            if (!char.IsWhiteSpace(t[prefix.Length])) return false;
            argument = t.Substring(prefix.Length).Trim();
            return true;
        }
        internal static bool TryParseSocial(string raw, out string argument)
        {
            argument = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            const string prefix = "/dssocial";
            if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (t.Length == prefix.Length) { argument = string.Empty; return true; }
            if (!char.IsWhiteSpace(t[prefix.Length])) return false;
            argument = t.Substring(prefix.Length).Trim();
            return true;
        }

        internal static bool TryParseRoleplay(string raw, out string argument)
        {
            argument = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            const string prefix = "/dsroleplay";
            if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (t.Length == prefix.Length) { argument = string.Empty; return true; }
            if (!char.IsWhiteSpace(t[prefix.Length])) return false;
            argument = t.Substring(prefix.Length).Trim();
            return true;
        }

        internal static bool TryParseInferenceMode(string raw, out string argument)
        {
            argument = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            string[] prefixes = new string[] { "/dsinference", "/dsmode" };
            for (int i = 0; i < prefixes.Length; i++)
            {
                string prefix = prefixes[i];
                if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (t.Length == prefix.Length) { argument = string.Empty; return true; }
                if (!char.IsWhiteSpace(t[prefix.Length])) continue;
                argument = t.Substring(prefix.Length).Trim();
                return true;
            }
            return false;
        }

        internal static bool TryParseReasoningMode(string raw, out string argument)
        {
            argument = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            string[] prefixes = new string[] { "/dsreasoning", "/dsthinking" };
            for (int i = 0; i < prefixes.Length; i++)
            {
                string prefix = prefixes[i];
                if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (t.Length == prefix.Length) { argument = string.Empty; return true; }
                if (!char.IsWhiteSpace(t[prefix.Length])) continue;
                argument = t.Substring(prefix.Length).Trim();
                return true;
            }
            return false;
        }

        internal static bool TryParseMemoryInspect(string raw, out string target)
        {
            target = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            const string prefix = "/dsmemory";
            if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (t.Length == prefix.Length) { target = string.Empty; return true; }
            if (!char.IsWhiteSpace(t[prefix.Length])) return false;
            target = t.Substring(prefix.Length).Trim();
            return true;
        }

        internal static bool TryParseIdentity(string raw, out string argument)
        {
            return TryParseSimpleArgument(raw, "/dsidentity", out argument);
        }

        internal static bool TryParseExport(string raw, out string argument)
        {
            argument = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            const string prefix = "/dsexport";
            if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (t.Length == prefix.Length) { argument = string.Empty; return true; }
            if (!char.IsWhiteSpace(t[prefix.Length])) return false;
            argument = t.Substring(prefix.Length).Trim();
            return true;
        }

        internal static bool IsAiTest(string raw)
        {
            string t = Trim(raw);
            return Eq(t, "/aitest") || Eq(t, "/dstest");
        }

        internal static bool TryParseAiModel(string raw, out string model)
        {
            model = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            const string prefix = "/aimodel";
            if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (t.Length == prefix.Length) { model = string.Empty; return true; }
            if (t.Length > prefix.Length && !char.IsWhiteSpace(t[prefix.Length])) return false;
            model = t.Substring(prefix.Length).Trim();
            return true;
        }



        internal static bool TryParseNews(string raw, out string argument)
        {
            argument = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            const string prefix = "/dsnews";
            if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (t.Length == prefix.Length) { argument = "latest update"; return true; }
            if (!char.IsWhiteSpace(t[prefix.Length])) return false;
            argument = t.Substring(prefix.Length).Trim();
            if (argument.Length == 0) argument = "latest update";
            return true;
        }

        internal static bool TryParseWiki(string raw, out string argument)
        {
            argument = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            const string prefix = "/dswiki";
            if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (t.Length == prefix.Length) { argument = string.Empty; return true; }
            if (!char.IsWhiteSpace(t[prefix.Length])) return false;
            argument = t.Substring(prefix.Length).Trim();
            return true;
        }


        internal static bool TryParseExternalNews(string raw, out string argument)
        {
            argument = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            const string prefix = "/dsxnews";
            if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (t.Length == prefix.Length) { argument = string.Empty; return true; }
            if (!char.IsWhiteSpace(t[prefix.Length])) return false;
            argument = t.Substring(prefix.Length).Trim();
            return true;
        }

        internal static bool IsNewsSources(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return string.Equals(raw.Trim(), "/dsnewsources", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryParseDirector(string raw, out string argument)
        {
            argument = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            const string prefix = "/dsdirector";
            if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (t.Length == prefix.Length) { argument = string.Empty; return true; }
            if (!char.IsWhiteSpace(t[prefix.Length])) return false;
            argument = t.Substring(prefix.Length).Trim();
            return true;
        }

        internal static bool TryParseTalk(string raw, out string speaker)
        {
            speaker = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            const string prefix = "/dstalk";
            if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (t.Length == prefix.Length) { speaker = string.Empty; return true; }
            if (!char.IsWhiteSpace(t[prefix.Length])) return false;
            speaker = t.Substring(prefix.Length).Trim();
            return true;
        }

        internal static bool IsBanter(string raw)
        {
            string t = Trim(raw);
            return Eq(t, "/dsbanter") || Eq(t, "/dsconversation");
        }

        internal static bool IsList(string raw)
        {
            string t = Trim(raw);
            return Eq(t, "/dsims") || Eq(t, "/deepsims");
        }

        internal static bool IsInspect(string raw)
        {
            string t = Trim(raw);
            return Eq(t, "/dsinspect") || Eq(t, "/dsdump");
        }

        internal static bool IsGuardTest(string raw)
        {
            string t = Trim(raw);
            return Eq(t, "/dsguardtest") || Eq(t, "/dstestguard");
        }

        internal static bool IsRefresh(string raw)
        {
            string t = Trim(raw);
            return Eq(t, "/dsrefresh");
        }

        internal static bool TryParseManualSlots(string raw, out string slots)
        {
            slots = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            const string prefix = "/dslots";
            if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (t.Length == prefix.Length) { slots = string.Empty; return true; }
            if (!char.IsWhiteSpace(t[prefix.Length])) return false;
            slots = t.Substring(prefix.Length).Trim();
            return true;
        }

        internal static bool TryParseForget(string raw, out string target)
        {
            target = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string t = raw.Trim();
            const string prefix = "/dsforget";
            if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (t.Length <= prefix.Length) return false;
            target = t.Substring(prefix.Length).Trim();
            return target.Length > 0;
        }

        private static bool TryParseTargeted(string raw, string[] commands, out string target, out string message)
        {
            target = null;
            message = null;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string trimmed = raw.Trim();
            string matched = null;
            for (int i = 0; i < commands.Length; i++)
            {
                string command = commands[i];
                if (trimmed.Equals(command, StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith(command + " ", StringComparison.OrdinalIgnoreCase))
                {
                    matched = command;
                    break;
                }
            }
            if (matched == null || trimmed.Length <= matched.Length) return false;
            string rest = trimmed.Substring(matched.Length).Trim();
            int firstSpace = rest.IndexOf(' ');
            if (firstSpace <= 0 || firstSpace >= rest.Length - 1) return false;
            target = rest.Substring(0, firstSpace).Trim();
            message = rest.Substring(firstSpace + 1).Trim();
            return target.Length > 0 && message.Length > 0;
        }

        private static string Trim(string value) { return value == null ? string.Empty : value.Trim(); }
        private static bool Eq(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }


    internal static class VanillaGroupCommandClassifier
    {
        // Follow-owned direct travel requests are gameplay intent, not social prompts. The grammar is
        // intentionally no broader than Follow's own deterministic parser so ordinary questions remain chat.
        internal static bool IsExternalGameplayControlIntent(string message)
        {
            string leader;
            string destination;
            return TravelCommandGrammar.TryParseLeadRequest(message, out leader, out destination);
        }

        // Erenshor uses /group both as chat and as a keyword-driven tactical command channel.
        // Deep Sims should keep normal conversation away from that parser, but obvious imperative
        // group orders still belong to vanilla so they can change actual party behavior.
        internal static bool ShouldLetVanillaHandle(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return true;
            if (IsExternalGameplayControlIntent(message)) return true;
            string m = Normalize(message);
            if (m.Length == 0) return true;

            // Strip a few harmless address/politeness words so "/p guys follow me" still behaves
            // like the vanilla Follow command without treating "what should we do next?" as an order.
            m = StripLeading(m, new string[] { "please ", "pls ", "guys ", "everyone ", "everybody ", "team ", "group ", "party ", "hey " });

            string[] phrases = new string[] { "stop pulling", "hold pulls", "no pulls" };
            for (int i = 0; i < phrases.Length; i++)
                if (m == phrases[i] || m.StartsWith(phrases[i] + " ", StringComparison.Ordinal)) return true;

            string first = m;
            int space = first.IndexOf(' ');
            if (space >= 0) first = first.Substring(0, space);

            switch (first)
            {
                case "attack":
                case "assist":
                case "target":
                case "kill":
                case "fight":
                case "pull":
                case "autopull":
                case "grab":
                case "wait":
                case "guard":
                case "stay":
                case "follow":
                case "come":
                case "run":
                case "flee":
                case "escape":
                case "careful":
                case "cautious":
                case "aggressive":
                case "burn":
                case "mana":
                case "loc":
                    return true;
                case "where":
                    // Preserve the game's explicit location request without stealing broader social
                    // questions such as "where should we go next?".
                    return m == "where" || m.StartsWith("where are you", StringComparison.Ordinal) || m.StartsWith("where is everyone", StringComparison.Ordinal);
                default:
                    return false;
            }
        }

        private static string StripLeading(string value, string[] prefixes)
        {
            string current = value;
            bool changed;
            do
            {
                changed = false;
                for (int i = 0; i < prefixes.Length; i++)
                {
                    if (current.StartsWith(prefixes[i], StringComparison.Ordinal))
                    {
                        current = current.Substring(prefixes[i].Length).TrimStart();
                        changed = true;
                        break;
                    }
                }
            } while (changed && current.Length > 0);
            return current;
        }

        private static string Normalize(string value)
        {
            string lower = value.Trim().ToLowerInvariant();
            char[] chars = lower.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)) chars[i] = ' ';
            }
            string normalized = new string(chars);
            while (normalized.Contains("  ")) normalized = normalized.Replace("  ", " ");
            return normalized.Trim();
        }
    }

    internal static class VanillaWhisperClassifier
    {
        internal static bool ShouldLetVanillaHandle(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return true;
            string m = " " + message.Trim().ToLowerInvariant() + " ";
            string bare = message.Trim().ToLowerInvariant();

            // Keep action-like and live-state intents in vanilla. Knowledge questions are intentionally
            // NOT claimed here in 0.3 so DeepSims can answer them with optional wiki grounding.
            if (ContainsAny(m, new string[] { " group ", " invite ", " lfg ", " party ", " join me ", " join us ", " guild ", " help ", " invis ", " location ", " where are you ", " what level ", " your level ", " lvl ", " thanks ", " thank you ", " thx ", " sorry ", " whats up ", " what's up ", " goodnight ", " good night " })) return true;

            if (bare == "hi" || bare == "hey" || bare == "hello" || bare == "hail" || bare == "sup" || bare == "yo" || bare == "ty" || bare == "gg" || bare == "gn") return true;
            if (bare.StartsWith("yes") || bare == "yep" || bare == "yeah" || bare == "no" || bare == "nope") return true;
            return false;
        }

        private static bool ContainsAny(string text, string[] needles)
        {
            for (int i = 0; i < needles.Length; i++) if (text.Contains(needles[i])) return true;
            return false;
        }
    }
}
