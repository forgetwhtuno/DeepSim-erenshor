using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ErenshorDeepSims
{
    internal sealed class ConversationEvidenceDecision
    {
        internal bool IsNewCanonical;
        internal bool IsRepeat;
        internal int RecurrenceCount;
        internal string CanonicalText = string.Empty;
        internal string Speaker = string.Empty;
    }

    // Deduplicates prompt/memory evidence without hiding the social fact that repetition happened.
    // Native chat remains untouched; this only controls what Deep Sims carries forward internally.
    internal sealed class ConversationEvidenceDedupe
    {
        private sealed class Entry
        {
            internal string Speaker;
            internal string Text;
            internal string Normalized;
            internal DateTime LastUtc;
            internal int Count;
        }

        private readonly object _gate = new object();
        private readonly List<Entry> _recent = new List<Entry>();
        private readonly double _windowSeconds;
        private readonly int _maxEntries;
        private int _dedupeCount;

        internal ConversationEvidenceDedupe(double windowSeconds, int maxEntries)
        {
            _windowSeconds = Math.Max(2.0, windowSeconds);
            _maxEntries = Math.Max(4, maxEntries);
        }

        internal ConversationEvidenceDecision Observe(string speaker, string text, DateTime nowUtc)
        {
            ConversationEvidenceDecision result = new ConversationEvidenceDecision();
            result.Speaker = string.IsNullOrWhiteSpace(speaker) ? "Party" : speaker.Trim();
            result.CanonicalText = (text ?? string.Empty).Trim();
            result.RecurrenceCount = 1;
            if (result.CanonicalText.Length == 0) return result;
            string normalized = Normalize(result.CanonicalText);
            lock (_gate)
            {
                Prune(nowUtc);
                for (int i = _recent.Count - 1; i >= 0; i--)
                {
                    Entry e = _recent[i];
                    if (!string.Equals(e.Speaker, result.Speaker, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!Near(normalized, e.Normalized)) continue;
                    e.Count = Math.Min(1000, e.Count + 1);
                    e.LastUtc = nowUtc;
                    result.IsRepeat = true;
                    result.IsNewCanonical = false;
                    result.RecurrenceCount = e.Count;
                    result.CanonicalText = e.Text;
                    _dedupeCount++;
                    return result;
                }
                _recent.Add(new Entry { Speaker = result.Speaker, Text = result.CanonicalText, Normalized = normalized, LastUtc = nowUtc, Count = 1 });
                while (_recent.Count > _maxEntries) _recent.RemoveAt(0);
                result.IsNewCanonical = true;
                return result;
            }
        }

        internal int DedupeCount { get { lock (_gate) return _dedupeCount; } }
        internal void Clear() { lock (_gate) { _recent.Clear(); _dedupeCount = 0; } }

        internal static string Normalize(string value)
        {
            string clean = Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9']+", " ").Trim();
            return Regex.Replace(clean, @"\s+", " ");
        }

        internal static bool Near(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.Ordinal)) return true;
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            string[] aa = a.Split(' '), bb = b.Split(' ');
            if (Math.Abs(aa.Length - bb.Length) > 2) return false;
            HashSet<string> left = new HashSet<string>(aa, StringComparer.Ordinal);
            HashSet<string> right = new HashSet<string>(bb, StringComparer.Ordinal);
            int overlap = 0; foreach (string token in left) if (right.Contains(token)) overlap++;
            int union = left.Count + right.Count - overlap;
            return union > 0 && ((double)overlap / union) >= .80;
        }

        private void Prune(DateTime nowUtc)
        {
            for (int i = _recent.Count - 1; i >= 0; i--)
                if ((nowUtc - _recent[i].LastUtc).TotalSeconds > _windowSeconds) _recent.RemoveAt(i);
        }
    }
}
