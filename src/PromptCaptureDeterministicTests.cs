using System;
using System.Collections.Generic;
using System.IO;

namespace ErenshorDeepSims
{
    // Deterministic coverage for the local prompt-capture instrumentation. Everything here runs
    // against a temporary directory and never touches the real Deep Sims data root.
    internal static class PromptCaptureDeterministicTests
    {
        private static List<string> _results;

        internal static List<string> Run()
        {
            _results = new List<string>();
            string root = Path.Combine(Path.GetTempPath(), "DeepSimsPromptCaptureTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                CaptureOffWritesNothing(root);
                BackgroundStagesRespectCapturePolicyAndSerialize(root);
                CaptureOnWritesBoundedSemanticPacket(root);
                ExactRequestMatchesSubmittedBody(root);
                SingleModelInvariantVisibleInPacket(root);
                RetriesShareOneLogicalRequestId(root);
                FlattenedFallbackIsCapturedExactly(root);
                RawContentStaysDistinctFromVisible(root);
                RejectionKeepsRawCandidate(root);
                ClassifierRecordsRawEffectiveAndOverride();
                MemoryCaptureHoldsOnlySelected(root);
                RetrievalHoldsOnlyBoundedEvidence(root);
                ConnectedSimBReferencesAcceptedVisibleA(root);
                PacketHasNoPrivateAbsolutePath(root);
                CaptureErrorsFailOpen();
                MaxCaptureLimitStopsAndWarns(root);
                InterestingCaseTagging();
            }
            catch (Exception ex)
            {
                Add("prompt capture tests threw", false, ex.GetType().Name);
            }
            finally
            {
                PromptCapture.ResetForTests();
                TryDeleteTree(root);
            }
            return _results;
        }

        // 1. capture OFF => no packet files and no behavior change.
        private static void CaptureOffWritesNothing(string root)
        {
            PromptCapture.ResetForTests();
            string dir = Path.Combine(root, "off");
            PromptCapturePacket packet = PromptCapture.TryBegin("direct_party_reply", "player_reply", false);
            Add("capture off yields no packet", packet == null, packet == null ? string.Empty : "packet was created");
            PromptCaptureScope.RecordRawModelContent("should not be recorded");
            PromptCaptureScope.RecordFinal(true, "LLM", "should not be recorded");
            bool nothingOnDisk = !Directory.Exists(dir) || Directory.GetFiles(dir).Length == 0;
            Add("capture off writes no files", nothingOnDisk, "files were written while disabled");
        }

        private static void BackgroundStagesRespectCapturePolicyAndSerialize(string root)
        {
            string[] stages = { "context_pulse", "autonomous_opener", "session_reflection", "social_curation" };
            PromptCapture.ResetForTests();
            bool allOff = true;
            for (int i = 0; i < stages.Length; i++)
                if (PromptCaptureScope.Begin(stages[i], "background") != null) allOff = false;
            Add("new background scopes remain disabled with capture off", allOff, "a background scope bypassed opt-in policy");

            StartSession(root, "background-stages", 20, true);
            bool allSerialized = true;
            for (int i = 0; i < stages.Length; i++)
            {
                PromptCaptureLease lease = PromptCaptureScope.Begin(stages[i], "background");
                if (lease == null) { allSerialized = false; continue; }
                PromptCaptureScope.DescribeBackground(i == 3 ? "Curation" : i == 2 ? "Reflection" : "Autonomous",
                    7, 11, "event-42", "correlation-42");
                PromptCaptureScope.DescribeSeed("verified_event", "topic_key", "campmaster", string.Empty,
                    string.Empty, true, false, false);
                PromptCaptureScope.RecordQueueAccepted(i == 1);
                string json = PromptCapturePacketSerializer.BuildSemanticRequestJson(lease.Packet);
                string result = PromptCapturePacketSerializer.BuildResultJson(lease.Packet);
                allSerialized = allSerialized && json.Contains("\"stage\": \"" + stages[i] + "\"") &&
                    json.Contains("\"lane\":") && json.Contains("\"conversationGeneration\": 11") &&
                    json.Contains("\"verifiedSourceId\": \"event-42\"") &&
                    result.Contains("\"visibilityDisposition\":");
                lease.Dispose();
            }
            Add("new background scopes serialize through the existing packet format", allSerialized,
                "stage or lifecycle metadata missing");
        }

