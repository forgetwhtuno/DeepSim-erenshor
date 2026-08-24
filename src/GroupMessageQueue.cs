using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    // A connected banter tail is only armed on a queued opener. It is consumed after that opener
    // actually crosses the final visible-output boundary, so rejected/hidden candidates can never
    // become "spoken" context for another Sim. The plan carries only bounded semantic seed data;
    // all live party state is recaptured before the continuation runs.
    internal sealed class ConnectedBanterPlan
    {
        internal int RemainingReplies;
        internal long OpportunityId;
        internal string EventType = string.Empty;
        internal string TopicKey = string.Empty;
        internal string CooldownGroup = string.Empty;
        internal string PromptHint = string.Empty;
        internal string VerifiedFact = string.Empty;
        internal bool ManualThread;
    }

    // Plain-data typing queue. Every eventual party-chat line retains the conversation generation
    // and diagnostic scope that owned it so the Unity-thread display boundary can revalidate it.
    internal class ScheduledGroupMessage
    {
        internal DateTime DueUtc;
        internal DateTime EnqueuedUtc;
        internal string Speaker;
        internal string Text;
        internal bool Autonomous;
        internal int ConversationGeneration = -1;
        internal string DiagnosticContext = string.Empty;
        internal long PartyRequestId;
        internal long MembershipVersion = -1;
        internal string SpeakerActorId = string.Empty;
        internal string GenerationPath = string.Empty;
        internal DateTime PartySnapshotCapturedUtc;
        internal int EligibleSpeakerCount;
        internal string SoftPreferenceTopicKey = string.Empty;
        internal ConnectedBanterPlan ConnectedBanter;
        internal List<string> KnownSocialSubjects = new List<string>();
        internal PromptCapturePacket PromptCapturePacket;
        internal long CorrelationRequestId;
        internal int CorrelationAttempt;
        internal long CorrelationThreadId;
        internal string CorrelationCandidateHash = string.Empty;
        internal string CorrelationRequestType = string.Empty;
    }

    internal class GroupMessageQueue
    {
        private readonly object _lock = new object();
        private readonly List<ScheduledGroupMessage> _items = new List<ScheduledGroupMessage>();

        internal void Enqueue(DateTime dueUtc, string speaker, string text, bool autonomous = false,
            int conversationGeneration = -1, string diagnosticContext = null, long partyRequestId = 0,
            long membershipVersion = -1, string speakerActorId = null, string generationPath = null,
            DateTime partySnapshotCapturedUtc = default(DateTime), int eligibleSpeakerCount = 0,
            string softPreferenceTopicKey = null, ConnectedBanterPlan connectedBanter = null,
            IList<string> knownSocialSubjects = null, PromptCapturePacket promptCapturePacket = null,
            long correlationRequestId = 0, int correlationAttempt = 0, long correlationThreadId = 0,
            string correlationCandidateHash = null, string correlationRequestType = null)
        {
            if (string.IsNullOrWhiteSpace(speaker) || string.IsNullOrWhiteSpace(text)) return;
            lock (_lock)
            {
                ScheduledGroupMessage item = new ScheduledGroupMessage();
                item.DueUtc = dueUtc;
                item.EnqueuedUtc = DateTime.UtcNow;
                item.Speaker = speaker;
                item.Text = text;
                item.Autonomous = autonomous;
                item.ConversationGeneration = conversationGeneration;
                item.DiagnosticContext = diagnosticContext ?? string.Empty;
                item.PartyRequestId = partyRequestId;
                item.MembershipVersion = membershipVersion;
                item.SpeakerActorId = speakerActorId ?? string.Empty;
                item.GenerationPath = generationPath ?? string.Empty;
                item.PartySnapshotCapturedUtc = partySnapshotCapturedUtc;
                item.EligibleSpeakerCount = Math.Max(0, eligibleSpeakerCount);
                item.SoftPreferenceTopicKey = softPreferenceTopicKey ?? string.Empty;
                item.ConnectedBanter = connectedBanter;
                item.PromptCapturePacket = promptCapturePacket;
                item.CorrelationRequestId = correlationRequestId;
                item.CorrelationAttempt = correlationAttempt;
                item.CorrelationThreadId = correlationThreadId;
                item.CorrelationCandidateHash = correlationCandidateHash ?? string.Empty;
                item.CorrelationRequestType = correlationRequestType ?? string.Empty;
                if (knownSocialSubjects != null)
                    for (int i = 0; i < knownSocialSubjects.Count; i++)
                        if (!string.IsNullOrWhiteSpace(knownSocialSubjects[i])) item.KnownSocialSubjects.Add(knownSocialSubjects[i].Trim());
                _items.Add(item);
                _items.Sort(delegate(ScheduledGroupMessage a, ScheduledGroupMessage b) { return a.DueUtc.CompareTo(b.DueUtc); });
            }
        }

        internal List<ScheduledGroupMessage> TakeDue(DateTime now)
        {
            List<ScheduledGroupMessage> due = new List<ScheduledGroupMessage>();
            lock (_lock)
            {
                int count = 0;
                while (count < _items.Count && _items[count].DueUtc <= now) count++;
                if (count > 0)
                {
                    due.AddRange(_items.GetRange(0, count));
                    _items.RemoveRange(0, count);
                }
            }
            return due;
        }

        internal List<ScheduledGroupMessage> Clear()
        {
            lock (_lock)
            {
                List<ScheduledGroupMessage> removed = new List<ScheduledGroupMessage>(_items);
                _items.Clear();
                return removed;
            }
        }

        internal List<ScheduledGroupMessage> ClearAutonomous()
        {
            lock (_lock)
            {
                List<ScheduledGroupMessage> removed = new List<ScheduledGroupMessage>();
                for (int i = _items.Count - 1; i >= 0; i--)
                {
                    if (!_items[i].Autonomous) continue;
                    removed.Add(_items[i]);
                    _items.RemoveAt(i);
                }
                return removed;
            }
        }

        internal int Count
        {
            get { lock (_lock) return _items.Count; }
        }
    }
}
