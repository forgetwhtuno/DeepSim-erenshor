using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    internal static class CognitionObservabilityDeterministicTests
    {
        internal static List<string> Run()
        {
            List<string> results = new List<string>();
            try
            {
                SimMemory source = Fixture();
                MemoryCollectionObservation live = MemoryCollectionObservation.Capture(source);
                SimMemory clone = MemoryStore.CloneMemoryForDiagnostics(source);
                MemoryCollectionObservation cloned = MemoryCollectionObservation.Capture(clone);
                Add(results, "collection counts survive writer CloneMemory", live.SameAs(cloned), cloned.Describe());
                Add(results, "writer clone is independent", !object.ReferenceEquals(source, clone) &&
                    !object.ReferenceEquals(source.StructuredMemories, clone.StructuredMemories), "clone retained live references");

                string before = SimMemoryPersistence.Serialize(source);
                JsonFieldPresenceObservation fields = JsonFieldPresenceObservation.Inspect(before);
                MemoryCollectionObservation ignored = MemoryCollectionObservation.Capture(source);
                string after = SimMemoryPersistence.Serialize(source);
                Add(results, "diagnostics do not alter writer snapshot", string.Equals(before, after, StringComparison.Ordinal), "snapshot changed");
                Add(results, "persistence contract emits structured collections", fields.ContainsStructuredMemories &&
                    fields.ContainsRecentEvents && fields.ContainsPreferences && fields.ContainsSimRelationships &&
                    fields.ContainsSocialRelationships && fields.ContainsAuthoredIdentity, fields.Describe());
                Add(results, "instrumentation cannot manufacture persistence success or failure",
                    JsonFieldPresenceObservation.Inspect(before).ContainsStructuredMemories ==
                    JsonFieldPresenceObservation.Inspect(after).ContainsStructuredMemories, "field result changed after observation");

                PromptCapturePacket queued = new PromptCapturePacket { RequestId = 1, QueueAccepted = true,
                    Displayed = false, VisibilityDisposition = "queued" };
                string queuedJson = PromptCapturePacketSerializer.BuildResultJson(queued);
                PromptCaptureScope.RecordVisibility(queued, true, "displayed", "visible line");
                string visibleJson = PromptCapturePacketSerializer.BuildResultJson(queued);
                Add(results, "visibility diagnostics distinguish queue acceptance from final display",
                    queuedJson.Contains("\"queueAccepted\": true") && queuedJson.Contains("\"displayed\": false") &&
                    visibleJson.Contains("\"displayed\": true") && visibleJson.Contains("\"visibilityDisposition\": \"displayed\""),
                    "queued and displayed states collapsed");
            }
            catch (Exception ex) { Add(results, "cognition observability tests threw", false, ex.GetType().Name); }
            return results;
        }

        private static SimMemory Fixture()
        {
            SimMemory memory = new SimMemory { SimKey = "test-sim", Name = "Test" };
            memory.Normalize();
            memory.StructuredMemories.Add(new StructuredMemoryRecord { Id = "record", Text = "private fixture text" });
            memory.RecentEvents.Add(new MemoryEvent { type = "test", text = "private fixture event" });
            memory.Preferences.Add(new SimPreferenceMemory { TopicKey = "zone", Statement = "private fixture preference" });
            memory.SimRelationships.Add(new SimRelationshipMemory { OtherSimKey = "other", OtherName = "Other" });
            memory.SocialRelationships.Add(new SocialRelationshipMemory { OtherSimKey = "other", OtherName = "Other" });
            memory.AuthoredIdentity.CorePersonality = "private fixture identity";
            memory.Normalize();
            return memory;
        }

        private static void Add(List<string> results, string name, bool pass, string detail)
        {
            results.Add("[CognitionObservability] " + name + ": " + (pass ? "PASS" : "FAIL") +
                (pass || string.IsNullOrEmpty(detail) ? string.Empty : " (" + detail + ")"));
        }
    }
}