        // 2. capture ON => semantic request packet contains the expected bounded fields.
        private static void CaptureOnWritesBoundedSemanticPacket(string root)
        {
            string dir = StartSession(root, "semantic", 100, true);
            PromptCapturePacket packet = PromptCapture.TryBegin("direct_party_reply", "player_reply", false);
            if (packet == null) { Add("capture on yields a packet", false, "no packet"); return; }
            packet.SpeakerName = "Dancer";
            packet.SpeakerClass = "Windblade";
            packet.SpeakerLevel = 12;
            packet.EffectiveTurnType = "PersonalPreference";
            packet.EffectiveKnowledgeNeed = "None";
            packet.WorldZone = "Braewick";
            packet.SessionSummary = "The party fought militia in Brake.";
            packet.Messages.Add(new PromptCaptureMessage("system", "You are Dancer."));
            packet.Messages.Add(new PromptCaptureMessage("user", "do you like being a windblade?"));
            string json = PromptCapturePacketSerializer.BuildSemanticRequestJson(packet);
            bool ok = json.Contains("\"stage\": \"direct_party_reply\"") &&
                      json.Contains("\"source\": \"player_reply\"") &&
                      json.Contains("\"name\": \"Dancer\"") &&
                      json.Contains("\"class\": \"Windblade\"") &&
                      json.Contains("\"turnType\": \"PersonalPreference\"") &&
                      json.Contains("\"zone\": \"Braewick\"") &&
                      json.Contains("do you like being a windblade?") &&
                      json.Contains("\"requestId\": 1");
            Add("semantic packet carries bounded expected fields", ok, "missing expected field");
            PromptCapture.Complete(packet);
            Add("semantic packet reaches disk", WaitForFile(Path.Combine(dir, "000001-request.json")), "request file not written");
        }

        // 3. exact serialized request capture matches what was submitted to Ollama.
        private static void ExactRequestMatchesSubmittedBody(string root)
        {
            StartSession(root, "exact", 100, true);
            PromptCapturePacket packet = PromptCapture.TryBegin("direct_party_reply", "player_reply", false);
            if (packet == null) { Add("exact request packet begins", false, "no packet"); return; }
            const string submitted = "{\"model\":\"qwen3.5:4b\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]," +
                "\"stream\":false,\"think\":false,\"keep_alive\":\"10m\",\"options\":{\"num_ctx\":2048,\"temperature\":0.6,\"num_predict\":72}}";
            PromptCaptureAttempt attempt = packet.BeginAttempt(PromptCaptureAttemptKind.Primary);
            attempt.Model = "qwen3.5:4b";
            attempt.Stream = false;
            attempt.Think = false;
            attempt.KeepAlive = "10m";
            attempt.NumCtx = 2048;
            attempt.Temperature = 0.60f;
            attempt.NumPredict = 72;
            attempt.SerializedRequestJson = submitted;
            string json = PromptCapturePacketSerializer.BuildExactRequestJson(packet);
            bool ok = json.Contains(submitted) &&
                      json.Contains("\"model\": \"qwen3.5:4b\"") &&
                      json.Contains("\"stream\": false") &&
                      json.Contains("\"think\": false") &&
                      json.Contains("\"keepAlive\": \"10m\"") &&
                      json.Contains("\"numCtx\": 2048") &&
                      json.Contains("\"numPredict\": 72");
            Add("exact serialized request is embedded verbatim with matching options", ok, "serialized body mismatch");
        }

