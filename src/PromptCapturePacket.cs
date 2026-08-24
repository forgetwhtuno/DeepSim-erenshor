using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    // ---------------------------------------------------------------------------------------------
    // Packet value objects + serializers. Unity-free and IO-free on purpose (see PromptCaptureModel).
    //
    // Every field here is a simple immutable value that the caller COPIED at the request boundary.
    // Nothing in this file ever holds a reference to a live Unity object, a snapshot object, or the
    // memory store, so background serialization can never race live mutable game state.
    // ---------------------------------------------------------------------------------------------

    internal sealed class PromptCaptureMessage
    {
        internal readonly string Role;
        internal readonly string Content;
        internal PromptCaptureMessage(string role, string content)
        {
            Role = role ?? string.Empty;
            Content = content ?? string.Empty;
        }
    }

    internal sealed class PromptCaptureMemoryItem
    {
        internal readonly string Source;
        internal readonly string Text;
        internal PromptCaptureMemoryItem(string source, string text)
        {
            Source = source ?? string.Empty;
            Text = text ?? string.Empty;
        }
    }

    internal sealed class PromptCaptureThreadLine
    {
        internal readonly string Speaker;
        internal readonly string Text;
        internal PromptCaptureThreadLine(string speaker, string text)
        {
            Speaker = speaker ?? string.Empty;
            Text = text ?? string.Empty;
        }
    }

    // One actual HTTP call to Ollama. Several of these share one logical request id.
    internal sealed class PromptCaptureAttempt
    {
        internal int Attempt;
        internal PromptCaptureAttemptKind Kind;
        internal string Model = string.Empty;
        internal string EndpointKind = string.Empty;
        internal int NumCtx;
        internal float Temperature;
        internal int NumPredict;
        internal int NumGpu = int.MinValue;
        internal int NumThread;
        internal bool Stream;
        internal bool Think;
        internal string KeepAlive = string.Empty;
        internal string InferenceMode = string.Empty;
        internal List<PromptCaptureMessage> Messages = new List<PromptCaptureMessage>();

        // The exact bytes-equivalent body handed to the HTTP layer.
        internal string SerializedRequestJson = string.Empty;

        internal bool HttpSucceeded;
        internal string HttpErrorType = string.Empty;
        internal bool Done;
        internal string DoneReason = string.Empty;
        internal string Content = string.Empty;
        internal string Thinking = string.Empty;
        internal int PromptEvalCount;
        internal int EvalCount;
        internal double TotalMs;
        internal double LoadMs;
        internal double PromptEvalMs;
        internal double EvalMs;
    }

    // One logical LLM request: the semantic inputs, every HTTP attempt, and the post-processing
    // outcome. Built incrementally on the calling thread with already-copied values.
    internal sealed class PromptCapturePacket
    {
        private readonly object _gate = new object();
        private readonly List<PromptCaptureAttempt> _attempts = new List<PromptCaptureAttempt>();

        internal int SchemaVersion = 1;
        internal string SessionId = string.Empty;
        internal int TurnId;
        internal int RequestId;
        // Stable lightweight identity supplied by DialogueRequestCorrelation. Unlike RequestId,
        // this remains available in normal diagnostics when capture is disabled.
        internal long CorrelationRequestId;
        internal int CorrelationAttempt;
        internal long CorrelationThreadId;
        internal string CorrelationCandidateHash = string.Empty;
        internal string CorrelationDisposition = string.Empty;
        internal string Utc = string.Empty;
        internal string Stage = string.Empty;
        internal string Source = string.Empty;
        internal string Lane = string.Empty;
        internal int SessionGeneration;
        internal int ConversationGeneration;
        internal string VerifiedSourceId = string.Empty;
        internal string CorrelationId = string.Empty;
        internal bool IsClassifier;
        internal string ManualLabel = string.Empty;

        internal string SpeakerName = string.Empty;
        internal string SpeakerClass = string.Empty;
        internal int SpeakerLevel;

        // Raw classifier output, before any deterministic correction.
        internal bool HasRawClassifier;
        internal string RawTurnType = string.Empty;
        internal string RawKnowledgeNeed = string.Empty;
        internal string RawTopic = string.Empty;
        internal string RawSubject = string.Empty;
        internal string RawSearchQuery = string.Empty;
        internal double RawConfidence;
        internal bool RawDirectAnswerRequired;

        // Effective route after deterministic correction.
        internal string EffectiveTurnType = string.Empty;
        internal string EffectiveKnowledgeNeed = string.Empty;
        internal string EffectiveTopic = string.Empty;
        internal string EffectiveSubject = string.Empty;
        internal string EffectiveSocialIntent = string.Empty;
        internal List<string> DeterministicCorrections = new List<string>();

        // Prompt inputs, each already bounded by the production prompt builder.
        internal string WorldZone = string.Empty;
        internal string WorldCurrentEncounter = string.Empty;
        internal string WorldLastEncounter = string.Empty;
        internal string SpeakerGuild = string.Empty;
        internal string SpeakerAssignedRoles = string.Empty;
        internal string SpeakerPersonality = string.Empty;
        internal string LivePartyMembership = string.Empty;
        internal string SessionSummary = string.Empty;

        internal List<PromptCaptureMemoryItem> SelectedVerifiedMemory = new List<PromptCaptureMemoryItem>();
        internal List<string> SelectedSoftPersona = new List<string>();
        internal int MemoryCandidateCount;
        internal int MemorySelectedCount;

        internal bool RetrievalUsed;
        internal string RetrievalKind = string.Empty;
        internal string RetrievalSourceLabel = string.Empty;
        internal string RetrievalQuery = string.Empty;
        internal bool RetrievalFound;
        internal string RetrievalEvidence = string.Empty;

        internal List<PromptCaptureThreadLine> ConversationThread = new List<PromptCaptureThreadLine>();

        // Connected Sim-to-Sim linkage.
        internal int ParentRequestId;
        internal int ConversationTurnIndex;
        internal string PreviousSpeaker = string.Empty;
        internal string PreviousRawCandidate = string.Empty;
        internal string PreviousAcceptedVisibleText = string.Empty;
        internal bool ConnectedSimTurn;

        // Autonomous seed context.
        internal bool HasSeed;
        internal string SeedType = string.Empty;
        internal string SeedTopicKey = string.Empty;
        internal string SeedSource = string.Empty;
        internal string SeedSupportingFact = string.Empty;
        internal string SeedSelectedOtherSim = string.Empty;
        internal bool SeedPrerequisitesPassed;
        internal bool SeedTopicSuppressed;
        internal bool ForceMessage;

        // Generation configuration as actually resolved for this request. `Model` here is the
        // RESOLVED model that was actually put in the Ollama request (single-model pipeline).
        // `ConfiguredModel` is the raw Deep Sims "Model" config value read independently at the same
        // moment, purely so a packet can prove the two agree; if a future regression ever lets a
        // caller diverge from the canonical resolver, this field pair makes it visible in the capture
        // data instead of silently trusting the code.
        internal string Model = string.Empty;
        internal string ConfiguredModel = string.Empty;
        internal bool ReasoningModelSelected;
        internal int NumCtx;
        internal float Temperature;
        internal int NumPredict;
        internal string KeepAlive = string.Empty;
        internal string InferenceMode = string.Empty;
        internal int CpuThreads;
        internal List<PromptCaptureMessage> Messages = new List<PromptCaptureMessage>();

        // Post-processing outcome. Raw / post-guard / final are deliberately three separate fields.
        internal string RawModelContent = string.Empty;
        internal string PostGuardContent = string.Empty;
        internal string FinalVisibleContent = string.Empty;
        internal int QualityRetryCount;
        internal string GroundingDecision = "unknown";
        internal string GroundingReason = string.Empty;
        internal string GroundingReasonCategory = "none";
        internal bool RoleplayGuardRan;
        internal bool RoleplayGuardChanged;
        internal bool RoleplayGuardRejected;
        internal string RoleplayGuardReasonCategory = "none";
        internal bool FallbackUsed;
        internal string FallbackKind = "none";
        internal bool Displayed;
        internal string FinalSource = string.Empty;
        internal bool QueueAccepted;
        internal string VisibilityDisposition = "not_applicable";
        internal bool TopicFatigueAdvanced;
        internal bool ConversationMomentAdded;
        internal bool PreferencePersisted;
        internal bool CallbackStateAdvanced;

        internal List<string> InterestingCases = new List<string>();

        internal PromptCaptureAttempt BeginAttempt(PromptCaptureAttemptKind kind)
        {
            lock (_gate)
            {
                PromptCaptureAttempt attempt = new PromptCaptureAttempt();
                attempt.Attempt = _attempts.Count + 1;
                attempt.Kind = kind;
                _attempts.Add(attempt);
                return attempt;
            }
        }

        internal List<PromptCaptureAttempt> AttemptsSnapshot()
        {
            lock (_gate) return new List<PromptCaptureAttempt>(_attempts);
        }

        internal int AttemptCount { get { lock (_gate) return _attempts.Count; } }

        internal string RequestFileName { get { return FileStem + "-request.json"; } }
        internal string ResultFileName { get { return FileStem + "-result.json"; } }
        internal string RawRequestFileName { get { return FileStem + "-request-raw.json"; } }
        internal string FileStem { get { return RequestId.ToString("000000", System.Globalization.CultureInfo.InvariantCulture); } }
    }

    internal static class PromptCapturePacketSerializer
    {
        internal static string BuildSemanticRequestJson(PromptCapturePacket packet)
        {
            if (packet == null) return "{}";
            PromptCaptureJsonWriter w = new PromptCaptureJsonWriter();
            w.StartObject();
            w.Number("schemaVersion", packet.SchemaVersion);
            w.String("sessionId", packet.SessionId);
            w.Number("turnId", packet.TurnId);
            w.Number("requestId", packet.RequestId);
            w.Number("correlationRequestId", packet.CorrelationRequestId);
            w.Number("attempt", packet.CorrelationAttempt);
            w.Number("threadId", packet.CorrelationThreadId);
            w.String("candidateHash", packet.CorrelationCandidateHash);
            w.String("disposition", packet.CorrelationDisposition);
            w.String("utc", packet.Utc);
            w.String("stage", packet.Stage);
            w.String("source", packet.Source);
            w.String("lane", packet.Lane);
            w.Number("sessionGeneration", packet.SessionGeneration);
            w.Number("conversationGeneration", packet.ConversationGeneration);
            w.String("verifiedSourceId", packet.VerifiedSourceId);
            w.String("correlationId", packet.CorrelationId);
            if (!string.IsNullOrEmpty(packet.ManualLabel)) w.String("manualLabel", packet.ManualLabel);

            w.StartObject("speaker");
            w.String("name", packet.SpeakerName);
            w.String("class", packet.SpeakerClass);
            w.Number("level", packet.SpeakerLevel);
            w.EndObject();

            w.StartObject("route");
            if (packet.HasRawClassifier)
            {
                w.StartObject("rawClassifier");
                w.String("turnType", packet.RawTurnType);
                w.String("knowledgeNeed", packet.RawKnowledgeNeed);
                w.String("topic", packet.RawTopic);
                w.String("subject", packet.RawSubject);
                w.String("searchQuery", packet.RawSearchQuery);
                w.Number("confidence", packet.RawConfidence);
                w.Bool("directAnswerRequired", packet.RawDirectAnswerRequired);
                w.EndObject();
            }
            w.StartObject("effective");
            w.String("turnType", packet.EffectiveTurnType);
            w.String("knowledgeNeed", packet.EffectiveKnowledgeNeed);
            w.String("topic", packet.EffectiveTopic);
            w.String("subject", packet.EffectiveSubject);
            w.String("socialIntent", packet.EffectiveSocialIntent);
            w.EndObject();
            w.StartArray("deterministicCorrections");
            for (int i = 0; i < packet.DeterministicCorrections.Count; i++) w.StringArrayItem(packet.DeterministicCorrections[i]);
            w.EndArray();
            w.EndObject();

            w.StartObject("promptInputs");
            w.StartObject("world");
            w.String("zone", packet.WorldZone);
            w.String("currentEncounter", packet.WorldCurrentEncounter);
            w.String("lastCompletedEncounter", packet.WorldLastEncounter);
            w.EndObject();
            w.StartObject("livePartyFacts");
            w.String("partyMembership", packet.LivePartyMembership);
            w.String("speakerGuild", packet.SpeakerGuild);
            w.String("speakerAssignedRoles", packet.SpeakerAssignedRoles);
            w.String("speakerPersonality", packet.SpeakerPersonality);
            w.EndObject();
            w.String("sessionSummary", packet.SessionSummary);

            w.StartArray("selectedVerifiedMemory");
            for (int i = 0; i < packet.SelectedVerifiedMemory.Count; i++)
            {
                w.StartObject();
                w.String("source", packet.SelectedVerifiedMemory[i].Source);
                w.String("text", packet.SelectedVerifiedMemory[i].Text);
                w.EndObject();
            }
            w.EndArray();
            w.StartArray("selectedSoftPersona");
            for (int i = 0; i < packet.SelectedSoftPersona.Count; i++) w.StringArrayItem(packet.SelectedSoftPersona[i]);
            w.EndArray();
            w.StartObject("memorySelection");
            w.Number("memoryCandidateCount", packet.MemoryCandidateCount);
            w.Number("memorySelectedCount", packet.MemorySelectedCount);
            w.EndObject();

            w.StartObject("retrieval");
            w.Bool("used", packet.RetrievalUsed);
            w.String("kind", packet.RetrievalKind);
            w.String("sourceLabel", packet.RetrievalSourceLabel);
            w.String("query", packet.RetrievalQuery);
            w.Bool("found", packet.RetrievalFound);
            w.String("evidence", packet.RetrievalEvidence);
            w.EndObject();

            w.StartArray("conversationThread");
            for (int i = 0; i < packet.ConversationThread.Count; i++)
            {
                w.StartObject();
                w.String("speaker", packet.ConversationThread[i].Speaker);
                w.String("text", packet.ConversationThread[i].Text);
                w.EndObject();
            }
            w.EndArray();
            w.EndObject();

            if (packet.ConnectedSimTurn || packet.ParentRequestId > 0)
            {
                w.StartObject("connectedConversation");
                w.Number("parentRequestId", packet.ParentRequestId);
                w.Number("conversationTurnIndex", packet.ConversationTurnIndex);
                w.String("previousSpeaker", packet.PreviousSpeaker);
                w.String("previousRawCandidate", packet.PreviousRawCandidate);
                w.String("previousAcceptedVisibleText", packet.PreviousAcceptedVisibleText);
                w.EndObject();
            }

            if (packet.HasSeed)
            {
                w.StartObject("seed");
                w.String("seedType", packet.SeedType);
                w.String("topicKey", packet.SeedTopicKey);
                w.String("source", packet.SeedSource);
                w.String("verifiedSupportingFact", packet.SeedSupportingFact);
                w.String("selectedOtherSim", packet.SeedSelectedOtherSim);
                w.Bool("prerequisitesPassed", packet.SeedPrerequisitesPassed);
                w.Bool("topicSuppressed", packet.SeedTopicSuppressed);
                w.Bool("forceMessage", packet.ForceMessage);
                w.EndObject();
            }

            w.StartObject("generation");
            // configuredModel/resolvedModel/model are expected to always agree in a healthy
            // single-model session; `model` is kept for backward compatibility with existing readers.
            w.String("configuredModel", packet.ConfiguredModel);
            w.String("resolvedModel", packet.Model);
            w.String("model", packet.Model);
            w.Bool("reasoningModelSelected", packet.ReasoningModelSelected);
            w.Number("numCtx", packet.NumCtx);
            w.Number("temperature", packet.Temperature);
            w.Number("numPredict", packet.NumPredict);
            w.String("keepAlive", packet.KeepAlive);
            w.String("inferenceMode", packet.InferenceMode);
            w.Number("cpuThreads", packet.CpuThreads);
            w.StartArray("messages");
            for (int i = 0; i < packet.Messages.Count; i++)
            {
                w.StartObject();
                w.String("role", packet.Messages[i].Role);
                w.String("content", packet.Messages[i].Content);
                w.EndObject();
            }
            w.EndArray();
            w.EndObject();

            w.StartArray("interestingCases");
            for (int i = 0; i < packet.InterestingCases.Count; i++) w.StringArrayItem(packet.InterestingCases[i]);
            w.EndArray();

            w.EndObject();
            return w.ToString();
        }

        // The exact-HTTP file. serializedRequest holds the real object that went to Ollama, embedded
        // as JSON rather than as an escaped string so it can be replayed directly.
        internal static string BuildExactRequestJson(PromptCapturePacket packet)
        {
            if (packet == null) return "{}";
            PromptCaptureJsonWriter w = new PromptCaptureJsonWriter();
            w.StartObject();
            w.Number("schemaVersion", packet.SchemaVersion);
            w.String("sessionId", packet.SessionId);
            w.Number("requestId", packet.RequestId);
            w.Number("correlationRequestId", packet.CorrelationRequestId);
            w.Number("attempt", packet.CorrelationAttempt);
            w.Number("threadId", packet.CorrelationThreadId);
            w.String("candidateHash", packet.CorrelationCandidateHash);
            w.String("disposition", packet.CorrelationDisposition);
            // Single-model invariant check: these two should always match each attempt's own "model"
            // below AND its nested serializedRequest.model. Recorded once at the packet level since
            // they do not vary per attempt.
            w.String("configuredModel", packet.ConfiguredModel);
            w.String("resolvedModel", packet.Model);
            w.StartArray("attempts");
            List<PromptCaptureAttempt> attempts = packet.AttemptsSnapshot();
            for (int i = 0; i < attempts.Count; i++)
            {
                PromptCaptureAttempt a = attempts[i];
                w.StartObject();
                w.Number("attempt", a.Attempt);
                w.String("attemptKind", PromptCaptureAttemptKinds.Name(a.Kind));
                w.String("endpointKind", a.EndpointKind);
                w.String("model", a.Model);
                w.String("inferenceMode", a.InferenceMode);
                w.Bool("stream", a.Stream);
                w.Bool("think", a.Think);
                w.String("keepAlive", a.KeepAlive);
                w.Number("numCtx", a.NumCtx);
                w.Number("temperature", a.Temperature);
                w.Number("numPredict", a.NumPredict);
                if (a.NumGpu != int.MinValue) w.Number("numGpu", a.NumGpu);
                if (a.NumThread > 0) w.Number("numThread", a.NumThread);
                w.RawJson("serializedRequest", a.SerializedRequestJson);
                w.EndObject();
            }
            w.EndArray();
            w.EndObject();
            return w.ToString();
        }

        internal static string BuildResultJson(PromptCapturePacket packet)
        {
            if (packet == null) return "{}";
            PromptCaptureJsonWriter w = new PromptCaptureJsonWriter();
            w.StartObject();
            w.Number("schemaVersion", packet.SchemaVersion);
            w.String("sessionId", packet.SessionId);
            w.Number("requestId", packet.RequestId);

            w.StartArray("attempts");
            List<PromptCaptureAttempt> attempts = packet.AttemptsSnapshot();
            for (int i = 0; i < attempts.Count; i++)
            {
                PromptCaptureAttempt a = attempts[i];
                w.StartObject();
                w.Number("attempt", a.Attempt);
                w.String("attemptKind", PromptCaptureAttemptKinds.Name(a.Kind));
                w.Bool("httpSucceeded", a.HttpSucceeded);
                if (!string.IsNullOrEmpty(a.HttpErrorType)) w.String("httpErrorType", a.HttpErrorType);
                w.StartObject("ollama");
                w.Bool("done", a.Done);
                w.String("doneReason", a.DoneReason);
                w.String("content", a.Content);
                w.String("thinking", a.Thinking);
                w.Number("promptEvalCount", a.PromptEvalCount);
                w.Number("evalCount", a.EvalCount);
                w.Number("totalMs", a.TotalMs);
                w.Number("loadMs", a.LoadMs);
                w.Number("promptEvalMs", a.PromptEvalMs);
                w.Number("evalMs", a.EvalMs);
                w.EndObject();
                w.EndObject();
            }
            w.EndArray();

            // Three distinct texts. Never collapse these: the whole point of the capture is to
            // compare what the model said with what the player actually saw.
            w.String("rawModelContent", packet.RawModelContent);
            w.String("postGuardContent", packet.PostGuardContent);
            w.String("selectedRawContent", packet.RawModelContent);

            w.StartObject("postProcessing");
            w.Number("qualityRetryCount", packet.QualityRetryCount);
            w.StartObject("grounding");
            w.String("decision", packet.GroundingDecision);
            w.String("reason", packet.GroundingReason);
            w.String("reasonCategory", packet.GroundingReasonCategory);
            w.EndObject();
            w.StartObject("roleplayGuard");
            w.Bool("ran", packet.RoleplayGuardRan);
            w.Bool("changed", packet.RoleplayGuardChanged);
            w.Bool("rejected", packet.RoleplayGuardRejected);
            w.String("reasonCategory", packet.RoleplayGuardReasonCategory);
            w.EndObject();
            w.StartObject("fallback");
            w.Bool("used", packet.FallbackUsed);
            w.String("kind", packet.FallbackKind);
            w.EndObject();
            w.EndObject();

            w.StartObject("final");
            w.Bool("displayed", packet.Displayed);
            w.String("source", packet.FinalSource);
            w.String("visibleText", packet.FinalVisibleContent);
            w.Bool("queueAccepted", packet.QueueAccepted);
            w.String("visibilityDisposition", packet.VisibilityDisposition);
            w.StartObject("advancedBeforeVisibility");
            w.Bool("topicFatigueAdvanced", packet.TopicFatigueAdvanced);
            w.Bool("conversationMomentAdded", packet.ConversationMomentAdded);
            w.Bool("preferencePersisted", packet.PreferencePersisted);
            w.Bool("callbackStateAdvanced", packet.CallbackStateAdvanced);
            w.EndObject();
            w.EndObject();

            w.StartArray("interestingCases");
            for (int i = 0; i < packet.InterestingCases.Count; i++) w.StringArrayItem(packet.InterestingCases[i]);
            w.EndArray();

            w.EndObject();
            return w.ToString();
        }

        // One compact line per logical request so specimens can be located without opening packets.
        internal static string BuildIndexLine(PromptCapturePacket packet)
        {
            if (packet == null) return string.Empty;
            PromptCaptureJsonWriter w = new PromptCaptureJsonWriter(0);
            w.StartObject();
            w.Number("requestId", packet.RequestId);
            w.Number("turnId", packet.TurnId);
            w.String("stage", packet.Stage);
            w.String("source", packet.Source);
            w.String("lane", packet.Lane);
            w.String("speaker", packet.SpeakerName);
            w.String("route", packet.EffectiveTurnType);
            w.String("knowledgeNeed", packet.EffectiveKnowledgeNeed);
            w.Bool("retrievalUsed", packet.RetrievalUsed);
            w.String("model", packet.Model);
            w.Number("attempts", packet.AttemptCount);
            w.String("grounding", packet.GroundingDecision);
            w.Bool("displayed", packet.Displayed);
            w.String("visibilityDisposition", packet.VisibilityDisposition);
            if (!string.IsNullOrEmpty(packet.ManualLabel)) w.String("manualLabel", packet.ManualLabel);
            w.StartArray("interestingCases");
            for (int i = 0; i < packet.InterestingCases.Count; i++) w.StringArrayItem(packet.InterestingCases[i]);
            w.EndArray();
            w.String("requestFile", packet.RequestFileName);
            w.String("resultFile", packet.ResultFileName);
            w.EndObject();
            return w.ToString();
        }
    }
}
