using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ErenshorDeepSims
{
    // A deliberately narrow compatibility layer for model control replies.  This is not natural
    // language interpretation: only a complete, normalized reply in this fixed set is silence.
    internal static class DialogueControlSentinel
    {
        private static readonly HashSet<string> WholeResponseAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NO MESSAGE", "NO MESSAGE NEEDED", "NO REPLY", "NO REPLY NEEDED",
            "NO RESPONSE", "NO RESPONSE NEEDED", "SILENCE"
        };

        internal static bool IsNoMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            string normalized = Normalize(text);
            return WholeResponseAliases.Contains(normalized);
        }

        internal static string Normalize(string text)
        {
            string value = (text ?? string.Empty).Trim();
            value = StripBalancedWrapper(value, '"', '"');
            value = StripBalancedWrapper(value, '\'', '\'');
            value = StripBalancedWrapper(value, '`', '`');
            value = StripBalancedWrapper(value, '[', ']');
            value = StripBalancedWrapper(value, '<', '>');
            value = value.Trim().TrimEnd('.', '!', '?', ':', ';').Trim();
            value = value.Replace('_', ' ');
            return Regex.Replace(value, @"\s+", " ").Trim();
        }

        private static string StripBalancedWrapper(string value, char open, char close)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 2 || value[0] != open || value[value.Length - 1] != close) return value;
            return value.Substring(1, value.Length - 2).Trim();
        }
    }

    // Lightweight, content-free request identity. It is independent of optional prompt capture
    // and flows through async dialogue work so retries remain part of the same logical request.
    internal sealed class DialogueRequestCorrelation
    {
        private static long _nextRequestId;
        private static readonly AsyncLocal<DialogueRequestCorrelation> _current = new AsyncLocal<DialogueRequestCorrelation>();

        internal long RequestId;
        internal int Attempt;
        internal string RequestType;
        internal string Speaker;
        internal long ThreadId;
        internal string CandidateHash;
        internal string Disposition;

        internal static DialogueRequestCorrelation Current { get { return _current.Value; } }

        internal static DialogueRequestCorrelation Ensure(string requestType, string speaker, long threadId)
        {
            DialogueRequestCorrelation current = _current.Value;
            if (current != null) return current;
            current = new DialogueRequestCorrelation();
            current.RequestId = Interlocked.Increment(ref _nextRequestId);
            current.Attempt = 0;
            current.RequestType = string.IsNullOrWhiteSpace(requestType) ? "generation" : requestType;
            current.Speaker = speaker ?? string.Empty;
            current.ThreadId = threadId;
            current.Disposition = "generated";
            _current.Value = current;
            return current;
        }

        internal static DialogueRequestCorrelation Start(string requestType, string speaker, long threadId)
        {
            DialogueRequestCorrelation current = new DialogueRequestCorrelation();
            current.RequestId = Interlocked.Increment(ref _nextRequestId);
            current.Attempt = 0;
            current.RequestType = string.IsNullOrWhiteSpace(requestType) ? "generation" : requestType;
            current.Speaker = speaker ?? string.Empty;
            current.ThreadId = threadId;
            current.Disposition = "generated";
            _current.Value = current;
            return current;
        }

        internal static void MarkCandidate(string text, string disposition)
        {
            DialogueRequestCorrelation current = _current.Value;
            if (current == null) return;
            current.CandidateHash = Fingerprint(text);
            current.Disposition = string.IsNullOrWhiteSpace(disposition) ? current.Disposition : disposition;
        }

        internal static void MarkDisposition(string disposition)
        {
            DialogueRequestCorrelation current = _current.Value;
            if (current != null && !string.IsNullOrWhiteSpace(disposition)) current.Disposition = disposition;
        }

        internal static void NextGroundingRetry()
        {
            DialogueRequestCorrelation current = _current.Value;
            if (current == null) return;
            current.Attempt++;
            current.Disposition = "grounding_retry";
        }

        internal static string Fingerprint(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                string text = value ?? string.Empty;
                for (int i = 0; i < text.Length; i++) { hash ^= text[i]; hash *= 16777619; }
                return hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        internal static string Describe()
        {
            DialogueRequestCorrelation current = _current.Value;
            if (current == null) return "requestId=0 attempt=0 threadId=0 requestType=none speaker=? candidateHash=none disposition=unknown";
            return "requestId=" + current.RequestId + " attempt=" + current.Attempt +
                " threadId=" + current.ThreadId + " requestType=" + (current.RequestType ?? "unknown") +
                " speaker=" + (string.IsNullOrWhiteSpace(current.Speaker) ? "?" : current.Speaker) +
                " candidateHash=" + (string.IsNullOrWhiteSpace(current.CandidateHash) ? "none" : current.CandidateHash) +
                " disposition=" + (current.Disposition ?? "unknown");
        }

        internal static void ResetForTests()
        {
            Interlocked.Exchange(ref _nextRequestId, 0);
            _current.Value = null;
        }
    }

    internal static class DialogueControlDeterministicTests
    {
        internal static List<string> Run()
        {
            List<string> lines = new List<string>();
            DialogueRequestCorrelation.ResetForTests();
            string[] suppressed = { "NO_MESSAGE", "no_message", "NO MESSAGE", "\"NO_MESSAGE\"", "NO_MESSAGE.", "`NO_MESSAGE`", "NO_MESSAGE_NEEDED", "no message needed", "\"No message needed.\"", "[NO_MESSAGE_NEEDED]", "NO_REPLY", "NO REPLY", "no reply needed", "NO_RESPONSE", "no response needed", "SILENCE", "\"silence.\"" };
            for (int i = 0; i < suppressed.Length; i++) Add(lines, "sentinel suppresses " + suppressed[i], DialogueControlSentinel.IsNoMessage(suppressed[i]));
            string[] visible = { "There was no response from Farrowick.", "No idea.", "Nothing to add? I have plenty.", "I don't need a reply.", "Let's stay silent while we sneak past.", "No message from the guild yet." };
            for (int i = 0; i < visible.Length; i++) Add(lines, "sentinel preserves dialogue " + (i + 1), !DialogueControlSentinel.IsNoMessage(visible[i]));
            DialogueRequestCorrelation first = DialogueRequestCorrelation.Ensure("test", "A", 1);
            long firstId = first.RequestId; DialogueRequestCorrelation.MarkCandidate("first", "generated");
            DialogueRequestCorrelation.NextGroundingRetry();
            Add(lines, "correlation root is nonzero and retry preserves root", firstId > 0 && DialogueRequestCorrelation.Current.RequestId == firstId && DialogueRequestCorrelation.Current.Attempt == 1);
            Add(lines, "correlation candidate hash is present", !string.IsNullOrWhiteSpace(DialogueRequestCorrelation.Current.CandidateHash));
            DialogueRequestCorrelation.Start("test", "B", 2);
            Add(lines, "sequential logical requests receive distinct roots", DialogueRequestCorrelation.Current.RequestId != firstId);
            return lines;
        }

        private static void Add(List<string> lines, string name, bool pass) { lines.Add("[DeepSims DialogueControl " + (pass ? "PASS" : "FAIL") + "] " + name); }
    }
}
