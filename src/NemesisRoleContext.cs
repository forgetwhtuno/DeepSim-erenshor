using System;
using System.Reflection;
using System.Text;

namespace ErenshorDeepSims
{
    internal sealed class NemesisRoleContextSnapshot
    {
        internal bool IsActiveNemesis;
        internal int StableSimId = -1;
        internal string NemesisName = string.Empty;
        internal string Stage = string.Empty;
        internal int CompetitiveInteractionCount;
        internal string LatestVerifiedCompetitiveEvent = string.Empty;
        internal string LatestVerifiedResult = string.Empty;
    }

    // Reflection-only sibling integration. The provider is optional and every failed/unknown
    // state fails closed, so Deep Sims has no hard dependency on Nemesis or its load order.
    internal static class NemesisRoleContext
    {
        private static Type _apiType;
        private static MethodInfo _getSnapshot;
        private static int _apiVersion;

        internal static bool TryGetForSpeaker(SimSnapshot speaker, out NemesisRoleContextSnapshot snapshot)
        {
            snapshot = null;
            if (speaker == null || speaker.NativeStableSimId < 0) return false;
            Refresh();
            if (_apiVersion < 1 || _getSnapshot == null) return false;
            try
            {
                object raw = _getSnapshot.Invoke(null, null);
                NemesisRoleContextSnapshot candidate = Read(raw);
                if (candidate == null || !candidate.IsActiveNemesis || candidate.StableSimId < 0) return false;
                // Exact native identity is mandatory. Never identify a Nemesis by display name.
                if (candidate.StableSimId != speaker.NativeStableSimId) return false;
                snapshot = candidate;
                return true;
            }
            catch { return false; }
        }

        internal static void ResetForTests()
        {
            _apiType = null;
            _getSnapshot = null;
            _apiVersion = 0;
        }

