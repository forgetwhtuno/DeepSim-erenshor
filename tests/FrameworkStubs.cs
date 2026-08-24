using System.Web.Script.Serialization;

namespace UnityEngine
{
    public static class JsonUtility
    {
        public static T FromJson<T>(string value)
        {
            return new JavaScriptSerializer().Deserialize<T>(value);
        }

        public static string ToJson(object value, bool prettyPrint = false)
        {
            return new JavaScriptSerializer().Serialize(value);
        }
    }

    public class GameObject
    {
        public string name;
        public T GetComponent<T>() where T : class { return null; }
        public T GetComponentInParent<T>() where T : class { return null; }
    }
}


public class SimPlayer
{
    public T GetComponent<T>() where T : class { return null; }
}

public class Stats
{
    public int CurrentHP;
}

public class NPC { }
public class Item { }

namespace ErenshorDeepSims
{
    internal sealed class DeepSimsPlugin
    {
        internal static DeepSimsPlugin Instance;
        internal sealed class BoolConfig { internal bool Value = true; }
        internal sealed class NumberConfig { internal double Value = 60.0; }

        internal readonly BoolConfig DirectorEnabledConfig = new BoolConfig();
        internal readonly BoolConfig EventChatterConfig = new BoolConfig();
        internal readonly NumberConfig EventCooldownSecondsConfig = new NumberConfig();

        internal void NotifyObservedGameEvent(string type, string description, int importance, bool importantMemory, double socialWeight) { }
        internal void NotifyCompletedEncounter(EncounterSnapshot encounter, System.Collections.Generic.List<string> participants, int primaryEnemyKills) { }
        internal double GetEventReactionChance(string type, double chance) { return chance; }
        internal bool QueueVerifiedEventConversation(SocialEventCandidate candidate, out string speaker) { speaker = "A"; return true; }
        internal void LogEventConversationDecision(string type, bool accepted, string reason, string speaker) { }
        internal void NoteSocialPlayerConversation() { }
        internal bool IsSocialSpeakerCoolingDown(string speaker) { return false; }
        internal double GetSocialOpportunityMultiplier() { return 1.0; }
        internal bool TryAdmitAutonomousOpportunity(string type, SocialPriority priority, string semanticKey, bool combat, out string reason) { reason = string.Empty; return true; }
        internal string DescribeSocialBudget() { return "test budget"; }
        internal bool WillUseLlmForAutonomousEvent(string type) { return false; }
        internal static void LogNemesisRoleDiagnostic(string value) { }
    }

    internal static class SimContextReader
    {
        internal static string DescribeClassRole(string className)
        {
            if (string.Equals(className, "Druid", System.StringComparison.OrdinalIgnoreCase)) return "healer";
            if (string.Equals(className, "Paladin", System.StringComparison.OrdinalIgnoreCase)) return "tank";
            return string.Empty;
        }

        internal static string DescribeTyping(SimSnapshot sim) { return "normal chat"; }
        internal static string DescribeHardOutputStyle(SimSnapshot sim) { return "short party chat"; }

        // Mirrors the real SimContextReader.NormalizeClassName (legacy "Duelist" -> current
        // "Windblade" terminology) so PromptBuilder's identity-vs-asked-class cross reference is
        // testable in the standalone harness without pulling in the Unity-dependent reader.
        internal static string NormalizeClassName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown class";
            string clean = value.Trim();
            if (string.Equals(clean, "Duelist", System.StringComparison.OrdinalIgnoreCase)) return "Windblade";
            return clean;
        }
    }
}