        // Single-model invariant: configuredModel, resolvedModel, and serializedRequest.model must
        // all agree in a healthy session, and the packet/serializer must actually expose all three
        // rather than only the resolved one.
        private static void SingleModelInvariantVisibleInPacket(string root)
        {
            StartSession(root, "singlemodel", 100, true);
            PromptCapturePacket packet = PromptCapture.TryBegin("semantic_classifier", "semantic_classifier", true);
            if (packet == null) { Add("single-model packet begins", false, "no packet"); return; }
            packet.ConfiguredModel = "qwen3.5:4b";
            packet.Model = "qwen3.5:4b";
            PromptCaptureAttempt attempt = packet.BeginAttempt(PromptCaptureAttemptKind.Primary);
            attempt.Model = "qwen3.5:4b";
            attempt.SerializedRequestJson = "{\"model\":\"qwen3.5:4b\",\"messages\":[]}";

            string semanticJson = PromptCapturePacketSerializer.BuildSemanticRequestJson(packet);
            bool semanticOk = semanticJson.Contains("\"configuredModel\": \"qwen3.5:4b\"") &&
                              semanticJson.Contains("\"resolvedModel\": \"qwen3.5:4b\"") &&
                              semanticJson.Contains("\"model\": \"qwen3.5:4b\"");
            Add("semantic packet exposes configuredModel and resolvedModel alongside model", semanticOk, "field missing");

            string exactJson = PromptCapturePacketSerializer.BuildExactRequestJson(packet);
            bool exactOk = exactJson.Contains("\"configuredModel\": \"qwen3.5:4b\"") &&
                          exactJson.Contains("\"resolvedModel\": \"qwen3.5:4b\"") &&
                          exactJson.Contains("qwen3.5:4b\",\"messages\"");
            Add("exact-request packet exposes configuredModel/resolvedModel beside serializedRequest.model", exactOk, "field missing");

            // A regression that let a call site diverge from the canonical resolver would show up as
            // configuredModel != resolvedModel in a real capture; prove the fields are independent
            // enough to actually catch that (not silently forced equal by the serializer).
            packet.ConfiguredModel = "qwen3.5:2b";
            string divergedJson = PromptCapturePacketSerializer.BuildSemanticRequestJson(packet);
            bool detectsDivergence = divergedJson.Contains("\"configuredModel\": \"qwen3.5:2b\"") &&
                                     divergedJson.Contains("\"resolvedModel\": \"qwen3.5:4b\"");
            Add("configuredModel/resolvedModel divergence would be visible in a packet", detectsDivergence, "divergence not distinguishable");
        }

        // 4. retries remain under the same logical request id.
        private static void RetriesShareOneLogicalRequestId(string root)
        {
            StartSession(root, "retries", 100, true);
            PromptCapturePacket packet = PromptCapture.TryBegin("direct_party_reply", "player_reply", false);
            if (packet == null) { Add("retry packet begins", false, "no packet"); return; }
            packet.BeginAttempt(PromptCaptureAttemptKind.Primary);
            packet.BeginAttempt(PromptCaptureAttemptKind.PostLoadRetry);
            packet.BeginAttempt(PromptCaptureAttemptKind.ExpandedBudget);
            List<PromptCaptureAttempt> attempts = packet.AttemptsSnapshot();
            bool ok = attempts.Count == 3 && attempts[0].Attempt == 1 && attempts[1].Attempt == 2 && attempts[2].Attempt == 3 &&
                      PromptCaptureAttemptKinds.Name(attempts[1].Kind) == "post_load_retry" &&
                      PromptCaptureAttemptKinds.Name(attempts[2].Kind) == "expanded_budget";
            Add("all retry attempts share one logical request id", ok, "attempt numbering or kinds wrong");
            Add("retry attempts do not allocate new request ids", packet.RequestId == 1, "request id changed across attempts");
        }

