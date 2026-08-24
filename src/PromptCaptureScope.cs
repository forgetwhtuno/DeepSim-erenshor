using System;
using System.Collections.Generic;
using System.Threading;

namespace ErenshorDeepSims
{
    // ---------------------------------------------------------------------------------------------
    // Ambient current-packet plumbing.
    //
    // The generation pipeline is a deep async call chain (reply -> prompt build -> TimedChatAsync ->
    // OllamaClient -> grounding -> retry). Threading a diagnostic parameter through every one of those
    // call sites would touch a lot of production code for no runtime benefit, so the current packet is
    // carried in an AsyncLocal instead. AsyncLocal flows across await points, including
    // ConfigureAwait(false), which a ThreadStatic would not.
    //
    // Everything here fails open: if the ambient value is missing for any reason, capture simply does
    // not record that stage and generation proceeds untouched.
    // ---------------------------------------------------------------------------------------------
    internal static class PromptCaptureScope
    {
        private static readonly AsyncLocal<PromptCapturePacket> _current = new AsyncLocal<PromptCapturePacket>();

        internal static PromptCapturePacket Current
        {
            get
            {
                try { return _current.Value; }
                catch { return null; }
            }
        }

        // Returns null when capture is off; `using (null)` is not valid, so callers test for null.
        internal static PromptCaptureLease Begin(string stage, string source)
        {
            PromptCapturePacket packet = PromptCapture.TryBegin(stage, source, false);
            if (packet == null) return null;
            return new PromptCaptureLease(packet, _current);
        }

        internal static PromptCaptureLease BeginClassifier(string stage, int parentTurnId)
        {
            PromptCapturePacket packet = PromptCapture.TryBegin(stage, "semantic_classifier", true);
            if (packet == null) return null;
            packet.TurnId = parentTurnId;
            return new PromptCaptureLease(packet, _current);
        }

        // Applies values that only the reply pipeline knows. Every argument is already a bounded,
        // copied value; nothing here dereferences a live snapshot later.
        internal static void DescribeSpeaker(string name, string className, int level)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try
            {
                packet.SpeakerName = name ?? string.Empty;
                packet.SpeakerClass = className ?? string.Empty;
                packet.SpeakerLevel = level;
            }
            catch { }
        }

