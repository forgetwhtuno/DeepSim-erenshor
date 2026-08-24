using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace ErenshorDeepSims
{
    internal sealed class HistoricalFriendAvailability
    {
        internal string Name;
        internal string StableId;
        internal bool Online;
    }

    internal interface IHistoricalFriendAvailability
    {
        bool TrySnapshot(DateTime utc, out List<HistoricalFriendAvailability> friends);
    }

    // Optional reflection-only bridge. Deep Sims never references Party Tools at compile time and
    // fails closed when v2 is absent, late-loading, disabled, or malformed.
    internal sealed class PartyToolsHistoricalAvailabilityBridge : IHistoricalFriendAvailability
    {
        private MethodInfo _snapshot;

        internal void Reset() { _snapshot = null; }

        public bool TrySnapshot(DateTime utc, out List<HistoricalFriendAvailability> friends)
        {
            friends = new List<HistoricalFriendAvailability>();
            try
            {
                if (_snapshot == null && !TryBind()) return false;
                object raw = _snapshot.Invoke(null, new object[] { utc.ToUniversalTime().Ticks });
                IEnumerable rows = raw as IEnumerable;
                if (rows == null) return false;
                foreach (object row in rows)
                {
                    IDictionary values = row as IDictionary;
                    if (values == null) continue;
                    string name = Read(values, "name");
                    string id = Read(values, "stableId");
                    string state = Read(values, "availability");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id) ||
                        (!string.Equals(state, "Online", StringComparison.Ordinal) &&
                         !string.Equals(state, "Offline", StringComparison.Ordinal))) continue;
                    friends.Add(new HistoricalFriendAvailability { Name = name.Trim(), StableId = id.Trim(), Online = state == "Online" });
                }
                return true;
            }
            catch { _snapshot = null; friends.Clear(); return false; }
        }

        private bool TryBind()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType("ErenshorPartyTools.PartyToolsFriendAvailabilityApi", false);
                if (type == null) continue;
                FieldInfo version = type.GetField("ContractVersion", BindingFlags.Public | BindingFlags.Static);
                if (version == null || Convert.ToInt32(version.GetValue(null), CultureInfo.InvariantCulture) < 2) return false;
                _snapshot = type.GetMethod("GetBaseAvailabilitySnapshotAtUtc", BindingFlags.Public | BindingFlags.Static,
                    null, new Type[] { typeof(long) }, null);
                return _snapshot != null;
            }
            return false;
        }

        private static string Read(IDictionary values, string key)
        {
            object value = values[key];
            return value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    internal sealed class RecentLifeSimulationResult
    {
        internal readonly List<RecentLifeOwnedRecord> Records = new List<RecentLifeOwnedRecord>();
        internal int WindowsInspected;
        internal int FriendsInspected;
        internal int SharedEpisodes;
        internal double RuntimeMilliseconds;
    }

    internal sealed class RecentLifeOwnedRecord
    {
        internal SimSnapshot Owner;
        internal StructuredMemoryRecord Record;
    }

    internal static class RecentLifePolicy
    {
        internal const int EpochHours = 4;
        internal const int MaxLookbackDays = 7;
        internal const int MaxRecordsTotal = 24;
        internal const int MaxRecordsPerSim = 3;
        internal const string Source = "simulated_recent_life";
        internal const string SourceSystem = "DeepSimsRecentLife";
        private static readonly string[] Activities = { "exploring", "gathering", "crafting", "socializing", "solo play" };

        internal static RecentLifeSimulationResult Simulate(string characterScope, DateTime startUtc, DateTime endUtc,
            IHistoricalFriendAvailability availability, Func<string, string, string> identityInfluence = null)
        {
            Stopwatch watch = Stopwatch.StartNew();
            RecentLifeSimulationResult result = new RecentLifeSimulationResult();
            if (availability == null) return Finish(result, watch);
            DateTime end = endUtc.ToUniversalTime();
            DateTime start = startUtc.ToUniversalTime();
            if (end <= start) return Finish(result, watch);
            if ((end - start).TotalDays > MaxLookbackDays) start = end.AddDays(-MaxLookbackDays);

            Dictionary<string, int> perSim = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            List<AvailabilityTracker> availabilityStats = new List<AvailabilityTracker>();
            long firstEpoch = start.Ticks / (TimeSpan.TicksPerHour * EpochHours);
            long lastEpoch = (end.Ticks - 1) / (TimeSpan.TicksPerHour * EpochHours);
            for (long epoch = firstEpoch; epoch <= lastEpoch && result.Records.Count < MaxRecordsTotal; epoch++)
            {
                result.WindowsInspected++;
                DateTime midpoint = new DateTime(epoch * TimeSpan.TicksPerHour * EpochHours, DateTimeKind.Utc).AddHours(2);
                List<HistoricalFriendAvailability> friends;
                if (!availability.TrySnapshot(midpoint, out friends)) { result.Records.Clear(); return Finish(result, watch); }
                result.FriendsInspected += friends.Count;
                List<HistoricalFriendAvailability> active = new List<HistoricalFriendAvailability>();
                for (int i = 0; i < friends.Count && result.Records.Count < MaxRecordsTotal; i++)
                {
                    HistoricalFriendAvailability friend = friends[i];
                    AvailabilityTracker tracker = FindTracker(availabilityStats, friend);
                    if (friend.Online) tracker.Online++; else tracker.Offline++;
                    int count; perSim.TryGetValue(friend.StableId, out count);
                    if (!friend.Online || count >= MaxRecordsPerSim) continue;
                    if ((Hash(characterScope + "|session|" + friend.StableId + "|" + epoch) % 100u) >= 34u) continue;
                    active.Add(friend);
                    string influence = identityInfluence == null ? string.Empty : (identityInfluence(friend.Name, SafeKey(friend.Name)) ?? string.Empty);
                    string activity = PickActivity(characterScope, friend, epoch, influence);
                    int minutes = 25 + (int)(Hash(characterScope + "|duration|" + friend.StableId + "|" + epoch) % 111u);
                    Add(result, perSim, characterScope, friend, epoch, activity, minutes, null);
                }
                if (active.Count >= 2 && result.SharedEpisodes < 3 && result.Records.Count + 2 <= MaxRecordsTotal &&
                    (Hash(characterScope + "|shared|" + epoch) % 100u) < 28u)
                {
                    HistoricalFriendAvailability first = active[0], second = active[1];
                    Add(result, perSim, characterScope, first, epoch, "grouped play", 45, second);
                    Add(result, perSim, characterScope, second, epoch, "grouped play", 45, first);
                    result.SharedEpisodes++;
                }
            }
            for (int i = 0; i < availabilityStats.Count && result.Records.Count < MaxRecordsTotal; i++)
            {
                AvailabilityTracker tracker = availabilityStats[i];
                int count; perSim.TryGetValue(tracker.Friend.StableId, out count);
                if (count == 0 && tracker.Offline > tracker.Online)
                    Add(result, perSim, characterScope, tracker.Friend, lastEpoch, "unavailable", 0, null);
            }
            return Finish(result, watch);
        }

        private sealed class AvailabilityTracker { internal HistoricalFriendAvailability Friend; internal int Online; internal int Offline; }
        private static string PickActivity(string scope, HistoricalFriendAvailability friend, long epoch, string influence)
        {
            string lower = (influence ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("craft") && Hash(scope + friend.StableId + epoch + "craft") % 3u == 0u) return "crafting";
            if ((lower.Contains("explor") || lower.Contains("adventure")) && Hash(scope + friend.StableId + epoch + "explore") % 3u == 0u) return "exploring";
            if ((lower.Contains("gather") || lower.Contains("resource")) && Hash(scope + friend.StableId + epoch + "gather") % 3u == 0u) return "gathering";
            return Activities[Hash(scope + "|activity|" + friend.StableId + "|" + epoch + "|" + lower) % (uint)Activities.Length];
        }
        private static AvailabilityTracker FindTracker(List<AvailabilityTracker> values, HistoricalFriendAvailability friend)
        {
            for (int i = 0; i < values.Count; i++) if (values[i].Friend.StableId == friend.StableId) return values[i];
            AvailabilityTracker value = new AvailabilityTracker { Friend = friend }; values.Add(value); return value;
        }

        private static void Add(RecentLifeSimulationResult result, Dictionary<string, int> counts, string characterScope,
            HistoricalFriendAvailability owner, long epoch, string activity, int minutes, HistoricalFriendAvailability other)
        {
            if (result.Records.Count >= MaxRecordsTotal) return;
            int count; counts.TryGetValue(owner.StableId, out count);
            if (count >= MaxRecordsPerSim) return;
            string ownerKey = SafeKey(owner.Name);
            string correlation = "recent-life-" + Hash(owner.StableId + "|" + epoch + "|" + activity + "|" + (other == null ? "" : other.StableId)).ToString("x8");
            string duration = minutes < 45 ? "briefly" : minutes < 90 ? "for about an hour" : "for a couple of hours";
            string text = activity == "unavailable"
                ? owner.Name + " was mostly unavailable during the recent offline interval."
                : owner.Name + " was online " + duration + " and spent the time " + activity + ".";
            List<string> participants = new List<string> { ownerKey };
            List<string> knownBy = new List<string> { ownerKey };
            if (other != null) { participants.Add(SafeKey(other.Name)); knownBy.Add(SafeKey(other.Name)); }
            StructuredMemoryRecord record = new StructuredMemoryRecord
            {
                Id = correlation + "-" + ownerKey,
                EpisodeId = correlation,
                SourceCorrelationId = correlation,
                MemoryType = activity == "unavailable" ? "recent_life_unavailable" : "recent_life_brief_online_session",
                Text = text,
                FactSummary = text,
                Subject = activity,
                Importance = 22,
                Participants = participants,
                KnownBy = knownBy,
                Topics = new List<string> { "recent life", "what have you been up to", activity },
                EmotionalTags = new List<string>(),
                Utc = new DateTime(epoch * TimeSpan.TicksPerHour * EpochHours, DateTimeKind.Utc).AddHours(2).ToString("o"),
                Source = Source,
                SourceSystem = SourceSystem,
                CharacterScope = characterScope ?? string.Empty,
                MemoryTier = SocialMemoryPolicy.TierRecent,
                OwnerSimKey = ownerKey,
                OwnerSimName = owner.Name,
                EvidenceIds = new List<string>()
            };
            result.Records.Add(new RecentLifeOwnedRecord
            {
                Owner = new SimSnapshot { Key = ownerKey, Name = owner.Name }, Record = record
            });
            counts[owner.StableId] = count + 1;
        }

        internal static bool IsRecentLifeQuestion(string text)
        {
            string value = (text ?? string.Empty).ToLowerInvariant();
            return value.Contains("what have you been up to") || value.Contains("been doing") ||
                value.Contains("were you on") || value.Contains("yesterday") || value.Contains("earlier") ||
                value.Contains("play without me");
        }

        internal static string SafeKey(string name)
        {
            StringBuilder value = new StringBuilder();
            string source = string.IsNullOrWhiteSpace(name) ? "sim" : name.Trim();
            for (int i = 0; i < source.Length; i++) if (char.IsLetterOrDigit(source[i]) || source[i] == '-' || source[i] == '_') value.Append(source[i]);
            return value.Length == 0 ? "sim" : value.ToString();
        }

        private static RecentLifeSimulationResult Finish(RecentLifeSimulationResult result, Stopwatch watch)
        { watch.Stop(); result.RuntimeMilliseconds = watch.Elapsed.TotalMilliseconds; return result; }

        private static uint Hash(string value)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < (value ?? string.Empty).Length; i++) { hash ^= value[i]; hash *= 16777619u; }
            return hash;
        }
    }

    [Serializable]
    internal sealed class RecentLifeState
    {
        public int Version = 1;
        public string CharacterScope = string.Empty;
        public string LastSessionStartedUtc = string.Empty;
        public string LastSessionEndedUtc = string.Empty;
        public string LastHeartbeatUtc = string.Empty;
        public string SimulationCursorUtc = string.Empty;
    }

    internal sealed class RecentLifeCoordinator
    {
        private readonly string _path;
        private readonly string _scope;
        private readonly MemoryStore _memory;
        private readonly IHistoricalFriendAvailability _availability;
        private RecentLifeState _state;
        private DateTime _nextHeartbeatUtc;
        private bool _catchupResolved;

        internal RecentLifeCoordinator(string memoryDirectory, string scope, MemoryStore memory, IHistoricalFriendAvailability availability)
        { _path = Path.Combine(memoryDirectory, "RecentLife", "state.json"); _scope = scope; _memory = memory; _availability = availability; }

        internal RecentLifeSimulationResult BeginSession(DateTime nowUtc)
        {
            _state = Load();
            DateTime cursor;
            bool hasCursor = DateTime.TryParse(_state.SimulationCursorUtc, out cursor);
            if (!hasCursor)
            {
                _catchupResolved = true;
                _state.SimulationCursorUtc = nowUtc.ToUniversalTime().ToString("o");
                _state.LastSessionStartedUtc = nowUtc.ToUniversalTime().ToString("o");
                _state.LastHeartbeatUtc = _state.LastSessionStartedUtc;
                Save(); _nextHeartbeatUtc = nowUtc.AddMinutes(1);
                return new RecentLifeSimulationResult();
            }
            RecentLifeSimulationResult result = RecentLifePolicy.Simulate(_scope, cursor.ToUniversalTime(), nowUtc.ToUniversalTime(),
                _availability, _memory.GetRecentLifeIdentityInfluence);
            RecentLifeDiagnosticCounters.Generated(result.Records.Count);
            if (nowUtc.ToUniversalTime() < cursor.ToUniversalTime())
            {
                _catchupResolved = true;
                _state.SimulationCursorUtc = nowUtc.ToUniversalTime().ToString("o");
                result = new RecentLifeSimulationResult();
            }
            _catchupResolved = result.WindowsInspected == 0 || result.Records.Count > 0 || AvailabilityResolved(cursor, nowUtc);
            if (result.WindowsInspected > 0 && _catchupResolved)
            {
                for (int i = 0; i < result.Records.Count; i++)
                {
                    string decision;
                    _memory.StoreSimulatedRecentLifeMemory(result.Records[i].Owner, result.Records[i].Record, out decision);
                    RecentLifeDiagnosticCounters.StoreDecision(decision);
                }
                _memory.FlushPending(true);
                _state.SimulationCursorUtc = nowUtc.ToUniversalTime().ToString("o");
            }
            _state.LastSessionStartedUtc = nowUtc.ToUniversalTime().ToString("o");
            _state.LastHeartbeatUtc = _state.LastSessionStartedUtc;
            Save(); _nextHeartbeatUtc = nowUtc.AddMinutes(1);
            return result;
        }

        private bool AvailabilityResolved(DateTime start, DateTime end)
        {
            List<HistoricalFriendAvailability> ignored;
            DateTime probe = start + TimeSpan.FromTicks(Math.Max(0L, (end - start).Ticks / 2));
            return _availability != null && _availability.TrySnapshot(probe, out ignored);
        }

        internal void Tick(DateTime nowUtc)
        {
            if (_state == null || nowUtc < _nextHeartbeatUtc) return;
            _state.LastHeartbeatUtc = nowUtc.ToUniversalTime().ToString("o");
            // Once catch-up authority was resolved, each heartbeat becomes the crash fallback
            // boundary. If authority was unavailable, keep the old cursor so the interval is not lost.
            if (_catchupResolved) _state.SimulationCursorUtc = _state.LastHeartbeatUtc;
            Save(); _nextHeartbeatUtc = nowUtc.AddMinutes(1);
        }

        internal void EndSession(DateTime nowUtc)
        {
            if (_state == null) return;
            _state.LastSessionEndedUtc = nowUtc.ToUniversalTime().ToString("o");
            _state.LastHeartbeatUtc = _state.LastSessionEndedUtc;
            if (_catchupResolved) _state.SimulationCursorUtc = _state.LastSessionEndedUtc;
            Save();
        }

        private RecentLifeState Load()
        {
            try { if (File.Exists(_path)) { RecentLifeState value = JsonUtil.ReadFile<RecentLifeState>(_path); if (value != null && value.CharacterScope == _scope) return value; } }
            catch { }
            return new RecentLifeState { CharacterScope = _scope };
        }

        private void Save()
        {
            string temp = _path + ".tmp";
            JsonUtil.WriteFile(temp, _state);
            if (File.Exists(_path)) File.Replace(temp, _path, null); else File.Move(temp, _path);
        }
    }
}
