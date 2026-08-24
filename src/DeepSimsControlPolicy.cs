using System;
using System.Collections.Generic;
using System.Text;

namespace ErenshorDeepSims
{
    // Unity-free validation plus the canonical Suite Hub wire values for Deep Sims.
    // Keep choice option strings and normalizers together so the descriptor cannot drift into
    // enum-ToString casing mismatches (notably Llm vs the stored/public wire value LLM).
    internal static class DeepSimsControlPolicy
    {
        internal const string SocialModeOptions = "Auto,LLM,Templates,Off";
        internal const string ActivityOptions = "Adaptive,Quiet,Normal,Lively";
        internal const string PerspectiveOptions = "MMO,Roleplay";
        internal const string InferenceModeOptions = "Auto,CPU,GPU";
        internal const string ReasoningModeOptions = "Off,Selective,Always";

        internal static bool TryNormalizeSocialMode(string value, out string normalized)
        {
            string v = Clean(value);
            if (v == "auto") { normalized = "Auto"; return true; }
            if (v == "llm") { normalized = "LLM"; return true; }
            if (v == "templates" || v == "template") { normalized = "Templates"; return true; }
            if (v == "off") { normalized = "Off"; return true; }
            normalized = null;
            return false;
        }

        internal static bool TryNormalizeActivity(string value, out string normalized)
        {
            string v = Clean(value);
            if (v == "adaptive") { normalized = "Adaptive"; return true; }
            if (v == "quiet") { normalized = "Quiet"; return true; }
            if (v == "normal") { normalized = "Normal"; return true; }
            if (v == "lively") { normalized = "Lively"; return true; }
            normalized = null;
            return false;
        }

        internal static bool TryNormalizePerspective(string value, out string normalized)
        {
            string v = Clean(value);
            if (v == "mmo" || v == "player" || v == "off") { normalized = "MMO"; return true; }
            if (v == "roleplay" || v == "rp" || v == "in-world" || v == "inworld")
            {
                normalized = "Roleplay";
                return true;
            }
            normalized = null;
            return false;
        }

        internal static bool TryNormalizeInferenceMode(string value, out string normalized)
        {
            string v = Clean(value);
            if (v == "auto") { normalized = "Auto"; return true; }
            if (v == "cpu") { normalized = "CPU"; return true; }
            if (v == "gpu") { normalized = "GPU"; return true; }
            normalized = null;
            return false;
        }

        internal static bool TryNormalizeReasoningMode(string value, out string normalized)
        {
            string v = Clean(value);
            if (v == "off") { normalized = "Off"; return true; }
            if (v == "selective") { normalized = "Selective"; return true; }
            if (v == "always" || v == "on") { normalized = "Always"; return true; }
            normalized = null;
            return false;
        }

