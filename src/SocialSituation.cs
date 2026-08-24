using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ErenshorDeepSims
{
    internal enum SocialActivityState
    {
        Unknown = 0,
        Combat = 1,
        Travel = 2,
        ActiveGameplay = 3,
        SocialDowntime = 4,
        ExtendedDowntime = 5
    }

    internal enum RecentChatChannel { Party, Say, Shout, Guild, Whisper, System, Other }

    internal sealed class RecentSocialLine
    {
        internal RecentChatChannel Channel;
        internal string Speaker;
        internal string Text;
        internal DateTime Utc;
        internal bool PlayerOrParty;
        internal bool Public;
    }

    // Ephemeral only. There is intentionally no serialization method and no MemoryStore reference.
    internal sealed class RecentSocialChatBuffer
    {
        internal const int Capacity = 20;
        private readonly object _lock = new object();
        private readonly List<RecentSocialLine> _lines = new List<RecentSocialLine>();

        internal void ObserveVisible(string raw, DateTime nowUtc)
        {
            ObserveVisible(raw, nowUtc, null);
        }

        internal void ObserveVisible(string raw, DateTime nowUtc, RecentChatChannel? nativeChannel)
        {
            RecentSocialLine line = Parse(raw, nowUtc);
            if (line == null && nativeChannel.HasValue)
                line = new RecentSocialLine { Channel = nativeChannel.Value, Speaker = "Unknown", Text = Clean(raw, 220), Utc = nowUtc };
            if (line == null || string.IsNullOrWhiteSpace(line.Text)) return;
            if (nativeChannel.HasValue)
            {
                line.Channel = nativeChannel.Value;
                line.Public = line.Channel == RecentChatChannel.Say || line.Channel == RecentChatChannel.Shout;
                line.PlayerOrParty = line.Channel == RecentChatChannel.Party;
            }
            lock (_lock)
            {
                if (_lines.Count > 0)
                {
                    RecentSocialLine prior = _lines[_lines.Count - 1];
                    if (prior.Channel == line.Channel && string.Equals(prior.Speaker, line.Speaker, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(prior.Text, line.Text, StringComparison.Ordinal) && Math.Abs((line.Utc - prior.Utc).TotalSeconds) < 1.0) return;
                }
                _lines.Add(line);
                while (_lines.Count > Capacity) _lines.RemoveAt(0);
            }
        }

        internal List<RecentSocialLine> Snapshot(DateTime nowUtc, double maxAgeSeconds)
        {
            List<RecentSocialLine> result = new List<RecentSocialLine>();
            lock (_lock)
            {
                for (int i = 0; i < _lines.Count; i++)
                    if ((nowUtc - _lines[i].Utc).TotalSeconds <= Math.Max(1.0, maxAgeSeconds)) result.Add(Clone(_lines[i]));
            }
            return result;
        }

        internal void Clear() { lock (_lock) _lines.Clear(); }

        private static RecentSocialLine Parse(string raw, DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string text;
            try { text = Regex.Replace(raw, @"<[^>]+>", string.Empty); }
            catch { text = raw; }
            text = Clean(text, 280);
            if (text.Length == 0) return null;
            RecentSocialLine line = new RecentSocialLine { Utc = nowUtc, Channel = RecentChatChannel.Other };
            Match match = Regex.Match(text, @"^(.+?)\s+(tells the group|says to the group|shouts|says|tells you):\s*(.+)$", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                line.Speaker = Clean(match.Groups[1].Value, 64);
                string verb = match.Groups[2].Value.ToLowerInvariant();
                line.Text = Clean(match.Groups[3].Value, 220);
                if (verb.Contains("group")) { line.Channel = RecentChatChannel.Party; line.PlayerOrParty = true; }
                else if (verb == "shouts") { line.Channel = RecentChatChannel.Shout; line.Public = true; }
                else if (verb == "says") { line.Channel = RecentChatChannel.Say; line.Public = true; }
                else line.Channel = RecentChatChannel.Whisper;
            }
            else if (text.StartsWith("You tell the group:", StringComparison.OrdinalIgnoreCase))
            {
                line.Channel = RecentChatChannel.Party; line.Speaker = "Player"; line.PlayerOrParty = true;
                line.Text = Clean(text.Substring("You tell the group:".Length), 220);
            }
            else if (text.IndexOf("[Guild]", StringComparison.OrdinalIgnoreCase) >= 0)
            { line.Channel = RecentChatChannel.Guild; line.Speaker = "Guild"; line.Text = text; }
            else if (text.StartsWith("[System]", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("[Social]", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("[WHISPER FROM]", StringComparison.OrdinalIgnoreCase))
            { line.Channel = text.StartsWith("[WHISPER", StringComparison.OrdinalIgnoreCase) ? RecentChatChannel.Whisper : RecentChatChannel.System; line.Speaker = "System"; line.Text = text; }
            else return null;
            return line.Text.Length == 0 ? null : line;
        }

        private static RecentSocialLine Clone(RecentSocialLine value)
        {
            return new RecentSocialLine { Channel = value.Channel, Speaker = value.Speaker, Text = value.Text, Utc = value.Utc,
                PlayerOrParty = value.PlayerOrParty, Public = value.Public };
        }

        private static string Clean(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string clean = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Replace('\0', ' ').Trim();
            return clean.Length <= max ? clean : clean.Substring(0, max);
        }
    }

    internal sealed class UnansweredSocialTurn
    {
        internal string Speaker;
        internal string Text;
        internal DateTime ExpiresUtc;
    }

    internal sealed class UnansweredTurnTracker
    {
        private UnansweredSocialTurn _current;

        internal void NotePlayer(string text, DateTime nowUtc)
        {
            if (!IsMeaningful(text)) { _current = null; return; }
            _current = new UnansweredSocialTurn { Speaker = "Player", Text = Bound(text, 180), ExpiresUtc = nowUtc.AddSeconds(150) };
        }

        internal void NoteAcknowledgement(DateTime nowUtc) { if (_current != null && nowUtc <= _current.ExpiresUtc) _current = null; }
        internal UnansweredSocialTurn Current(DateTime nowUtc) { if (_current != null && nowUtc > _current.ExpiresUtc) _current = null; return _current; }
        internal void Clear() { _current = null; }

        internal static bool IsMeaningful(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string value = text.Trim().ToLowerInvariant().Trim('.', '!', '?', ' ');
            string[] trivial = new string[] { "lol", "nice", "ok", "okay", "gg", "yeah", "yep", "cool", "thanks", "ty" };
            for (int i = 0; i < trivial.Length; i++) if (value == trivial[i]) return false;
            return value.Length >= 14;
        }

        private static string Bound(string value, int max) { return value.Length <= max ? value : value.Substring(0, max); }
    }

    internal sealed class SocialSituationSnapshot
    {
        internal SocialActivityState ActivityState;
        internal string Scene;
        internal int EligiblePartyCount;
        internal List<string> EligiblePartyMembers = new List<string>();
        internal double SecondsStationary;
        internal double SecondsOutOfCombat;
        internal double SecondsSinceMeaningfulGameplay;
        internal double SecondsSincePlayerSpeech;
        internal double SecondsSincePartySpeech;
        internal double SecondsSinceAnyObservedChat;
        internal double SecondsSinceAutonomousSpeech;
        internal string CurrentThread;
        internal string LastVisibleSpeaker;
        internal List<RecentSocialLine> RecentChat = new List<RecentSocialLine>();
        internal string CurrentCampmasterState;
        internal string SocialBudget;
        internal SocialActivityPreset PacingPreset;
        internal bool AutoRelax;
        internal bool AutoCamp;
        internal string EphemeralSummary;
        internal UnansweredSocialTurn Unanswered;

        internal string RenderPulseInput()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Activity=" + ActivityState + "; Party=" + EligiblePartyCount + "; Scene=" + Bound(Scene, 80));
            if (EligiblePartyMembers.Count > 0) sb.AppendLine("PartyMembers=" + Bound(string.Join(", ", EligiblePartyMembers.ToArray()), 220));
            sb.AppendLine("StationarySeconds=" + Math.Round(SecondsStationary) + "; SincePlayerSpeech=" + Math.Round(SecondsSincePlayerSpeech) +
                "; SincePartySpeech=" + Math.Round(SecondsSincePartySpeech) + "; SinceAnyChat=" + Math.Round(SecondsSinceAnyObservedChat));
            sb.AppendLine("Thread=" + Bound(CurrentThread, 180) + "; Relax=" + (AutoRelax ? "Auto" : CurrentCampmasterState));
            if (!string.IsNullOrWhiteSpace(EphemeralSummary)) sb.AppendLine("CurrentConversation=" + Bound(EphemeralSummary, 900));
            if (Unanswered != null) sb.AppendLine("UnansweredHEARD=" + Bound(Unanswered.Speaker + " said: " + Unanswered.Text, 220));
            int start = Math.Max(0, RecentChat.Count - 10);
            for (int i = start; i < RecentChat.Count; i++)
            {
                RecentSocialLine line = RecentChat[i];
                sb.AppendLine("Recent[" + line.Channel + "] " + Bound(line.Speaker, 40) + " said: " + Bound(line.Text, 160));
            }
            return Bound(sb.ToString(), 2600);
        }

        private static string Bound(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return "none";
            string clean = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return clean.Length <= max ? clean : clean.Substring(0, max);
        }
    }

    internal sealed class ContextPulseDecision
    {
        internal bool SpeakNow;
        internal bool SilenceStillNatural;
        internal string CurrentTopic;
        internal string ReasonCategory;
        internal bool Cancelled;
        internal string TerminalReason;

        internal static ContextPulseDecision Terminal(string reason)
        {
            return new ContextPulseDecision { Cancelled = true, TerminalReason = string.IsNullOrWhiteSpace(reason) ? "failed" : reason };
        }

        internal static ContextPulseDecision Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            ContextPulseDecision value = new ContextPulseDecision();
            string[] lines = raw.Replace('\r', '\n').Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool sawSpeak = false;
            for (int i = 0; i < lines.Length; i++)
            {
                int eq = lines[i].IndexOf('='); if (eq <= 0) continue;
                string key = lines[i].Substring(0, eq).Trim().ToLowerInvariant();
                string field = lines[i].Substring(eq + 1).Trim();
                if (key == "speaknow") { sawSpeak = true; value.SpeakNow = field.Equals("yes", StringComparison.OrdinalIgnoreCase); }
                else if (key == "silencestillnatural") value.SilenceStillNatural = field.Equals("yes", StringComparison.OrdinalIgnoreCase);
                else if (key == "currenttopic") value.CurrentTopic = Bound(field, 100);
                else if (key == "reasoncategory") value.ReasonCategory = Token(field, 40);
            }
            return sawSpeak ? value : null;
        }

        private static string Bound(string value, int max) { return string.IsNullOrWhiteSpace(value) ? string.Empty : (value.Length <= max ? value : value.Substring(0, max)); }
        private static string Token(string value, int max)
        {
            string clean = Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9_]+", "_").Trim('_');
            return clean.Length <= max ? clean : clean.Substring(0, max);
        }
    }

    internal static class ContextPulsePolicy
    {
        internal static double NextSeconds(int partyCount, SocialActivityState activity, SocialActivityPreset preset, double roll)
        {
            return NextSeconds(partyCount, activity, preset, "campmaster", roll);
        }

        internal static bool IsEligibleActivity(SocialActivityState activity)
        {
            return activity == SocialActivityState.ActiveGameplay || activity == SocialActivityState.SocialDowntime ||
                activity == SocialActivityState.ExtendedDowntime;
        }

        internal static double NextSeconds(int partyCount, SocialActivityState activity, SocialActivityPreset preset,
            string activitySource, double roll)
        {
            if (!IsEligibleActivity(activity)) return double.PositiveInfinity;
            double min, max;
            if (activity == SocialActivityState.ActiveGameplay)
            {
                if (partyCount <= 1) { min = 120; max = 150; }
                else if (partyCount == 2) { min = 90; max = 135; }
                else if (partyCount == 3) { min = 60; max = 105; }
                else if (preset == SocialActivityPreset.Lively) { min = 45; max = 90; }
                else if (preset == SocialActivityPreset.Quiet) { min = 90; max = 150; }
                else { min = 75; max = 120; }
            }
            else if (partyCount <= 1) { min = 120; max = 180; }
            else if (partyCount == 2) { min = 45; max = 90; }
            else if (partyCount == 3) { min = 30; max = 60; }
            else { min = preset == SocialActivityPreset.Lively ? 20 : 45; max = preset == SocialActivityPreset.Lively ? 45 : 90; }
            // Campmaster-proven downtime is the highest-confidence/fastest context. Standalone
            // still uses the same scheduler, but with a modest multiplier rather than going dark.
            if ((activity == SocialActivityState.SocialDowntime || activity == SocialActivityState.ExtendedDowntime) &&
                !string.Equals(activitySource, "campmaster", StringComparison.OrdinalIgnoreCase))
            { min *= 1.35; max *= 1.35; }
            if (activity == SocialActivityState.ExtendedDowntime && partyCount >= 3) { min *= .85; max *= .85; }
            double bounded = Math.Max(0.0, Math.Min(1.0, roll));
            return min + (bounded * (max - min));
        }
    }

    internal sealed class SocialFunnelMetrics
    {
        internal int BasePacingOpportunities, ContextPulses, PulseSpeak, PulseSilence, BlockedBudget, BlockedCooldown, NoSpeakerTopic;
        internal int LlmGenerations, GroundingRejected, StaleRejected, VisibleLines, ThreadsCreated;
        internal double AverageVisibleGap, MaximumVisibleGap;
    }

    internal static class SocialFunnelSimulation
    {
        internal static SocialFunnelMetrics RunTenMinutes(int partyCount)
        {
            SocialFunnelMetrics m = new SocialFunnelMetrics();
            double now = 60.0, lastVisible = 0.0, gapSum = 0.0;
            int visibleGaps = 0, pulse = 0;
            while (now <= 600.0)
            {
                m.BasePacingOpportunities++; m.ContextPulses++; pulse++;
                m.LlmGenerations++; // structured context assessment
                bool silence = partyCount > 1 ? pulse % 4 == 0 : pulse % 2 == 1;
                if (silence) m.PulseSilence++;
                else
                {
                    m.PulseSpeak++; m.LlmGenerations++; // one bounded dialogue generation
                    if (pulse % 11 == 0) m.GroundingRejected++;
                    else
                    {
                        m.VisibleLines++; m.ThreadsCreated++;
                        double gap = now - lastVisible; gapSum += gap; visibleGaps++; if (gap > m.MaximumVisibleGap) m.MaximumVisibleGap = gap;
                        lastVisible = now;
                    }
                }
                double delay = ContextPulsePolicy.NextSeconds(partyCount, SocialActivityState.SocialDowntime,
                    SocialActivityPreset.Lively, ((pulse * 37) % 100) / 100.0);
                now += delay;
            }
            m.AverageVisibleGap = visibleGaps == 0 ? 0.0 : gapSum / visibleGaps;
            return m;
        }
    }
}