        // 5. the flattened compatibility fallback is captured exactly.
        private static void FlattenedFallbackIsCapturedExactly(string root)
        {
            StartSession(root, "flattened", 100, true);
            PromptCapturePacket packet = PromptCapture.TryBegin("direct_party_reply", "player_reply", false);
            if (packet == null) { Add("flattened packet begins", false, "no packet"); return; }
            const string flattened = "Follow the character/context instructions below...\n[SYSTEM]\nYou are Dancer.\n\n[RESPONSE]\n";
            PromptCaptureAttempt attempt = packet.BeginAttempt(PromptCaptureAttemptKind.FlattenedFallback);
            attempt.Messages.Add(new PromptCaptureMessage("user", flattened));
            attempt.SerializedRequestJson = "{\"model\":\"m\",\"messages\":[{\"role\":\"user\",\"content\":\"flattened\"}]}";
            string json = PromptCapturePacketSerializer.BuildExactRequestJson(packet);
            bool ok = json.Contains("\"attemptKind\": \"flattened_fallback\"") && json.Contains("flattened");
            Add("flattened fallback prompt is captured as its own attempt", ok, "flattened attempt missing");
        }

        // 6 + 7. raw model content, post-guard content, and final visible text stay separate, and a
        // rejection keeps the raw candidate plus a reason category.
        private static void RawContentStaysDistinctFromVisible(string root)
        {
            StartSession(root, "raw", 100, true);
            PromptCapturePacket packet = PromptCapture.TryBegin("direct_party_reply", "player_reply", false);
            if (packet == null) { Add("raw/final packet begins", false, "no packet"); return; }
            packet.RawModelContent = "We cleared that dragon again.";
            packet.PostGuardContent = string.Empty;
            packet.FinalVisibleContent = "not sure what you mean";
            packet.GroundingDecision = "rejected";
            packet.GroundingReason = "unsupported kill/clear assertion";
            packet.GroundingReasonCategory = PromptCaptureReasonCategory.Classify(packet.GroundingReason);
            string json = PromptCapturePacketSerializer.BuildResultJson(packet);
            bool ok = json.Contains("\"rawModelContent\": \"We cleared that dragon again.\"") &&
                      json.Contains("\"visibleText\": \"not sure what you mean\"") &&
                      json.Contains("\"decision\": \"rejected\"");
            Add("raw model content stays distinct from final visible text", ok, "raw/final were collapsed");
        }

        private static void RejectionKeepsRawCandidate(string root)
        {
            StartSession(root, "reject", 100, true);
            PromptCapturePacket packet = PromptCapture.TryBegin("direct_party_reply", "player_reply", false);
            if (packet == null) { Add("rejection packet begins", false, "no packet"); return; }
            packet.RawModelContent = "We looted the crown from that boss.";
            packet.GroundingDecision = "rejected";
            packet.GroundingReason = "unsupported loot acquisition claim";
            packet.GroundingReasonCategory = PromptCaptureReasonCategory.Classify(packet.GroundingReason);
            packet.FinalVisibleContent = string.Empty;
            string json = PromptCapturePacketSerializer.BuildResultJson(packet);
            bool ok = json.Contains("We looted the crown from that boss.") &&
                      json.Contains("\"reasonCategory\": \"loot_acquisition\"");
            Add("grounding rejection records reason category without losing the raw candidate", ok, "raw candidate or category lost");

            Add("reason categories map the known historical failures",
                PromptCaptureReasonCategory.Classify("topic mismatch for selected other_sim_preference") == "topic_mismatch" &&
                PromptCaptureReasonCategory.Classify("unsupported kill claim") == "kill_clear" &&
                PromptCaptureReasonCategory.Classify("entity relationship not supported") == "entity_relationship" &&
                PromptCaptureReasonCategory.Classify(null) == "none", "reason category mapping wrong");
        }