        internal static bool TryParseWireBool(string value, out bool parsed)
        {
            if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) { parsed = true; return true; }
            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) { parsed = false; return true; }
            parsed = false;
            return false;
        }

        internal static string SafeResponseStatusCategory(string value)
        {
            string v = Clean(value);
            if (v == "idle") return "idle";
            if (v == "queued") return "queued";
            if (v == "lookup") return "lookup";
            if (v == "generating") return "generating";
            if (v == "rejected") return "rejected";
            if (v == "unavailable") return "unavailable";
            if (v == "error") return "error";
            if (v == "suppressed") return "suppressed";
            if (v == "displayed") return "displayed";
            if (v == "cooldown") return "cooldown";
            if (v == "request-active") return "request-active";
            // Future internal diagnostic strings must not automatically become cross-mod status.
            return "busy";
        }

        internal static string SafePublicModelLabel(string value)
        {
            string model = (value ?? string.Empty).Trim();
            if (model.Length == 0) return string.Empty;
            // Ollama model tags commonly contain ':' (for example qwen3.5:4b), so colon alone is
            // not path evidence. Directory separators are: do not send a user/profile path across
            // the public control surface if a custom runner value was configured.
            if (model.IndexOf('\\') >= 0 || model.IndexOf('/') >= 0) return "custom";
            if (model.Length > 80) model = model.Substring(0, 80);
            return model;
        }

        internal static string SocialModeOrDefault(string value)
        {
            string normalized;
            return TryNormalizeSocialMode(value, out normalized) ? normalized : "Auto";
        }

        internal static string ActivityOrDefault(string value)
        {
            string normalized;
            return TryNormalizeActivity(value, out normalized) ? normalized : "Lively";
        }

        internal static string PerspectiveOrDefault(string value)
        {
            string normalized;
            return TryNormalizePerspective(value, out normalized) ? normalized : "MMO";
        }

        internal static string InferenceModeOrDefault(string value)
        {
            string normalized;
            return TryNormalizeInferenceMode(value, out normalized) ? normalized : "Auto";
        }

        internal static string ReasoningModeOrDefault(string value)
        {
            string normalized;
            return TryNormalizeReasoningMode(value, out normalized) ? normalized : "Selective";
        }

        internal static bool ChoiceContains(string optionsCsv, string value)
        {
            if (string.IsNullOrEmpty(optionsCsv) || value == null) return false;
            string[] values = optionsCsv.Split(',');
            for (int i = 0; i < values.Length; i++)
                if (string.Equals(values[i], value, StringComparison.Ordinal)) return true;
            return false;
        }

        // Canonical mutation gate shared by Hub setting.set and deterministic tests. A descriptor
        // id cannot become mutable merely because a caller guessed an internal config key.
        internal static bool TryNormalizeSettingValue(string settingId, string value, out string normalized)
        {
            string id = settingId == null ? string.Empty : settingId.Trim();
            if (string.Equals(id, "socialMode", StringComparison.OrdinalIgnoreCase))
                return TryNormalizeSocialMode(value, out normalized);
            if (string.Equals(id, "activity", StringComparison.OrdinalIgnoreCase))
                return TryNormalizeActivity(value, out normalized);
            if (string.Equals(id, "perspective", StringComparison.OrdinalIgnoreCase))
                return TryNormalizePerspective(value, out normalized);
            if (string.Equals(id, "inferenceMode", StringComparison.OrdinalIgnoreCase))
                return TryNormalizeInferenceMode(value, out normalized);
            if (string.Equals(id, "reasoningMode", StringComparison.OrdinalIgnoreCase))
                return TryNormalizeReasoningMode(value, out normalized);

            if (IsBoolSetting(id))
            {
                bool parsed;
                if (TryParseWireBool(value, out parsed))
                {
                    normalized = parsed ? "true" : "false";
                    return true;
                }
            }

            normalized = null;
            return false;
        }

        internal static bool IsBoolSetting(string settingId)
        {
            string id = settingId ?? string.Empty;
            string[] known = {
                "autonomousSocial", "partyChatResponses", "wholeParty", "eventChatter",
                "idleChatter", "simToSim", "conversationThreads", "conversationSeeding",
                "hybridWhispers", "vanillaTyping", "wikiLookup", "officialNews",
                "externalNews", "externalNewsAuto", "pauseAutonomousCombat",
                "campmasterIntegration", "verboseLogging", "seedDiagnostics"
            };
            for (int i = 0; i < known.Length; i++)
                if (string.Equals(id, known[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }
    }

    // Pure wire builder used directly by the Aura provider and deterministic tests. It accepts only
    // the already-sanitized ControlApi settings snapshot; secrets/endpoints/raw memories never enter
    // this type, which makes accidental Hub exposure structurally difficult.
    internal static class DeepSimsSuiteDescriptorPolicy
    {
        internal const int MaxHubText = 200;

        internal static string BuildDescribe(string version, string status)
        {
            return "protocol=1"
                + "&module=deepsims"
                + "&display=" + Escape("Erenshor Deep Sims")
                + "&version=" + Escape(Bound(version, 32))
                + "&summary=" + Escape("Grounded local-AI party social layer")
                + "&status=" + Escape(Bound(status, MaxHubText))
                + "&actions=openPanel,closePanel,refreshStatus";
        }

        internal static string BuildBasicSettings(IDictionary<string, string> values)
        {
            StringBuilder sb = new StringBuilder();
            AppendChoice(sb, "perspective", "Perspective",
                DeepSimsControlPolicy.PerspectiveOrDefault(Get(values, "perspective", "MMO")),
                DeepSimsControlPolicy.PerspectiveOptions, "basic");
            AppendChoice(sb, "socialMode", "Social expression",
                DeepSimsControlPolicy.SocialModeOrDefault(Get(values, "socialMode", "Auto")),
                DeepSimsControlPolicy.SocialModeOptions, "basic");
            AppendChoice(sb, "activity", "Social activity",
                DeepSimsControlPolicy.ActivityOrDefault(Get(values, "activity", "Adaptive")),
                DeepSimsControlPolicy.ActivityOptions, "basic");
            AppendBool(sb, "autonomousSocial", "Autonomous social chatter", GetBool(values, "autonomousSocial", true), "basic");
            AppendBool(sb, "partyChatResponses", "Reply to party chat", GetBool(values, "partyChatResponses", true), "basic");
            return sb.ToString();
        }

        internal static string BuildAdvancedSettings(IDictionary<string, string> values)
        {
            StringBuilder sb = new StringBuilder();
            AppendBool(sb, "wholeParty", "Enhance whole normal party", GetBool(values, "wholeParty", true), "advanced");
            AppendBool(sb, "eventChatter", "Observed-event reactions", GetBool(values, "eventChatter", true), "advanced");
            AppendBool(sb, "idleChatter", "Idle chatter", GetBool(values, "idleChatter", true), "advanced");
            AppendBool(sb, "simToSim", "Sim-to-Sim replies", GetBool(values, "simToSim", true), "advanced");
            AppendBool(sb, "conversationThreads", "Conversation threads", GetBool(values, "conversationThreads", true), "advanced");
            AppendBool(sb, "conversationSeeding", "Grounded conversation seeding", GetBool(values, "conversationSeeding", true), "advanced");
            AppendBool(sb, "hybridWhispers", "Hybrid vanilla/Deep Sim whispers", GetBool(values, "hybridWhispers", true), "advanced");
            AppendBool(sb, "vanillaTyping", "Apply vanilla Sim typing style", GetBool(values, "vanillaTyping", true), "advanced");
            AppendBool(sb, "wikiLookup", "Erenshor wiki lookup", GetBool(values, "wikiLookup", true), "advanced");
            AppendBool(sb, "officialNews", "Official Erenshor news lookup", GetBool(values, "officialNews", true), "advanced");
            AppendBool(sb, "externalNews", "External real-world news lookup", GetBool(values, "externalNews", true), "advanced");
            AppendBool(sb, "externalNewsAuto", "Automatic external-news lookup", GetBool(values, "externalNewsAuto", true), "advanced");
            AppendBool(sb, "pauseAutonomousCombat", "Pause autonomous AI in combat", GetBool(values, "pauseAutonomousCombat", true), "advanced");
            AppendBool(sb, "campmasterIntegration", "Use Campmaster context when available", GetBool(values, "campmasterIntegration", true), "advanced");
            AppendChoice(sb, "inferenceMode", "Inference device",
                DeepSimsControlPolicy.InferenceModeOrDefault(Get(values, "inferenceMode", "Auto")),
                DeepSimsControlPolicy.InferenceModeOptions, "advanced");
            AppendChoice(sb, "reasoningMode", "Reasoning model routing",
                DeepSimsControlPolicy.ReasoningModeOrDefault(Get(values, "reasoningMode", "Selective")),
                DeepSimsControlPolicy.ReasoningModeOptions, "advanced");
            return sb.ToString();
        }

        internal static string BuildDeveloperSettings(IDictionary<string, string> values)
        {
            StringBuilder sb = new StringBuilder();
            AppendBool(sb, "verboseLogging", "Verbose routing diagnostics", GetBool(values, "verboseLogging", false), "developer");
            AppendBool(sb, "seedDiagnostics", "Conversation seed score diagnostics", GetBool(values, "seedDiagnostics", true), "developer");
            return sb.ToString();
        }

        internal static bool AllChoiceValuesAdvertised(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return true;
            string[] lines = payload.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf("&type=choice", StringComparison.Ordinal) < 0) continue;
                Dictionary<string, string> fields = ParseFields(lines[i]);
                string value, options;
                if (!fields.TryGetValue("value", out value) || !fields.TryGetValue("options", out options)) return false;
                if (!DeepSimsControlPolicy.ChoiceContains(options, value)) return false;
            }
            return true;
        }

        internal static bool ContainsSensitiveFieldName(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return false;
            string lower = Uri.UnescapeDataString(payload).ToLowerInvariant();
            string[] forbidden = {
                "apikey", "api key", "endpoint", "filesystem", "filepath", "memorycontent",
                "rawmemory", "conversationhistory", "rawconversation", "prompt", "windows username"
            };
            for (int i = 0; i < forbidden.Length; i++)
                if (lower.IndexOf(forbidden[i], StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static void AppendBool(StringBuilder sb, string id, string label, bool value, string tier)
        {
            AppendSeparator(sb);
            sb.Append("id=").Append(id)
              .Append("&label=").Append(Escape(label))
              .Append("&tier=").Append(tier)
              .Append("&type=bool&value=").Append(value ? "true" : "false")
              .Append("&mutable=true");
        }

        private static void AppendChoice(StringBuilder sb, string id, string label, string value, string options, string tier)
        {
            AppendSeparator(sb);
            sb.Append("id=").Append(id)
              .Append("&label=").Append(Escape(label))
              .Append("&tier=").Append(tier)
              .Append("&type=choice&value=").Append(Escape(value))
              .Append("&mutable=true&options=").Append(Escape(options));
        }

        private static void AppendSeparator(StringBuilder sb)
        {
            if (sb.Length > 0) sb.Append('\n');
        }

        private static string Get(IDictionary<string, string> values, string key, string fallback)
        {
            string value;
            return values != null && values.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
        }

        private static bool GetBool(IDictionary<string, string> values, string key, bool fallback)
        {
            string value;
            bool parsed;
            return values != null && values.TryGetValue(key, out value) &&
                   DeepSimsControlPolicy.TryParseWireBool(value, out parsed) ? parsed : fallback;
        }

        private static string Bound(string value, int max)
        {
            string safe = value ?? string.Empty;
            return safe.Length <= max ? safe : safe.Substring(0, max);
        }

        private static string Escape(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty);
        }

        private static Dictionary<string, string> ParseFields(string line)
        {
            Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] pairs = (line ?? string.Empty).Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                int eq = pairs[i].IndexOf('=');
                if (eq <= 0) continue;
                string key = Uri.UnescapeDataString(pairs[i].Substring(0, eq));
                string value = Uri.UnescapeDataString(pairs[i].Substring(eq + 1));
                fields[key] = value;
            }
            return fields;
        }
    }
}
