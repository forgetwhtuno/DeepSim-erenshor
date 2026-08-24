using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    internal static class StandaloneRegressionMain
    {
        private static int Main()
        {
            int result = DeterministicRegressionTests.RunToConsole();
            result = PrintAndCheck(GroundingGuard.RunSelfTests(), result);
            result = PrintAndCheck(ReplyCompletenessGuard.RunSelfTests(), result);
            result = PrintAndCheck(DuelSocialSemantics.RunSelfTests(), result);
            result = PrintAndCheck(RelationshipModel.RunSelfTests(), result);
            result = PrintAndCheck(ChatRoutingRegression.Run(), result);
            result = PrintAndCheck(QualityReliabilityDeterministicTests.Run(), result);
            result = PrintAndCheck(ConversationTurnGuardTests.Run(), result);
            result = PrintAndCheck(ConversationPacingTests.Run(), result);
            result = PrintAndCheck(SocialSituationDeterministicTests.Run(), result);
            result = PrintAndCheck(AutonomousSocialSchedulerDeterministicTests.Run(), result);
            result = PrintAndCheck(ConversationSeedRuntimeDeterministicTests.Run(), result);
            result = PrintAndCheck(RoleplayDeterministicTests.RunSelfTests(), result);
            result = PrintAndCheck(CharacterScopeDeterministicTests.Run(), result);
            result = PrintAndCheck(DeepSimsControlPolicyTests.Run(), result);
            result = PrintAndCheck(DiagnosticPrivacyTests.Run(), result);
            result = PrintAndCheck(LivePartyGroundingTests.Run(), result);
            result = PrintAndCheck(SocialOverhaulDeterministicTests.Run(), result);
            result = PrintAndCheck(PromptCaptureDeterministicTests.Run(), result);
            result = PrintAndCheck(CognitionObservabilityDeterministicTests.Run(), result);
            result = PrintAndCheck(SimMemoryPersistenceDeterministicTests.Run(), result);
            result = PrintAndCheck(DeepSimsModelResolutionTests.Run(), result);
            result = PrintAndCheck(ShouldReplyDeterministic.RunSelfTests(), result);
            result = PrintAndCheck(SimResponseDecision.RunSelfTests(), result);
            result = PrintAndCheck(RecentEventQuestionPolicy.RunSelfTests(), result);
            result = PrintAndCheck(IdentityContextDeterministicTests.Run(), result);
            result = PrintAndCheck(LiveSocialQualityDeterministicTests.Run(), result);
            result = PrintAndCheck(RecentLifeCurrentEventsDeterministicTests.Run(), result);
            return result;
        }

        private static int PrintAndCheck(List<string> lines, int result)
        {
            if (lines == null) return 1;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i] ?? string.Empty;
                Console.WriteLine(line);
                if (line.IndexOf(" FAIL]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    line.IndexOf(": FAIL", StringComparison.OrdinalIgnoreCase) >= 0) result = 1;
            }
            return result;
        }
    }
}