        // 8. classifier packet stores raw result, effective route, and the deterministic override
        // separately. Exercises the real production router, not a stand-in.
        private static void ClassifierRecordsRawEffectiveAndOverride()
        {
            SemanticTurnRoute route;
            SemanticTurnRouter.SemanticRouteTrace trace = new SemanticTurnRouter.SemanticRouteTrace();
            // The model classifies an explicit personal-taste question as a wiki lookup; the
            // deterministic override must correct it while the raw answer is still recorded.
            const string rawClassifierOutput =
                "TurnType=Opinion\nKnowledgeNeed=GameWiki\nTopic=windblade\nSubject=Windblade\nSearchQuery=Windblade\nConfidence=0.90\nDirectAnswerRequired=true";
            bool parsed = SemanticTurnRouter.TryParse(rawClassifierOutput, "Dancer, what do you think about being a Windblade?", out route, trace);
            bool ok = parsed && trace.HasRawClassifier &&
                      trace.RawKnowledgeNeed == KnowledgeNeed.GameWiki &&
                      route.KnowledgeNeed == KnowledgeNeed.None &&
                      trace.Corrections.Contains("ApplyMeaningOverride");
            Add("classifier trace keeps raw GameWiki separate from effective None", ok,
                parsed ? "raw/effective/override not recorded correctly" : "classifier output did not parse");

            // Production behaviour must be identical with and without the diagnostic trace.
            SemanticTurnRoute untraced;
            bool parsedUntraced = SemanticTurnRouter.TryParse(rawClassifierOutput, "Dancer, what do you think about being a Windblade?", out untraced);
            bool unchanged = parsedUntraced && untraced.TurnType == route.TurnType &&
                             untraced.KnowledgeNeed == route.KnowledgeNeed &&
                             untraced.SocialIntent == route.SocialIntent;
            Add("tracing the classifier does not change the effective route", unchanged, "traced and untraced routes differ");
        }

        // 9. memory capture contains only the memory selected for this prompt.
        private static void MemoryCaptureHoldsOnlySelected(string root)
        {
            StartSession(root, "memory", 100, true);
            PromptCapturePacket packet = PromptCapture.TryBegin("direct_party_reply", "player_reply", false);
            if (packet == null) { Add("memory packet begins", false, "no packet"); return; }
            packet.SelectedVerifiedMemory.Add(new PromptCaptureMemoryItem("VerifiedWorld", "The party fought militia in Brake."));
            packet.MemoryCandidateCount = 15;
            packet.MemorySelectedCount = 1;
            string json = PromptCapturePacketSerializer.BuildSemanticRequestJson(packet);
            bool ok = json.Contains("The party fought militia in Brake.") &&
                      json.Contains("\"memoryCandidateCount\": 15") &&
                      json.Contains("\"memorySelectedCount\": 1") &&
                      !json.Contains("rejected-memory");
            Add("memory capture stores only selected memory plus counts", ok, "unselected memory leaked or counts missing");
        }

        // 10. retrieval contains only the bounded evidence passed to the model.
        private static void RetrievalHoldsOnlyBoundedEvidence(string root)
        {
            StartSession(root, "retrieval", 100, true);
            PromptCapturePacket packet = PromptCapture.TryBegin("direct_party_reply", "player_reply", false);
            if (packet == null) { Add("retrieval packet begins", false, "no packet"); return; }
            packet.RetrievalUsed = true;
            packet.RetrievalKind = "GameWiki";
            packet.RetrievalSourceLabel = "Erenshor community wiki";
            packet.RetrievalQuery = "Windblade Abilities";
            packet.RetrievalFound = true;
            packet.RetrievalEvidence = "Windblades gain Gale Step at level 10.";
            string json = PromptCapturePacketSerializer.BuildSemanticRequestJson(packet);
            bool ok = json.Contains("\"kind\": \"GameWiki\"") &&
                      json.Contains("Windblades gain Gale Step at level 10.") &&
                      json.Contains("\"query\": \"Windblade Abilities\"");
            Add("retrieval capture records the bounded evidence and its source kind", ok, "retrieval evidence missing");
        }

