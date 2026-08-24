using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ErenshorDeepSims
{
    internal static class DuelV4ContractPolicy
    {
        internal const int SupportedContractVersion = 4;
        internal static bool AcceptsContract(int version) { return version == SupportedContractVersion; }

        internal static string Fingerprint(string requestId, string duelId, string eventType, string decision,
            string outcome, string winner, string yielded, string reasonToken)
        {
            string correlation = string.IsNullOrWhiteSpace(duelId) ? (requestId ?? string.Empty) : duelId;
            return correlation + "|" + (eventType ?? string.Empty) + "|" + (decision ?? string.Empty) + "|" +
                   (outcome ?? string.Empty) + "|" + (winner ?? string.Empty) + "|" + (yielded ?? string.Empty) + "|" + (reasonToken ?? string.Empty);
        }

        internal static string BuildCompletedFact(string participantA, string participantB, string opponent,
            string winner, string yielded, string playerName)
        {
            string displayPlayer = string.IsNullOrWhiteSpace(playerName) ? "the player" : playerName.Trim();
            string left = Display(participantA, displayPlayer, displayPlayer);
            string right = Display(participantB, displayPlayer, string.IsNullOrWhiteSpace(opponent) ? "a nearby Sim" : opponent.Trim());
            string displayWinner = Display(winner, displayPlayer, winner);
            string displayYielded = yielded;
            if (string.Equals(yielded, "player", StringComparison.OrdinalIgnoreCase)) displayYielded = displayPlayer;
            else if (string.Equals(yielded, "opponent", StringComparison.OrdinalIgnoreCase)) displayYielded = right;
            string defeated = string.Equals(displayWinner, left, StringComparison.OrdinalIgnoreCase) ? right : left;
            if (!string.IsNullOrWhiteSpace(displayWinner) && !string.IsNullOrWhiteSpace(displayYielded))
                return displayWinner + " defeated " + defeated + " in a friendly Practice Duel. " + displayYielded + " yielded.";
            return left + " and " + right + " completed a friendly Practice Duel.";
        }

        private static string Display(string value, string playerName, string fallback)
        {
            if (string.Equals(value, "player", StringComparison.OrdinalIgnoreCase)) return playerName;
            return string.IsNullOrWhiteSpace(value) ? (fallback ?? string.Empty) : value.Trim();
        }
    }

    internal static class SocialWitnessPolicy
    {
        internal static bool IsLocalSceneWitness(bool runtimeLoaded, string simScene, string activeScene)
        {
            // Witnessing is a positive local fact, not an inference from save-data presence.
            // A real current SimSnapshot supplies the active scene when its runtime actor is loaded,
            // so missing scene evidence must fail closed rather than becoming same-scene knowledge.
            if (!runtimeLoaded || string.IsNullOrWhiteSpace(activeScene) || string.IsNullOrWhiteSpace(simScene)) return false;
            return string.Equals(simScene.Trim(), activeScene.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class LiveSocialAddressability
    {
        internal static bool IsAddressable(bool runtimeLoaded, string simScene, string activeScene, bool currentNativePartyMember)
        {
            return currentNativePartyMember && SocialWitnessPolicy.IsLocalSceneWitness(runtimeLoaded, simScene, activeScene);
        }

        internal static bool HasStaleDirectAddress(string text, IList<string> knownSubjects, IList<string> currentPresence,
            string playerName, string speakerName, out string staleTarget)
        {
            staleTarget = string.Empty;
            if (string.IsNullOrWhiteSpace(text) || knownSubjects == null) return false;
            string candidate = DirectAddressCandidate(text);
            if (candidate.Length == 0 || Same(candidate, playerName) || Same(candidate, speakerName)) return false;
            bool known = Contains(knownSubjects, candidate);
            if (!known) return false;
            if (Contains(currentPresence, candidate)) return false;
            staleTarget = candidate;
            return true;
        }

        internal static string DirectAddressCandidate(string text)
        {
            Match vocative = Regex.Match(text ?? string.Empty, @"^\s*([A-Za-z][A-Za-z0-9'_-]{1,30})\s*[,;:]\s*");
            if (vocative.Success) return vocative.Groups[1].Value;
            Match question = Regex.Match(text ?? string.Empty,
                @"^\s*([A-Za-z][A-Za-z0-9'_-]{1,30})\s+(?:do|does|did|are|is|would|will|can|could|what|where|when|why|how)\b",
                RegexOptions.IgnoreCase);
            return question.Success ? question.Groups[1].Value : string.Empty;
        }

        private static bool Contains(IList<string> values, string candidate)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Count; i++) if (Same(values[i], candidate)) return true;
            return false;
        }

        private static bool Same(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
                string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class IdentityEditorLayoutMetrics
    {
        internal float Left;
        internal float Right;
        internal float Bottom;
        internal float Top;
        internal float Width;
        internal float Height;
    }

    internal static class IdentityEditorLayoutPolicy
    {
        internal const float LeftMarginFraction = .04f;
        internal const float RightMarginFraction = .04f;
        internal const float BottomMarginFraction = .05f;
        internal const float TopMarginFraction = .05f;

        internal static IdentityEditorLayoutMetrics Compute(float screenWidth, float screenHeight)
        {
            float w = Math.Max(1f, screenWidth); float h = Math.Max(1f, screenHeight);
            IdentityEditorLayoutMetrics m = new IdentityEditorLayoutMetrics();
            m.Left = w * LeftMarginFraction; m.Right = w * (1f - RightMarginFraction);
            m.Bottom = h * BottomMarginFraction; m.Top = h * (1f - TopMarginFraction);
            m.Width = m.Right - m.Left; m.Height = m.Top - m.Bottom;
            return m;
        }
    }
}