        internal static void DescribeEffectiveRoute(string turnType, string knowledgeNeed, string topic, string subject, string socialIntent)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try
            {
                packet.EffectiveTurnType = turnType ?? string.Empty;
                packet.EffectiveKnowledgeNeed = knowledgeNeed ?? string.Empty;
                packet.EffectiveTopic = topic ?? string.Empty;
                packet.EffectiveSubject = subject ?? string.Empty;
                packet.EffectiveSocialIntent = socialIntent ?? string.Empty;
            }
            catch { }
        }

        internal static void DescribeRawClassifier(string turnType, string knowledgeNeed, string topic, string subject,
            string searchQuery, double confidence, bool directAnswerRequired, List<string> corrections)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try
            {
                packet.HasRawClassifier = true;
                packet.RawTurnType = turnType ?? string.Empty;
                packet.RawKnowledgeNeed = knowledgeNeed ?? string.Empty;
                packet.RawTopic = topic ?? string.Empty;
                packet.RawSubject = subject ?? string.Empty;
                packet.RawSearchQuery = searchQuery ?? string.Empty;
                packet.RawConfidence = confidence;
                packet.RawDirectAnswerRequired = directAnswerRequired;
                if (corrections != null)
                {
                    for (int i = 0; i < corrections.Count; i++)
                        if (!packet.DeterministicCorrections.Contains(corrections[i])) packet.DeterministicCorrections.Add(corrections[i]);
                }
            }
            catch { }
        }

        internal static void DescribeWorld(string zone, string currentEncounter, string lastEncounter,
            string partyMembership, string guild, string assignedRoles, string personality)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try
            {
                packet.WorldZone = zone ?? string.Empty;
                packet.WorldCurrentEncounter = currentEncounter ?? string.Empty;
                packet.WorldLastEncounter = lastEncounter ?? string.Empty;
                packet.LivePartyMembership = partyMembership ?? string.Empty;
                packet.SpeakerGuild = guild ?? string.Empty;
                packet.SpeakerAssignedRoles = assignedRoles ?? string.Empty;
                packet.SpeakerPersonality = personality ?? string.Empty;
            }
            catch { }
        }

        internal static void DescribeSessionSummary(string summary)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try { packet.SessionSummary = summary ?? string.Empty; }
            catch { }
        }

        // Only the memories actually chosen for THIS prompt are recorded. The rejected candidates are
        // deliberately never serialized: only their count is kept.
        internal static void DescribeSelectedMemory(IList<PromptCaptureMemoryItem> selected, int candidateCount)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try
            {
                packet.SelectedVerifiedMemory.Clear();
                if (selected != null)
                {
                    for (int i = 0; i < selected.Count; i++)
                        if (selected[i] != null) packet.SelectedVerifiedMemory.Add(selected[i]);
                }
                packet.MemoryCandidateCount = candidateCount;
                packet.MemorySelectedCount = packet.SelectedVerifiedMemory.Count;
            }
            catch { }
        }

        internal static void DescribeSelectedSoftPersona(IList<string> persona)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try
            {
                packet.SelectedSoftPersona.Clear();
                if (persona != null)
                {
                    for (int i = 0; i < persona.Count; i++)
                        if (!string.IsNullOrEmpty(persona[i])) packet.SelectedSoftPersona.Add(persona[i]);
                }
            }
            catch { }
        }

        // `evidence` must already be the bounded extract PromptBuilder hands the model, never a whole
        // downloaded page.
        internal static void DescribeRetrieval(bool used, string kind, string sourceLabel, string query, bool found, string boundedEvidence)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try
            {
                packet.RetrievalUsed = used;
                packet.RetrievalKind = kind ?? string.Empty;
                packet.RetrievalSourceLabel = sourceLabel ?? string.Empty;
                packet.RetrievalQuery = query ?? string.Empty;
                packet.RetrievalFound = found;
                packet.RetrievalEvidence = boundedEvidence ?? string.Empty;
            }
            catch { }
        }

        internal static void DescribeThread(IList<PromptCaptureThreadLine> thread)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try
            {
                packet.ConversationThread.Clear();
                if (thread == null) return;
                for (int i = 0; i < thread.Count; i++)
                    if (thread[i] != null) packet.ConversationThread.Add(thread[i]);
            }
            catch { }
        }

        // Sim-to-Sim linkage. `previousAcceptedVisibleText` must be the text B actually receives, which
        // is the ACCEPTED VISIBLE line (or the deterministic opener when A's candidate was rejected).
        internal static void DescribeConnectedTurn(int parentRequestId, int conversationTurnIndex,
            string previousSpeaker, string previousRawCandidate, string previousAcceptedVisibleText)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try
            {
                packet.ConnectedSimTurn = true;
                packet.ParentRequestId = parentRequestId;
                packet.ConversationTurnIndex = conversationTurnIndex;
                packet.PreviousSpeaker = previousSpeaker ?? string.Empty;
                packet.PreviousRawCandidate = previousRawCandidate ?? string.Empty;
                packet.PreviousAcceptedVisibleText = previousAcceptedVisibleText ?? string.Empty;
            }
            catch { }
        }

        internal static void DescribeSeed(string seedType, string topicKey, string source, string supportingFact,
            string selectedOtherSim, bool prerequisitesPassed, bool topicSuppressed, bool forceMessage)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try
            {
                packet.HasSeed = true;
                packet.SeedType = seedType ?? string.Empty;
                packet.SeedTopicKey = topicKey ?? string.Empty;
                packet.SeedSource = source ?? string.Empty;
                packet.SeedSupportingFact = supportingFact ?? string.Empty;
                packet.SeedSelectedOtherSim = selectedOtherSim ?? string.Empty;
                packet.SeedPrerequisitesPassed = prerequisitesPassed;
                packet.SeedTopicSuppressed = topicSuppressed;
                packet.ForceMessage = forceMessage;
            }
            catch { }
        }

        internal static void DescribeBackground(string lane, int sessionGeneration, int conversationGeneration,
            string verifiedSourceId, string correlationId)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try
            {
                packet.Lane = lane ?? string.Empty;
                packet.SessionGeneration = sessionGeneration;
                packet.ConversationGeneration = conversationGeneration;
                packet.VerifiedSourceId = verifiedSourceId ?? string.Empty;
                packet.CorrelationId = correlationId ?? string.Empty;
            }
            catch { }
        }

        // The raw, pre-resolution Deep Sims "Model" config value, recorded independently of the
        // resolved model actually used for generation. In a healthy single-model session these always
        // agree; capturing both lets a packet prove that rather than assume it.
        internal static void DescribeConfiguredModel(string configuredModel)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try { packet.ConfiguredModel = configuredModel ?? string.Empty; }
            catch { }
        }

        internal static void DescribeGeneration(string model, bool reasoningModelSelected, int numCtx, float temperature,
            int numPredict, string keepAlive, string inferenceMode, int cpuThreads, IList<ChatMessage> messages)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try
            {
                packet.Model = model ?? string.Empty;
                packet.ReasoningModelSelected = reasoningModelSelected;
                packet.NumCtx = numCtx;
                packet.Temperature = temperature;
                packet.NumPredict = numPredict;
                packet.KeepAlive = keepAlive ?? string.Empty;
                packet.InferenceMode = inferenceMode ?? string.Empty;
                packet.CpuThreads = cpuThreads;
                packet.Messages.Clear();
                if (messages == null) return;
                for (int i = 0; i < messages.Count; i++)
                {
                    ChatMessage message = messages[i];
                    if (message == null) continue;
                    packet.Messages.Add(new PromptCaptureMessage(message.role, message.content));
                }
            }
            catch { }
        }

        // Raw model text, recorded once, before any guard can rewrite it.
        internal static void RecordRawModelContent(string raw)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try
            {
                SyncCorrelation(packet);
                if (string.IsNullOrEmpty(packet.RawModelContent)) packet.RawModelContent = raw ?? string.Empty;
            }
            catch { }
        }

        internal static void RecordQualityRetry()
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try { packet.QualityRetryCount++; }
            catch { }
        }

        internal static void RecordGrounding(string decision, string reason)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try
            {
                SyncCorrelation(packet);
                packet.GroundingDecision = decision ?? "unknown";
                packet.GroundingReason = reason ?? string.Empty;
                packet.GroundingReasonCategory = PromptCaptureReasonCategory.Classify(reason);
            }
            catch { }
        }

        internal static void RecordRoleplayGuard(bool ran, bool changed, bool rejected, string reason)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try
            {
                packet.RoleplayGuardRan = ran;
                packet.RoleplayGuardChanged = changed;
                packet.RoleplayGuardRejected = rejected;
                packet.RoleplayGuardReasonCategory = PromptCaptureReasonCategory.Classify(reason);
            }
            catch { }
        }

        internal static void RecordPostGuardContent(string text)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try { packet.PostGuardContent = text ?? string.Empty; }
            catch { }
        }

        internal static void RecordFallback(bool used, string kind)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try
            {
                packet.FallbackUsed = used;
                packet.FallbackKind = string.IsNullOrEmpty(kind) ? "none" : kind;
            }
            catch { }
        }

        internal static void RecordFinal(bool displayed, string source, string visibleText)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try
            {
                SyncCorrelation(packet);
                packet.Displayed = displayed;
                packet.FinalSource = source ?? string.Empty;
                packet.FinalVisibleContent = visibleText ?? string.Empty;
            }
            catch { }
        }

        internal static void RecordQueueAccepted(bool accepted)
        {
            PromptCapturePacket packet = Current;
            if (packet == null) return;
            try
            {
                packet.QueueAccepted = accepted;
                if (!accepted) packet.VisibilityDisposition = "queue_rejected";
                else if (packet.VisibilityDisposition == "not_applicable") packet.VisibilityDisposition = "queued";
            }
            catch { }
        }

        internal static void RecordAdvancedBeforeVisibility(AutonomousAdvanceObservation observation)
        {
            RecordAdvancedBeforeVisibility(Current, observation);
        }

        internal static void RecordAdvancedBeforeVisibility(PromptCapturePacket packet, AutonomousAdvanceObservation observation)
        {
            if (packet == null || observation == null) return;
            try
            {
                packet.TopicFatigueAdvanced = observation.TopicFatigueAdvanced;
                packet.ConversationMomentAdded = observation.ConversationMomentAdded;
                packet.PreferencePersisted = observation.PreferencePersisted;
                packet.CallbackStateAdvanced = observation.CallbackStateAdvanced;
            }
            catch { }
        }

        internal static void RecordVisibility(PromptCapturePacket packet, bool visible, string disposition, string visibleText)
        {
            if (packet == null) return;
            try
            {
                SyncCorrelation(packet);
                packet.Displayed = visible;
                packet.FinalSource = visible ? "LLM" : "none";
                packet.FinalVisibleContent = visible ? (visibleText ?? string.Empty) : string.Empty;
                packet.VisibilityDisposition = CognitionObservability.BoundedToken(disposition);
            }
            catch { }
        }

        internal static int CurrentRequestId
        {
            get
            {
                PromptCapturePacket packet = Current;
                return packet == null ? 0 : packet.RequestId;
            }
        }

        internal static void SyncCorrelation(PromptCapturePacket packet)
        {
            if (packet == null) return;
            DialogueRequestCorrelation current = DialogueRequestCorrelation.Current;
            if (current == null) return;
            packet.CorrelationRequestId = current.RequestId;
            packet.CorrelationAttempt = current.Attempt;
            packet.CorrelationThreadId = current.ThreadId;
            packet.CorrelationCandidateHash = current.CandidateHash ?? string.Empty;
            packet.CorrelationDisposition = current.Disposition ?? string.Empty;
        }
    }

    // Disposable ambient lease. Disposing writes the packet exactly once.
    internal sealed class PromptCaptureLease : IDisposable
    {
        private readonly AsyncLocal<PromptCapturePacket> _slot;
        private readonly PromptCapturePacket _previous;
        private PromptCapturePacket _packet;

        internal PromptCaptureLease(PromptCapturePacket packet, AsyncLocal<PromptCapturePacket> slot)
        {
            _packet = packet;
            _slot = slot;
            try
            {
                _previous = slot == null ? null : slot.Value;
                if (slot != null) slot.Value = packet;
            }
            catch { }
        }

        internal PromptCapturePacket Packet { get { return _packet; } }

        // Detach a packet from the ambient scope so final visibility can complete it later at the
        // Unity-thread display boundary. This changes only diagnostic ownership; production output
        // remains in the existing queue.
        internal PromptCapturePacket DeferCompletion()
        {
            PromptCapturePacket packet = _packet;
            _packet = null;
            try { if (_slot != null) _slot.Value = _previous; }
            catch { }
            return packet;
        }

        public void Dispose()
        {
            PromptCapturePacket packet = _packet;
            _packet = null;
            try { if (_slot != null) _slot.Value = _previous; }
            catch { }
            if (packet != null) PromptCapture.Complete(packet);
        }
    }
}