        // 11. Sim-to-Sim B references the exact accepted visible A text, even when A's model
        // candidate was rejected and a template opener became visible.
        private static void ConnectedSimBReferencesAcceptedVisibleA(string root)
        {
            StartSession(root, "connected", 100, true);
            PromptCapturePacket a = PromptCapture.TryBegin("direct_party_reply", "player_reply", false);
            if (a == null) { Add("connected A packet begins", false, "no packet"); return; }
            a.RawModelContent = "We cleared that dragon again.";
            a.GroundingDecision = "rejected";
            a.FinalVisibleContent = "anyway, what's next?";
            PromptCapture.Complete(a);

            PromptCapturePacket b = PromptCapture.TryBegin("connected_sim_reply", "sim_to_sim", false);
            if (b == null) { Add("connected B packet begins", false, "no packet"); return; }
            b.ConnectedSimTurn = true;
            b.ParentRequestId = a.RequestId;
            b.ConversationTurnIndex = 1;
            b.PreviousSpeaker = "Dancer";
            b.PreviousRawCandidate = a.RawModelContent;
            b.PreviousAcceptedVisibleText = a.FinalVisibleContent;
            string json = PromptCapturePacketSerializer.BuildSemanticRequestJson(b);
            bool ok = json.Contains("\"previousAcceptedVisibleText\": \"anyway, what's next?\"") &&
                      json.Contains("\"previousRawCandidate\": \"We cleared that dragon again.\"") &&
                      json.Contains("\"parentRequestId\": " + a.RequestId);
            Add("connected Sim B is tied to A's accepted visible text, not A's rejected candidate", ok,
                "B linkage missing or pointed at the rejected candidate");
        }

        // 12. no private absolute path is serialized into a packet.
        private static void PacketHasNoPrivateAbsolutePath(string root)
        {
            string dir = StartSession(root, "paths", 100, true);
            PromptCapturePacket packet = PromptCapture.TryBegin("direct_party_reply", "player_reply", false);
            if (packet == null) { Add("path packet begins", false, "no packet"); return; }
            packet.SessionSummary = "saved under C:\\Users\\someone\\AppData\\DeepSims and /home/someone/deepsims";
            packet.Messages.Add(new PromptCaptureMessage("system", "token api_key=abcdef123456 trailing"));
            string json = PromptCapturePacketSerializer.BuildSemanticRequestJson(packet);
            bool ok = json.IndexOf("C:\\\\Users\\\\someone", StringComparison.OrdinalIgnoreCase) < 0 &&
                      json.IndexOf("/home/someone", StringComparison.OrdinalIgnoreCase) < 0 &&
                      json.IndexOf("abcdef123456", StringComparison.Ordinal) < 0 &&
                      json.Contains(PromptCaptureRedaction.Redacted);
            Add("packets redact absolute user paths and secret-shaped values", ok, "private path or secret survived");

            string label = PromptCapture.RelativeDirectoryLabel();
            bool labelSafe = label.IndexOf(":", StringComparison.Ordinal) < 0 &&
                             label.IndexOf("Users", StringComparison.OrdinalIgnoreCase) < 0;
            Add("reported capture directory is a relative label only", labelSafe, "absolute path reported: " + label);

            bool endpointSafe = PromptCaptureRedaction.EndpointKind("http://localhost:11434/api/chat") == "ollama_chat" &&
                                PromptCaptureRedaction.EndpointKind("http://10.0.0.5:11434/api/chat") == "ollama_chat_remote";
            Add("endpoints are recorded as a kind rather than a URL", endpointSafe, "endpoint kind mapping wrong");
            TryDeleteTree(dir);
        }

        // 13. capture errors fail open: an unwritable directory must not throw into the pipeline.
        private static void CaptureErrorsFailOpen()
        {
            bool threw = false;
            try
            {
                PromptCaptureWriter writer = new PromptCaptureWriter("\0:/definitely/not/a/valid/path", delegate { });
                PromptCapturePacket packet = new PromptCapturePacket();
                packet.RequestId = 7;
                writer.WritePacket(packet, true);
            }
            catch (Exception) { threw = true; }
            Add("a failing packet write never throws into the caller", !threw, "diagnostic write threw");

            bool startThrew = false;
            try { PromptCapture.Start("\0:/bad", "\0:/bad", 10, true, null); }
            catch (Exception) { startThrew = true; }
            Add("a failing capture start never throws", !startThrew, "capture start threw");
            PromptCapture.ResetForTests();
        }