        private static void Refresh()
        {
            if (_getSnapshot != null && _apiType != null) return;
            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type type = assembly.GetType("ErenshorNemesis.NemesisRoleContextApi", false);
                    if (type == null) continue;
                    FieldInfo version = type.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static);
                    MethodInfo getter = type.GetMethod("GetActiveRoleSnapshot", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                    int apiVersion = version == null ? 0 : Convert.ToInt32(version.GetValue(null));
                    if (apiVersion < 1 || getter == null) return;
                    _apiType = type;
                    _getSnapshot = getter;
                    _apiVersion = apiVersion;
                    return;
                }
            }
            catch { }
        }

        private static NemesisRoleContextSnapshot Read(object raw)
        {
            if (raw == null) return null;
            Type type = raw.GetType();
            NemesisRoleContextSnapshot value = new NemesisRoleContextSnapshot();
            value.IsActiveNemesis = Bool(type, raw, "IsActiveNemesis");
            value.StableSimId = Int(type, raw, "StableSimId", -1);
            value.NemesisName = Text(type, raw, "NemesisName", 80);
            value.Stage = Text(type, raw, "Stage", 24);
            value.CompetitiveInteractionCount = Math.Max(0, Int(type, raw, "CompetitiveInteractionCount", 0));
            value.LatestVerifiedCompetitiveEvent = Text(type, raw, "LatestVerifiedCompetitiveEvent", 64);
            value.LatestVerifiedResult = Text(type, raw, "LatestVerifiedResult", 64);
            return value;
        }

        private static bool Bool(Type type, object raw, string property)
        { try { PropertyInfo p = type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance); return p != null && Convert.ToBoolean(p.GetValue(raw, null)); } catch { return false; } }
        private static int Int(Type type, object raw, string property, int fallback)
        { try { PropertyInfo p = type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance); return p == null ? fallback : Convert.ToInt32(p.GetValue(raw, null)); } catch { return fallback; } }
        private static string Text(Type type, object raw, string property, int max)
        { try { PropertyInfo p = type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance); string value = Convert.ToString(p == null ? null : p.GetValue(raw, null)) ?? string.Empty; value = value.Replace('\r', ' ').Replace('\n', ' ').Trim(); return value.Length <= max ? value : value.Substring(0, max); } catch { return string.Empty; } }
    }

    internal static class NemesisRolePrompt
    {
        internal static string Render(SimSnapshot speaker, WorldSnapshot world, NemesisRoleContextSnapshot role)
        {
            if (speaker == null || role == null || !role.IsActiveNemesis || speaker.NativeStableSimId < 0 || speaker.NativeStableSimId != role.StableSimId)
                return string.Empty;
            string stage = Stage(role.Stage);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CURRENT SPECIAL ROLE (verified current relationship context):");
            if (stage == "heated") sb.AppendLine("You are this player character's designated heated rival.");
            else if (stage == "rival") sb.AppendLine("You are this player character's established designated rival.");
            else sb.AppendLine("You are this player character's designated new rival; this is an early rivalry with limited competitive history.");
            sb.AppendLine("Rivalry stage: " + stage + ".");
            if (IsCurrentPartyMember(world, speaker)) sb.AppendLine("You are currently cooperating with the player as a party member. Party membership does not erase the rivalry.");
            string record = VerifiedRecord(role);
            if (!string.IsNullOrWhiteSpace(record)) sb.AppendLine("Verified rivalry history: " + record);
            sb.AppendLine("Let rivalry influence your perspective naturally only when relevant. Do not mention it in every message, become generically hostile, or invent additional rivalry history.");
            return sb.ToString().Trim();
        }

        internal static string Diagnostic(SimSnapshot speaker, NemesisRoleContextSnapshot role)
        {
            return "nemesisRoleContext=" + (role != null && role.IsActiveNemesis ? "true" : "false") +
                " speakerStableId=" + (speaker == null ? -1 : speaker.NativeStableSimId) +
                " activeNemesisStableId=" + (role == null ? -1 : role.StableSimId) +
                " stage=" + (role == null || string.IsNullOrWhiteSpace(role.Stage) ? "none" : Stage(role.Stage)) +
                " verifiedRecordCount=" + (role == null ? 0 : Math.Max(0, role.CompetitiveInteractionCount)) +
                " latestEventType=" + (role == null || string.IsNullOrWhiteSpace(role.LatestVerifiedCompetitiveEvent) ? "none" : role.LatestVerifiedCompetitiveEvent);
        }

        private static string VerifiedRecord(NemesisRoleContextSnapshot role)
        {
            string type = (role.LatestVerifiedCompetitiveEvent ?? string.Empty).Trim().ToLowerInvariant();
            string result = (role.LatestVerifiedResult ?? string.Empty).Trim().ToLowerInvariant();
            if (type == "practice_duel_completed")
            {
                if (result == "nemesis_win") return "You won one completed Practice Duel against the player.";
                if (result == "player_win") return "The player won one completed Practice Duel against you.";
            }
            if (type == "pvp_encounter_completed")
            {
                if (result == "nemesis_win") return "You won one verified PvP encounter against the player.";
                if (result == "player_win") return "The player won one verified PvP encounter against you.";
                if (result == "player_fled") return "The player disengaged from one verified PvP encounter.";
                if (result == "enemy_retreated") return "You retreated from one verified PvP encounter.";
            }
            return string.Empty;
        }

        private static string Stage(string value)
        {
            string stage = (value ?? string.Empty).Trim().ToLowerInvariant();
            return stage == "heated" || stage == "rival" ? stage : "new";
        }

        private static bool IsCurrentPartyMember(WorldSnapshot world, SimSnapshot speaker)
        {
            if (world == null || world.Party == null || speaker == null) return false;
            for (int i = 0; i < world.Party.Count; i++)
                if (world.Party[i] != null && world.Party[i].NativeStableSimId >= 0 && world.Party[i].NativeStableSimId == speaker.NativeStableSimId) return true;
            return false;
        }
    }
}
