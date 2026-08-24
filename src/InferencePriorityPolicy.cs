using System;

namespace ErenshorDeepSims
{
    // Pure scheduling/latency policy.  The request scheduler still owns ordering and the shared
    // inference semaphore owns model serialization; this policy only decides which work is allowed
    // to spend extra model calls and whether background curation has any evidence to justify work.
    internal static class InferencePriorityPolicy
    {
        internal static bool AllowsGroundingRetry(string diagnosticSource, bool forceMessage)
        {
            string source = (diagnosticSource ?? string.Empty).Trim().ToLowerInvariant();
            if (source == "organic_current_event") return true;
            if (source == "whisper" || source == "group" || source == "dstalk") return true;
            if (forceMessage && source != "autonomous" && source != "verified_event" &&
                source != "vanilla_continuation" && source != "conversation_continuation") return true;
            return false;
        }

        internal static bool IsLowPrioritySource(string diagnosticSource)
        {
            string source = (diagnosticSource ?? string.Empty).Trim().ToLowerInvariant();
            return source == "autonomous" || source == "verified_event" ||
                   source == "vanilla_continuation" || source == "conversation_continuation";
        }

        internal static bool ShouldRunCuration(int pendingEvidenceCount)
        {
            return pendingEvidenceCount > 0;
        }
    }
}