        // 14. the max-capture limit stops capture and warns exactly once.
        private static void MaxCaptureLimitStopsAndWarns(string root)
        {
            List<string> warnings = new List<string>();
            string dir = Path.Combine(root, "limit");
            PromptCapture.ResetForTests();
            PromptCapture.Start(dir, root, 2, true, delegate(string line) { warnings.Add(line); });
            PromptCapturePacket first = PromptCapture.TryBegin("s", "src", false);
            PromptCapturePacket second = PromptCapture.TryBegin("s", "src", false);
            PromptCapturePacket third = PromptCapture.TryBegin("s", "src", false);
            PromptCapturePacket fourth = PromptCapture.TryBegin("s", "src", false);
            bool ok = first != null && second != null && third == null && fourth == null;
            Add("capture stops at the configured maximum", ok, "limit not enforced");

            int limitWarnings = 0;
            for (int i = 0; i < warnings.Count; i++)
                if (warnings[i].IndexOf("reached configured maximum", StringComparison.OrdinalIgnoreCase) >= 0) limitWarnings++;
            Add("the limit warns exactly once rather than spamming", limitWarnings == 1, "limit warnings=" + limitWarnings);

            PromptCaptureState state = PromptCapture.State;
            Add("reaching the limit does not delete collected evidence",
                state != null && state.CapturedLogicalRequests == 2, "captured count changed after limit");
            PromptCapture.ResetForTests();
        }

        private static void InterestingCaseTagging()
        {
            List<string> opinionWithRetrieval = PromptCaptureInterestingCases.Derive("direct_party_reply",
                "Opinion", "PersonalPreference", "GameWiki", "None", true, true, "accepted", "none", false);
            bool ok = opinionWithRetrieval.Contains("opinion_with_retrieval") &&
                      opinionWithRetrieval.Contains("opinion_knowledge_override");
            Add("opinion-with-retrieval specimens are tagged", ok, "expected tags missing");

            List<string> rejection = PromptCaptureInterestingCases.Derive("direct_party_reply",
                "Statement", "Statement", "None", "None", false, false, "rejected", "kill_clear", false);
            Add("kill/clear rejections are tagged", rejection.Contains("grounding_reject_kill_clear"), "kill/clear tag missing");

            List<string> banter = PromptCaptureInterestingCases.Derive("connected_sim_reply",
                "Statement", "Statement", "None", "None", false, false, "accepted", "none", true);
            Add("connected Sim banter is tagged", banter.Contains("connected_sim_banter"), "banter tag missing");
        }

        // ---- helpers ------------------------------------------------------------------------

        private static string StartSession(string root, string name, int max, bool includeClassifier)
        {
            PromptCapture.ResetForTests();
            string dir = Path.Combine(root, name);
            PromptCapture.Start(dir, root, max, includeClassifier, delegate { });
            return dir;
        }

        private static bool WaitForFile(string path)
        {
            // Packets are written on the thread pool; give the write a bounded moment to land.
            for (int i = 0; i < 100; i++)
            {
                try
                {
                    PromptCaptureWriter writer = PromptCapture.WriterForTests;
                    if (writer != null && writer.CountRequestFiles() > 0) return true;
                }
                catch { }
                System.Threading.Thread.Sleep(20);
            }
            return false;
        }

        private static void TryDeleteTree(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch { }
        }

        private static void Add(string name, bool ok, string detail)
        {
            _results.Add("[DeepSims PromptCapture] " + name + ": " + (ok ? "PASS" : "FAIL" + (string.IsNullOrEmpty(detail) ? string.Empty : " - " + detail)));
        }
    }
}
