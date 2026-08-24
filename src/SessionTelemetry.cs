using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace ErenshorDeepSims
{
    // Compact verified session telemetry. Erenshor remains authoritative; Deep Sims observes
    // visible/logged facts and turns them into short prompt facts and outing memories.
    internal sealed class SessionTelemetry
    {
        private readonly DeepSimsPlugin _plugin;
        private readonly MemoryStore _memory;
        private readonly Func<DateTime> _utcNow;
        // RecordKill/RecordLoot/Observe/etc. run on the Unity main thread while
        // TryResolveExperiencedKnowledge runs on the background request-pump thread (see
        // DeepSimsPlugin.ResolveKnowledgeAsync). Both sides read/write the same dictionaries, so every
        // public entry point below serializes through this lock, consistent with MemoryStore's _ioLock.
        private readonly object _lock = new object();
        private readonly Dictionary<string, int> _kills = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _killsByKiller = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _loot = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _gearLikeLoot = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SimSnapshot> _participants = new Dictionary<string, SimSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _simCloseCalls = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _simDeaths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _lowHealthCooldown = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _zoneSeconds = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _zones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _recentVerified = new List<string>();
        private readonly HashSet<string> _lastActiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _keysWithOutingGap = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _participantSeconds = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _pairSeconds = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _observedLootSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _observedLootSourceUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private DateTime _startedUtc;
        private DateTime _lastObserveUtc = DateTime.MinValue;
        private DateTime _lastCombatUtc = DateTime.MinValue;
        private DateTime _lastProgressUtc = DateTime.MinValue;
        private DateTime _lastRestUtc = DateTime.MinValue;
        private DateTime _lastZoneChangeUtc = DateTime.MinValue;
        private DateTime _lastLogUtc = DateTime.MinValue;
        private DateTime _competitiveCombatSuppressedUntilUtc = DateTime.MinValue;
        private string _lastLogText = string.Empty;
        private string _currentZone = string.Empty;
        private string _playerName = string.Empty;
        private int _playerDeaths;
        private int _playerCloseCalls;
        private DateTime _playerLowHealthCooldown = DateTime.MinValue;
        private int _dangerEvents;
        private int _quests;
        private int _levels;
        private int _gold;
        private int _experience;
        private bool _active;
        private string _currentCombatTarget = string.Empty;
        private DateTime _currentCombatTargetUtc = DateTime.MinValue;
        private string _lastKilledEnemy = string.Empty;
        private DateTime _lastKillUtc = DateTime.MinValue;
        private EncounterState _currentEncounter;
        private readonly List<string> _recentEncounters = new List<string>();
        private readonly List<EncounterSnapshot> _recentCompletedEncounters = new List<EncounterSnapshot>();
        private EncounterSnapshot _lastCompletedEncounter;
        private int _nextEncounterId;
        private const double EncounterQuietSeconds = 15.0;

        private sealed class EncounterState
        {
            public DateTime StartedUtc;
            public DateTime LastActivityUtc;
            public string Zone;
            public readonly Dictionary<string, int> Kills = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> Enemies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> NotableActions = new List<string>();
            public readonly HashSet<string> Participants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public int CloseCalls;
            public int Deaths;
        }

        internal SessionTelemetry(DeepSimsPlugin plugin, MemoryStore memory, Func<DateTime> utcNow = null)
        {
            _plugin = plugin;
            _memory = memory;
            _utcNow = utcNow ?? delegate { return DateTime.UtcNow; };
        }

        internal void Observe(WorldSnapshot world, IList<SimSnapshot> active)
        {
            Observe(world, active, true);
        }

        internal void Observe(WorldSnapshot world, IList<SimSnapshot> active, bool gameplayReady)
        {
            lock (_lock) { ObserveLocked(world, active, gameplayReady); }
        }

        private void ObserveLocked(WorldSnapshot world, IList<SimSnapshot> active, bool gameplayReady)
        {
            if (world == null || active == null) return;
            DateTime now = UtcNow();
            _playerName = world.Player == null ? string.Empty : (world.Player.Name ?? string.Empty);

            // Character readiness is the existing native gameplay boundary. Menu/loading scenes
            // and unknown scene names can pause an outing, but can never start, relabel, or finish
            // one. No replacement location is guessed during that gap.
            if (!gameplayReady || !VerifiedOutingHistoryPolicy.IsDurableGameplayLocation(world.Scene))
            {
                if (_active) _lastObserveUtc = now;
                return;
            }

            if (active.Count == 0)
            {
                if (_active)
                {
                    if ((now - _startedUtc).TotalMinutes >= 2.0) FinishOuting(now);
                    else ResetWithoutSaving();
                }
                return;
            }

            if (!_active) StartOuting(world, active, now);
            else
            {
                AccumulateRelationshipTime(now);
                UpdateActiveParty(active);
            }

            AccumulateZoneTime(now);
            string newZone = world.Scene ?? _currentZone;
            if (!string.Equals(newZone, _currentZone, StringComparison.OrdinalIgnoreCase))
            {
                _currentZone = newZone;
                _lastZoneChangeUtc = now;
                if (!string.IsNullOrWhiteSpace(_currentZone))
                {
                    _zones.Add(_currentZone);
                    AddRecentVerified("Entered " + _currentZone + ".");
                }
            }
            else _currentZone = newZone;
            if (!string.IsNullOrWhiteSpace(_currentZone)) _zones.Add(_currentZone);

            ObservePlayerLowHealth(world.Player, now);

            for (int i = 0; i < active.Count; i++)
            {
                SimSnapshot sim = active[i];
                if (sim == null || string.IsNullOrWhiteSpace(sim.Key)) continue;
                _participants[sim.Key] = sim;
                if (_currentEncounter != null && !string.IsNullOrWhiteSpace(sim.Name)) _currentEncounter.Participants.Add(sim.Name);
                ObserveLowHealth(sim, now);
            }
            _lastObserveUtc = now;
        }

        // Semantic PvP/Practice Duel integrations own those combat results. While one is active,
        // generic HP, death, and kill proxies must not create a second PvE/close-call narrative.
        internal void ObserveCompetitiveCombatEvent(string sourceSystem, string eventType, string reasonToken)
        {
            lock (_lock)
            {
                DateTime now = UtcNow();
                string source = (sourceSystem ?? string.Empty).Trim().ToLowerInvariant();
                string type = (eventType ?? string.Empty).Trim().ToLowerInvariant();
                string reason = (reasonToken ?? string.Empty).Trim().ToLowerInvariant();
                bool starts = (source == "pvp" && (type == "pvp_accepted" || type == "pvp_ambush")) ||
                    (source == "duel" && (type == "duel_accepted" || type == "duel_started"));
                bool terminal = (source == "pvp" && (type == "pvp_match_completed" || type == "pvp_cancelled" || type == "pvp_refused")) ||
                    (source == "duel" && (type == "duel_completed" || type == "duel_cancelled" || type == "duel_declined" || type == "duel_request_rejected"));
                if (starts) _competitiveCombatSuppressedUntilUtc = now.AddMinutes(60.0);
                else if (terminal)
                {
                    // A hostile interruption is actual ordinary combat taking ownership back now.
                    _competitiveCombatSuppressedUntilUtc = source == "duel" && reason == "hostile_interruption"
                        ? now : now.AddSeconds(12.0);
                }
            }
        }

        private bool CompetitiveCombatOwnsGenericProxy(DateTime now)
        {
            return _competitiveCombatSuppressedUntilUtc != DateTime.MinValue && now < _competitiveCombatSuppressedUntilUtc;
        }

        internal bool ShouldSuppressGenericCombatEvent(string eventType)
        {
            lock (_lock)
            {
                string t = (eventType ?? string.Empty).Trim().ToLowerInvariant();
                if (t != "player_death" && t != "sim_death" && t != "sim_low_health" &&
                    t != "player_low_health" && t != "kill") return false;
                return CompetitiveCombatOwnsGenericProxy(UtcNow());
            }
        }

        internal void MarkCombatActivity()
        {
            MarkCombatActivity(null, false);
        }

        internal void MarkCombatActivity(string targetName)
        {
            MarkCombatActivity(targetName, false);
        }

        internal void MarkCombatActivity(string targetName, bool trustedNearbyTarget)
        {
            lock (_lock)
            {
            if (!_active) return;
            DateTime now = UtcNow();
            if (CompetitiveCombatOwnsGenericProxy(now)) return;
            string cleanTarget = string.IsNullOrWhiteSpace(targetName) ? string.Empty : CleanName(targetName, string.Empty);
            // ReduceHP does not expose its attacker. Neither proximity nor a chat line proves that
            // the party damaged this NPC, so named enemy damage fails closed. A future compatibility
            // hook may pass an attacker identity only after that API is established from game code.
            if (!string.IsNullOrWhiteSpace(cleanTarget)) return;
            TouchEncounter(cleanTarget, now);
            _lastCombatUtc = now;
            }
        }

        internal void RecordDirectKill(string enemyName)
        {
            // Stats.ReduceHP identifies only the dead NPC, not its killer. Recording this as a party
            // kill would let nearby unrelated combat enter encounter totals and persistent memory.
            // TODO: restore a direct fallback only if a stable attacker/owner signal is verified.
        }

        internal void ApplyLiveContext(IList<SimSnapshot> active)
        {
            if (active == null) return;
            lock (_lock)
            {
                DateTime now = UtcNow();
                bool combat = _active && (now - _lastCombatUtc).TotalSeconds < 12.0;
                for (int i = 0; i < active.Count; i++)
                {
                    SimSnapshot sim = active[i];
                    if (sim == null || string.IsNullOrWhiteSpace(sim.Name)) continue;
                    if (string.IsNullOrWhiteSpace(sim.CombatRole)) sim.CombatRole = SimContextReader.DescribeClassRole(sim.ClassName);
                    sim.CurrentAction = combat ? "in combat with the party (specific action not observed)" : string.Empty;
                    sim.CurrentTarget = string.Empty;
                }
            }
        }

        internal void RecordKill(string enemyName)
        {
            // A victim name without an attributed killer is insufficient evidence. Keep this
            // compatibility entry point fail-closed; the parsed authoritative kill log calls the
            // attributed overload below.
        }

        internal void RecordKill(string enemyName, string killerName)
        {
            lock (_lock)
            {
            if (!_active) return;
            if (CompetitiveCombatOwnsGenericProxy(UtcNow())) return;
            string name = CleanName(enemyName, "enemy");
            Increment(_kills, name, 1);
            if (!string.IsNullOrWhiteSpace(killerName)) Increment(_killsByKiller, CleanName(killerName, "party member"), 1);
            DateTime now = UtcNow();
            TouchEncounter(name, now);
            if (_currentEncounter != null) Increment(_currentEncounter.Kills, name, 1);
            _lastCombatUtc = now;
            _currentCombatTarget = name;
            _currentCombatTargetUtc = now;
            _lastKilledEnemy = name;
            _lastKillUtc = now;
            }
        }

        internal void RecordLoot(string itemName, int amount)
        {
            lock (_lock)
            {
            if (!_active) return;
            string name = CleanName(itemName, "item");
            int qty = Math.Max(1, amount);
            Increment(_loot, name, qty);
            if (LooksGearLike(name)) Increment(_gearLikeLoot, name, qty);
            DateTime now = UtcNow();
            _lastProgressUtc = now;
            // Erenshor prints loot immediately after the defeated enemy in normal play. Preserve that
            // observed sequence as experience without claiming it is the item's only possible source.
            if (!string.IsNullOrWhiteSpace(_lastKilledEnemy) && _lastKillUtc != DateTime.MinValue && (now - _lastKillUtc).TotalSeconds <= 12.0)
            {
                _observedLootSources[name] = _lastKilledEnemy;
                _observedLootSourceUtc[name] = now;
                AddRecentVerified("Looted " + name + " shortly after killing " + _lastKilledEnemy + ".");
            }
            }
        }

        internal WikiResult TryResolveExperiencedKnowledge(string message)
        {
            lock (_lock) { return TryResolveExperiencedKnowledgeLocked(message); }
        }

        private WikiResult TryResolveExperiencedKnowledgeLocked(string message)
        {
            if (!_active || string.IsNullOrWhiteSpace(message)) return null;
            string lower = message.ToLowerInvariant();
            bool sourceQuestion = lower.Contains("where") || lower.Contains("drop") || lower.Contains("get ") || lower.Contains("find ") ||
                                  lower.Contains("come from") || lower.Contains("comes from") || lower.Contains("source");
            if (!sourceQuestion) return null;

            string bestItem = string.Empty;
            foreach (KeyValuePair<string, int> pair in _loot)
            {
                if (string.IsNullOrWhiteSpace(pair.Key)) continue;
                if (lower.IndexOf(pair.Key.ToLowerInvariant(), StringComparison.Ordinal) >= 0 && pair.Key.Length > bestItem.Length) bestItem = pair.Key;
            }
            if (string.IsNullOrWhiteSpace(bestItem)) return null;

            string enemy;
            DateTime when;
            if (!_observedLootSources.TryGetValue(bestItem, out enemy) || string.IsNullOrWhiteSpace(enemy)) return null;
            if (!_observedLootSourceUtc.TryGetValue(bestItem, out when)) when = DateTime.MinValue;

            WikiResult result = new WikiResult();
            result.Query = message.Trim();
            result.Title = "Current outing observation: " + bestItem;
            result.SourceLabel = "verified current outing experience";
            result.Found = true;
            result.Extract = "During this current outing, the party looted " + bestItem + " shortly after killing " + enemy +
                ". This is direct observed session experience. It is safe to say 'we just got one after killing " + enemy +
                "', but this observation alone does not prove " + enemy + " is the only possible source.";
            return result;
        }

        internal void ObserveLogLine(string raw)
        {
            lock (_lock) { ObserveLogLineLocked(raw); }
        }

        private void ObserveLogLineLocked(string raw)
        {
            if (!_active || string.IsNullOrWhiteSpace(raw)) return;
            string text = Regex.Replace(raw, @"<[^>]+>", string.Empty).Trim();
            if (text.Length == 0) return;

            DateTime now = UtcNow();
            // Some versions/log overloads can surface the same line twice. Keep telemetry idempotent.
            if (string.Equals(text, _lastLogText, StringComparison.Ordinal) && (now - _lastLogUtc).TotalMilliseconds < 350) return;
            _lastLogText = text;
            _lastLogUtc = now;

            // Group chat (player, vanilla Sim, or generated) is HEARD context, never combat evidence.

            // Current Erenshor's local-player kill message uses this first-person form rather than
            // "X has been slain by Y". The UI itself is authoritative here: "You" is necessarily
            // the local player already captured in _playerName, so this remains attributed evidence.
            Match localKill = Regex.Match(text, @"^You\s+(?:have\s+)?slain\s+(?:(?:A|An|The)\s+)?(.+?)[.!]?$", RegexOptions.IgnoreCase);
            if (localKill.Success && !string.IsNullOrWhiteSpace(_playerName))
            {
                RecordKill(CleanName(localKill.Groups[1].Value, "enemy"), _playerName);
                return;
            }

            Match kill = Regex.Match(text, @"^(?:(?:A|An)\s+)?(.+?)\s+has been slain by\s+(.+?)[.!]?$", RegexOptions.IgnoreCase);
            if (kill.Success)
            {
                string enemy = CleanName(kill.Groups[1].Value, "enemy");
                string killer = CleanName(kill.Groups[2].Value, "party member");
                if (!IsVerifiedPartyKiller(killer)) return;
                RecordKill(enemy, killer);
                return;
            }
            Match loot = Regex.Match(text, @"^Looted Item:\s*(.+)$", RegexOptions.IgnoreCase);
            if (loot.Success)
            {
                RecordLoot(loot.Groups[1].Value, 1);
                return;
            }
            Match gold = Regex.Match(text, @"^Found\s+(\d+)\s+Gold!?$", RegexOptions.IgnoreCase);
            if (gold.Success)
            {
                int value;
                if (int.TryParse(gold.Groups[1].Value, out value)) _gold += Math.Max(0, value);
                _lastProgressUtc = now;
                return;
            }
            Match xp = Regex.Match(text, @"^You(?:'ve| have) gained\s+(\d+)\s+experience!?$", RegexOptions.IgnoreCase);
            if (xp.Success)
            {
                int value;
                if (int.TryParse(xp.Groups[1].Value, out value)) _experience += Math.Max(0, value);
                _lastProgressUtc = now;
                return;
            }
            if (text.IndexOf("settle into a meditative state", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("meditative state", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _lastRestUtc = now;
                AddRecentVerified("The player stopped to meditate/rest.");
            }
        }

        internal void RecordObservedEvent(string type, string description)
        {
            lock (_lock)
            {
            if (!_active) return;
            string t = type == null ? string.Empty : type.ToLowerInvariant();
            if (CompetitiveCombatOwnsGenericProxy(UtcNow()) &&
                (t == "player_death" || t == "sim_death" || t == "sim_low_health" || t == "player_low_health" || t == "kill")) return;
            if (t == "player_death")
            {
                _playerDeaths++;
                if (_currentEncounter != null) _currentEncounter.Deaths++;
            }
            if (t == "sim_death")
            {
                string simName = ExtractBefore(description, " was defeated");
                if (!IsActivePartySimName(simName)) return;
                _dangerEvents++;
                if (_currentEncounter != null) _currentEncounter.Deaths++;
                if (!string.IsNullOrWhiteSpace(simName)) Increment(_simDeaths, simName, 1);
            }
            if (t == "sim_low_health")
            {
                _dangerEvents++;
                string simName = ExtractBefore(description, " dropped below");
                if (!string.IsNullOrWhiteSpace(simName)) Increment(_simCloseCalls, simName, 1);
            }
            if (t.Contains("quest")) _quests++;
            if (t.Contains("level")) _levels++;
            if (t.Contains("death") || t.Contains("danger") || t.Contains("kill") || t.Contains("low_health")) _lastCombatUtc = UtcNow();
            if (t.Contains("quest") || t.Contains("level") || t.Contains("loot")) _lastProgressUtc = UtcNow();
            if (!string.IsNullOrWhiteSpace(description)) AddRecentVerified(description.Trim());
            }
        }

        internal OutingSnapshot Snapshot()
        {
            lock (_lock) { return SnapshotLocked(); }
        }

        private OutingSnapshot SnapshotLocked()
        {
            OutingSnapshot s = new OutingSnapshot();
            s.Active = _active;
            if (!_active)
            {
                s.Activity = "no active outing";
                s.Mood = "normal";
                s.Facts = new List<string>();
                return s;
            }

            DateTime now = UtcNow();
            FinalizeEncounterIfQuiet(now);
            AccumulateRelationshipTime(now);
            AccumulateZoneTime(now);
            s.Minutes = Math.Max(1, (int)Math.Round((now - _startedUtc).TotalMinutes));
            s.CurrentZone = _currentZone;
            s.Activity = DetermineActivity(now);
            int totalDeaths = _playerDeaths + Sum(_simDeaths);
            int totalCloseCalls = _playerCloseCalls + Sum(_simCloseCalls);
            s.Mood = totalDeaths > 0 || totalCloseCalls >= 3 ? "rough" : (_kills.Count > 0 && totalCloseCalls == 0 ? "smooth" : "normal");
            s.TotalKills = Sum(_kills);
            s.TotalLootItems = Sum(_loot);
            s.UniqueEnemies = _kills.Count;
            s.UniqueLoot = _loot.Count;
            s.Gold = _gold;
            s.Experience = _experience;
            s.ZoneHistory = BuildZoneHistory(3);
            if (!string.IsNullOrWhiteSpace(_currentCombatTarget) && (now - _currentCombatTargetUtc).TotalSeconds <= 15.0)
                s.CurrentCombatTarget = _currentCombatTarget;
            else s.CurrentCombatTarget = string.Empty;
            s.CurrentEncounter = DescribeCurrentEncounter(now);
            s.LastEncounter = _recentEncounters.Count == 0 ? string.Empty : _recentEncounters[_recentEncounters.Count - 1];
            s.RecentEncounters = new List<string>(_recentEncounters);
            s.LastCompletedEncounter = _lastCompletedEncounter;
            s.RecentCompletedEncounters = CloneEncounters(_recentCompletedEncounters);
            s.Facts = BuildFacts(10);
            return s;
        }

        internal string Describe()
        {
            OutingSnapshot s = Snapshot();
            if (!s.Active) return "no active outing";
            string result = s.Minutes + "m, " + s.Activity + ", mood=" + s.Mood +
                ", kills=" + s.TotalKills + ", loot=" + s.TotalLootItems;
            if (s.Facts != null && s.Facts.Count > 0)
            {
                int take = Math.Min(6, s.Facts.Count);
                List<string> shortFacts = new List<string>();
                for (int i = 0; i < take; i++) shortFacts.Add(s.Facts[i]);
                result += " | " + string.Join(" ", shortFacts.ToArray());
            }
            return result;
        }

        internal string DescribeDetailed()
        {
            OutingSnapshot s = Snapshot();
            if (!s.Active) return "No active outing.";
            StringBuilder sb = new StringBuilder();
            sb.Append("[DeepSims] Session: ").Append(s.Minutes).Append("m | ").Append(s.Activity).Append(" | mood=").Append(s.Mood);
            sb.Append(" | kills=").Append(s.TotalKills).Append(" (").Append(s.UniqueEnemies).Append(" types)");
            sb.Append(" | loot=").Append(s.TotalLootItems).Append(" (").Append(s.UniqueLoot).Append(" types)");
            if (s.Gold > 0) sb.Append(" | gold=").Append(s.Gold);
            if (s.Experience > 0) sb.Append(" | xp=").Append(s.Experience);
            if (!string.IsNullOrWhiteSpace(s.CurrentCombatTarget)) sb.Append(" | target=").Append(s.CurrentCombatTarget);
            sb.Append(" | right-now=");
            sb.Append(string.IsNullOrWhiteSpace(s.CurrentEncounter) ? "none" : s.CurrentEncounter);
            sb.Append(" | last-completed=");
            sb.Append(string.IsNullOrWhiteSpace(s.LastEncounter) ? "none" : s.LastEncounter);
            sb.Append(" | encounter-history=").Append(s.RecentEncounters == null ? 0 : s.RecentEncounters.Count).Append("/3");
            if (!string.IsNullOrWhiteSpace(s.ZoneHistory)) sb.Append(" | zones: ").Append(s.ZoneHistory);
            return sb.ToString();
        }

        internal string ExportReport()
        {
            OutingSnapshot s = Snapshot();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("DEEP SIMS SESSION NOTES");
            sb.AppendLine("Exported: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            if (!s.Active)
            {
                sb.AppendLine("No active outing.");
                return sb.ToString();
            }
            sb.AppendLine("OUTING");
            sb.AppendLine("Zone: " + Safe(s.CurrentZone, "unknown"));
            sb.AppendLine("Time grouped: about " + s.Minutes + " minutes");
            sb.AppendLine("Activity: " + Safe(s.Activity, "unknown"));
            sb.AppendLine("Mood: " + Safe(s.Mood, "normal"));
            sb.AppendLine("Kills: " + s.TotalKills + " across " + s.UniqueEnemies + " enemy types");
            sb.AppendLine("Loot: " + s.TotalLootItems + " across " + s.UniqueLoot + " item types");
            if (s.Gold > 0) sb.AppendLine("Gold found: " + s.Gold);
            if (s.Experience > 0) sb.AppendLine("Player XP observed: " + s.Experience);
            if (!string.IsNullOrWhiteSpace(s.ZoneHistory)) sb.AppendLine("Zones: " + s.ZoneHistory);
            sb.AppendLine();
            sb.AppendLine("RIGHT NOW");
            sb.AppendLine(string.IsNullOrWhiteSpace(s.CurrentEncounter) ? "No active encounter." : s.CurrentEncounter);
            sb.AppendLine();
            sb.AppendLine("LAST COMPLETED FIGHT");
            sb.AppendLine(string.IsNullOrWhiteSpace(s.LastEncounter) ? "No completed fight recorded yet." : s.LastEncounter);
            if (s.RecentEncounters != null && s.RecentEncounters.Count > 1)
            {
                sb.AppendLine();
                sb.AppendLine("RECENT FIGHTS");
                for (int i = 0; i < s.RecentEncounters.Count; i++) sb.AppendLine("- " + s.RecentEncounters[i]);
            }
            if (s.Facts != null && s.Facts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("VERIFIED SESSION FACTS");
                for (int i = 0; i < s.Facts.Count; i++) sb.AppendLine("- " + s.Facts[i]);
            }
            return sb.ToString();
        }

        private static string Safe(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        internal static string ReadActorName(UnityEngine.GameObject go)
        {
            if (go == null) return "enemy";
            try
            {
                NPC npc = go.GetComponent<NPC>();
                if (npc == null) npc = go.GetComponentInParent<NPC>();
                object value = ReadMember(npc, new string[] { "NPCName", "Name", "name", "DisplayName" });
                string text = value == null ? string.Empty : Convert.ToString(value);
                if (!string.IsNullOrWhiteSpace(text)) return CleanName(text, "enemy");
            }
            catch { }
            return CleanName(go.name, "enemy");
        }

        internal static string ReadItemName(Item item)
        {
            if (item == null) return "item";
            object value = ReadMember(item, new string[] { "ItemName", "Name", "name", "DisplayName", "itemName" });
            string text = value == null ? string.Empty : Convert.ToString(value);
            if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            try
            {
                text = item.ToString();
                if (!string.IsNullOrWhiteSpace(text) && !text.Contains("Item")) return text.Trim();
            }
            catch { }
            return "item";
        }

        private void StartOuting(WorldSnapshot world, IList<SimSnapshot> active, DateTime now)
        {
            _active = true;
            _startedUtc = now;
            _lastObserveUtc = now;
            _lastCombatUtc = DateTime.MinValue;
            _lastProgressUtc = DateTime.MinValue;
            _lastRestUtc = DateTime.MinValue;
            _lastZoneChangeUtc = now;
            _currentZone = world == null || !VerifiedOutingHistoryPolicy.IsDurableGameplayLocation(world.Scene)
                ? string.Empty : (world.Scene ?? string.Empty);
            _playerDeaths = 0;
            _playerCloseCalls = 0;
            _playerLowHealthCooldown = DateTime.MinValue;
            _dangerEvents = 0;
            _quests = 0;
            _levels = 0;
            _gold = 0;
            _experience = 0;
            _currentCombatTarget = string.Empty;
            _currentCombatTargetUtc = DateTime.MinValue;
            _kills.Clear();
            _killsByKiller.Clear();
            _loot.Clear();
            _gearLikeLoot.Clear();
            _observedLootSources.Clear();
            _observedLootSourceUtc.Clear();
            _lastKilledEnemy = string.Empty;
            _lastKillUtc = DateTime.MinValue;
            _participants.Clear();
            _simCloseCalls.Clear();
            _simDeaths.Clear();
            _lowHealthCooldown.Clear();
            _zones.Clear();
            _zoneSeconds.Clear();
            _recentVerified.Clear();
            _lastActiveKeys.Clear();
            _keysWithOutingGap.Clear();
            _participantSeconds.Clear();
            _pairSeconds.Clear();
            _currentEncounter = null;
            _recentEncounters.Clear();
            _recentCompletedEncounters.Clear();
            _lastCompletedEncounter = null;
            _nextEncounterId = 1;
            if (!string.IsNullOrWhiteSpace(_currentZone)) _zones.Add(_currentZone);
            for (int i = 0; i < active.Count; i++)
            {
                SimSnapshot sim = active[i];
                if (sim != null && !string.IsNullOrWhiteSpace(sim.Key))
                {
                    _participants[sim.Key] = sim;
                    _lastActiveKeys.Add(sim.Key);
                }
            }
        }

        private void FinishOuting(DateTime now)
        {
            AccumulateRelationshipTime(now);
            AccumulateZoneTime(now);
            if (_currentEncounter != null) FinalizeEncounter(now);
            int minutes = Math.Max(1, (int)Math.Round((now - _startedUtc).TotalMinutes));
            List<SimSnapshot> outingParty = new List<SimSnapshot>();
            foreach (KeyValuePair<string, SimSnapshot> pair in _participants)
            {
                if (pair.Value != null)
                {
                    outingParty.Add(pair.Value);
                    double seconds;
                    if (_participantSeconds.TryGetValue(pair.Key, out seconds) && seconds >= 120.0)
                    {
                        int participantMinutes = Math.Max(2, (int)Math.Round(seconds / 60.0));
                        bool continuousPresence = _lastActiveKeys.Contains(pair.Key) && !_keysWithOutingGap.Contains(pair.Key);
                        _memory.RecordOutingParticipation(pair.Value, BuildSummary(participantMinutes), participantMinutes, continuousPresence);
                    }
                }
            }
            for (int i = 0; i < outingParty.Count; i++)
            {
                SimSnapshot first = outingParty[i];
                if (first == null) continue;
                for (int j = i + 1; j < outingParty.Count; j++)
                {
                    SimSnapshot second = outingParty[j];
                    if (second == null) continue;
                    double sharedSeconds;
                    if (_pairSeconds.TryGetValue(PairKey(first.Key, second.Key), out sharedSeconds) && sharedSeconds >= 120.0)
                        _memory.RecordSharedOutingPair(first, second, Math.Max(2, (int)Math.Round(sharedSeconds / 60.0)));
                }
            }
            _active = false;
            _participants.Clear();
            _lowHealthCooldown.Clear();
            _lastActiveKeys.Clear();
            _keysWithOutingGap.Clear();
            _participantSeconds.Clear();
            _pairSeconds.Clear();
            _currentEncounter = null;
            _recentEncounters.Clear();
            _recentCompletedEncounters.Clear();
            _lastCompletedEncounter = null;
        }

        internal void FinishNow()
        {
            lock (_lock)
            {
                if (_active)
                {
                    DateTime now = UtcNow();
                    if ((now - _startedUtc).TotalMinutes >= 2.0) FinishOuting(now);
                    else ResetWithoutSaving();
                }
            }
        }

        private void UpdateActiveParty(IList<SimSnapshot> active)
        {
            HashSet<string> current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < active.Count; i++)
            {
                SimSnapshot sim = active[i];
                if (sim != null && !string.IsNullOrWhiteSpace(sim.Key)) current.Add(sim.Key);
            }
            foreach (string previous in _lastActiveKeys)
                if (!current.Contains(previous)) _keysWithOutingGap.Add(previous);
            _lastActiveKeys.Clear();
            for (int i = 0; i < active.Count; i++)
            {
                SimSnapshot sim = active[i];
                if (sim == null || string.IsNullOrWhiteSpace(sim.Key)) continue;
                _lastActiveKeys.Add(sim.Key);
                _participants[sim.Key] = sim;
            }
        }

        private void AccumulateRelationshipTime(DateTime now)
        {
            if (!_active || _lastObserveUtc == DateTime.MinValue || _lastActiveKeys.Count == 0) return;
            double seconds = Math.Max(0.0, Math.Min(10.0, (now - _lastObserveUtc).TotalSeconds));
            if (seconds <= 0.0) return;
            List<string> keys = new List<string>(_lastActiveKeys);
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                double current;
                if (!_participantSeconds.TryGetValue(key, out current)) current = 0.0;
                _participantSeconds[key] = current + seconds;
                for (int j = i + 1; j < keys.Count; j++)
                {
                    string pair = PairKey(key, keys[j]);
                    if (!_pairSeconds.TryGetValue(pair, out current)) current = 0.0;
                    _pairSeconds[pair] = current + seconds;
                }
            }
        }

        private static string PairKey(string first, string second)
        {
            string a = first ?? string.Empty;
            string b = second ?? string.Empty;
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0 ? a + "\u001f" + b : b + "\u001f" + a;
        }

        private void ResetWithoutSaving()
        {
            _active = false;
            _participants.Clear();
            _lowHealthCooldown.Clear();
            _lastActiveKeys.Clear();
            _keysWithOutingGap.Clear();
            _participantSeconds.Clear();
            _pairSeconds.Clear();
            _currentEncounter = null;
            _recentEncounters.Clear();
            _recentCompletedEncounters.Clear();
            _lastCompletedEncounter = null;
        }

        private string BuildSummary(int minutes)
        {
            // Keep long-term memory deliberately tiny: at most two compact sentences.
            int totalKills = Sum(_kills);
            StringBuilder first = new StringBuilder();
            first.Append("Grouped for about ").Append(minutes).Append(" minutes");
            if (_zones.Count == 1)
            {
                foreach (string zone in _zones) { first.Append(" in ").Append(zone); break; }
            }
            else if (_zones.Count > 1) first.Append(" across multiple verified areas");
            first.Append(".");

            List<string> details = new List<string>();
            if (totalKills > 0)
            {
                string top = TopEntry(_kills, string.Empty);
                details.Add("Fought " + totalKills + " enemies" + (!string.IsNullOrWhiteSpace(top) ? ", mostly " + top : string.Empty));
            }
            string gear = TopEntry(_gearLikeLoot, string.Empty);
            if (!string.IsNullOrWhiteSpace(gear)) details.Add("found " + gear);
            else
            {
                string loot = TopEntry(_loot, string.Empty);
                if (!string.IsNullOrWhiteSpace(loot)) details.Add("picked up " + loot);
            }
            int deaths = _playerDeaths + Sum(_simDeaths);
            if (deaths > 0) details.Add("had " + deaths + " death" + (deaths == 1 ? string.Empty : "s"));
            else
            {
                int close = _playerCloseCalls + Sum(_simCloseCalls);
                if (close > 0) details.Add("had " + close + " close call" + (close == 1 ? string.Empty : "s"));
            }
            if (_quests > 0) details.Add("completed " + _quests + " quest" + (_quests == 1 ? string.Empty : "s"));

            if (details.Count == 0) return first.ToString();
            if (details.Count > 3) details.RemoveRange(3, details.Count - 3);
            return first.ToString() + " The group " + JoinNatural(details) + ".";
        }

        private List<string> BuildFacts(int max)
        {
            List<string> facts = new List<string>();
            if (!string.IsNullOrWhiteSpace(_currentZone)) facts.Add("Current outing is in " + _currentZone + ".");
            DateTime now = UtcNow();
            if (!string.IsNullOrWhiteSpace(_currentCombatTarget) && (now - _currentCombatTargetUtc).TotalSeconds <= 15.0 && facts.Count < max)
                facts.Add("Currently/recently fighting " + _currentCombatTarget + ".");
            string zoneHistory = BuildZoneHistory(3);
            if (_zones.Count > 1 && !string.IsNullOrWhiteSpace(zoneHistory) && facts.Count < max) facts.Add("Zone time this outing: " + zoneHistory + ".");

            AddTopEntries(facts, _kills, "Killed ", max);
            if (_killsByKiller.Count > 0 && facts.Count < max)
            {
                string topKiller = TopEntry(_killsByKiller, string.Empty);
                if (!string.IsNullOrWhiteSpace(topKiller)) facts.Add("Most recorded killing blows: " + topKiller + ".");
            }
            AddTopEntries(facts, _gearLikeLoot, "Found gear-like item ", max);
            AddTopEntriesExcluding(facts, _loot, _gearLikeLoot, "Looted ", max);
            if (_observedLootSources.Count > 0 && facts.Count < max)
            {
                int shownSources = 0;
                foreach (KeyValuePair<string, string> pair in _observedLootSources)
                {
                    if (facts.Count >= max || shownSources >= 2) break;
                    if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    {
                        facts.Add("Observed " + pair.Key + " looted shortly after killing " + pair.Value + ".");
                        shownSources++;
                    }
                }
            }
            if (_gold > 0 && facts.Count < max) facts.Add("Found " + _gold + " gold this outing.");
            if (_experience > 0 && facts.Count < max) facts.Add("The player gained about " + _experience + " XP this outing.");
            if (_quests > 0 && facts.Count < max) facts.Add("Completed " + _quests + " quest" + (_quests == 1 ? string.Empty : "s") + " this outing.");
            if (_levels > 0 && facts.Count < max) facts.Add("Observed " + _levels + " level-up" + (_levels == 1 ? string.Empty : "s") + " this outing.");
            if (_playerDeaths > 0 && facts.Count < max) facts.Add("The player died " + _playerDeaths + " time" + (_playerDeaths == 1 ? string.Empty : "s") + " this outing.");
            else if (_playerCloseCalls > 0 && facts.Count < max) facts.Add("The player had " + _playerCloseCalls + " low-health close call" + (_playerCloseCalls == 1 ? string.Empty : "s") + " this outing.");
            AddPersonCounts(facts, _simDeaths, " died ", " time", max);
            AddPersonCounts(facts, _simCloseCalls, " had ", " low-health close call", max);

            // The freshest verified facts are useful for immediate chatter, but cap them hard.
            for (int i = Math.Max(0, _recentVerified.Count - 3); i < _recentVerified.Count && facts.Count < max; i++)
            {
                string f = _recentVerified[i];
                if (!ContainsEquivalent(facts, f)) facts.Add(f);
            }
            if (facts.Count > max) facts.RemoveRange(max, facts.Count - max);
            return facts;
        }

        private void TouchEncounter(string target, DateTime now)
        {
            if (_currentEncounter != null && (now - _currentEncounter.LastActivityUtc).TotalSeconds > EncounterQuietSeconds)
                FinalizeEncounter(_currentEncounter.LastActivityUtc);
            if (_currentEncounter == null)
            {
                _currentEncounter = new EncounterState();
                _currentEncounter.StartedUtc = now;
                _currentEncounter.Zone = _currentZone ?? string.Empty;
                foreach (SimSnapshot participant in _participants.Values)
                    if (participant != null && !string.IsNullOrWhiteSpace(participant.Name)) _currentEncounter.Participants.Add(participant.Name);
            }
            _currentEncounter.LastActivityUtc = now;
            if (!string.IsNullOrWhiteSpace(target)) _currentEncounter.Enemies.Add(target);
        }

        private void FinalizeEncounterIfQuiet(DateTime now)
        {
            if (_currentEncounter == null) return;
            if ((now - _currentEncounter.LastActivityUtc).TotalSeconds <= EncounterQuietSeconds) return;
            // Do not finalize a fight merely because no new target/action line was parsed for a few seconds.
            // The global combat timestamp is a second independent signal and must also be quiet.
            if (_lastCombatUtc != DateTime.MinValue && (now - _lastCombatUtc).TotalSeconds <= EncounterQuietSeconds) return;
            FinalizeEncounter(_currentEncounter.LastActivityUtc);
        }

        private void FinalizeEncounter(DateTime endedUtc)
        {
            if (_currentEncounter == null) return;
            EncounterState e = _currentEncounter;
            _currentEncounter = null;
            int seconds = Math.Max(1, (int)Math.Round((endedUtc - e.StartedUtc).TotalSeconds));
            int kills = Sum(e.Kills);
            string enemy = TopEntry(e.Kills, string.Empty);
            if (string.IsNullOrWhiteSpace(enemy) && e.Enemies.Count > 0)
            {
                foreach (string name in e.Enemies) { enemy = name; break; }
            }

            EncounterSnapshot record = new EncounterSnapshot();
            record.Id = _nextEncounterId++;
            record.StartedUtc = e.StartedUtc.ToString("o");
            record.EndedUtc = endedUtc.ToString("o");
            record.PrimaryEnemy = enemy;
            record.EnemyTypes = new List<string>();
            record.NotableSimActions = new List<string>(e.NotableActions);
            foreach (string name in e.Enemies)
                if (!string.IsNullOrWhiteSpace(name) && !record.EnemyTypes.Contains(name)) record.EnemyTypes.Add(name);
            record.TotalKills = kills;
            record.CloseCalls = e.CloseCalls;
            record.Deaths = e.Deaths;
            record.DurationSeconds = seconds;
            record.Zone = string.IsNullOrWhiteSpace(e.Zone) ? (_currentZone ?? string.Empty) : e.Zone;
            record.Result = kills > 0 ? "kills recorded" : "no kill recorded";

            StringBuilder sb = new StringBuilder();
            sb.Append("Completed fight");
            if (!string.IsNullOrWhiteSpace(record.Zone)) sb.Append(" in ").Append(record.Zone);
            sb.Append(": ");
            if (kills > 0)
            {
                sb.Append(kills).Append(kills == 1 ? " kill" : " kills");
                if (!string.IsNullOrWhiteSpace(enemy)) sb.Append(", mainly ").Append(enemy);
            }
            else if (!string.IsNullOrWhiteSpace(enemy)) sb.Append("fought ").Append(enemy).Append("; no kill was recorded");
            else sb.Append("no kill was recorded");
            if (e.Deaths > 0) sb.Append("; ").Append(e.Deaths).Append(e.Deaths == 1 ? " party death" : " party deaths");
            else if (e.CloseCalls > 0) sb.Append("; ").Append(e.CloseCalls).Append(e.CloseCalls == 1 ? " close call" : " close calls");
            else sb.Append("; no recorded deaths or close calls");
            sb.Append(".");
            record.Summary = sb.ToString();
            _lastCompletedEncounter = record;

            _recentEncounters.Add(record.Summary);
            while (_recentEncounters.Count > 3) _recentEncounters.RemoveAt(0);
            _recentCompletedEncounters.Add(record);
            while (_recentCompletedEncounters.Count > 3) _recentCompletedEncounters.RemoveAt(0);
            if (_plugin != null) _plugin.NotifyCompletedEncounter(record, new List<string>(e.Participants), MaxCount(e.Kills));
        }

        private static List<EncounterSnapshot> CloneEncounters(List<EncounterSnapshot> source)
        {
            List<EncounterSnapshot> copies = new List<EncounterSnapshot>();
            if (source == null) return copies;
            for (int i = 0; i < source.Count; i++)
            {
                EncounterSnapshot item = source[i];
                if (item == null) continue;
                EncounterSnapshot copy = new EncounterSnapshot();
                copy.Id = item.Id;
                copy.StartedUtc = item.StartedUtc;
                copy.EndedUtc = item.EndedUtc;
                copy.PrimaryEnemy = item.PrimaryEnemy;
                copy.EnemyTypes = item.EnemyTypes == null ? new List<string>() : new List<string>(item.EnemyTypes);
                copy.NotableSimActions = item.NotableSimActions == null ? new List<string>() : new List<string>(item.NotableSimActions);
                copy.TotalKills = item.TotalKills;
                copy.CloseCalls = item.CloseCalls;
                copy.Deaths = item.Deaths;
                copy.DurationSeconds = item.DurationSeconds;
                copy.Zone = item.Zone;
                copy.Result = item.Result;
                copy.Summary = item.Summary;
                copies.Add(copy);
            }
            return copies;
        }

        private string DescribeCurrentEncounter(DateTime now)
        {
            if (_currentEncounter == null || (now - _currentEncounter.LastActivityUtc).TotalSeconds > EncounterQuietSeconds) return string.Empty;
            int kills = Sum(_currentEncounter.Kills);
            string enemy = !string.IsNullOrWhiteSpace(_currentCombatTarget) ? _currentCombatTarget : TopEntry(_currentEncounter.Kills, string.Empty);
            if (string.IsNullOrWhiteSpace(enemy) && _currentEncounter.Enemies.Count > 0)
            {
                foreach (string name in _currentEncounter.Enemies) { enemy = name; break; }
            }
            StringBuilder sb = new StringBuilder();
            sb.Append("Fight currently in progress");
            if (!string.IsNullOrWhiteSpace(enemy)) sb.Append(" against ").Append(enemy);
            if (kills > 0) sb.Append("; ").Append(kills).Append(kills == 1 ? " kill recorded in this fight" : " kills recorded in this fight");
            else sb.Append("; no kill recorded in this fight yet");
            if (_currentEncounter.CloseCalls > 0) sb.Append("; ").Append(_currentEncounter.CloseCalls).Append(_currentEncounter.CloseCalls == 1 ? " close call" : " close calls");
            sb.Append(".");
            return sb.ToString();
        }

        private bool IsVerifiedPartyKiller(string killer)
        {
            return IsVerifiedPartyKiller(killer, _playerName, _participants);
        }

        private static bool IsVerifiedPartyKiller(string killer, string playerName, Dictionary<string, SimSnapshot> participants)
        {
            if (string.IsNullOrWhiteSpace(killer)) return false;
            string name = CleanName(killer, string.Empty);
            if (string.Equals(name, "you", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "player", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(playerName) && string.Equals(name, playerName, StringComparison.OrdinalIgnoreCase))) return true;
            return IsActivePartySimName(name, participants);
        }

        private bool IsActivePartySimName(string name)
        {
            return IsActivePartySimName(name, _participants);
        }

        private static bool IsActivePartySimName(string name, Dictionary<string, SimSnapshot> participants)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (participants == null) return false;
            foreach (KeyValuePair<string, SimSnapshot> pair in participants)
            {
                SimSnapshot snap = pair.Value;
                if (snap != null && !string.IsNullOrWhiteSpace(snap.Name) &&
                    string.Equals(snap.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        internal static List<string> RunAttributionSelfTests()
        {
            Dictionary<string, SimSnapshot> participants = new Dictionary<string, SimSnapshot>(StringComparer.OrdinalIgnoreCase);
            participants["phanty"] = new SimSnapshot { Key = "phanty", Name = "Phanty" };
            List<string> results = new List<string>();
            AddAttributionSelfTest(results, "local player kill", IsVerifiedPartyKiller("Brinon", "Brinon", participants), true);
            AddAttributionSelfTest(results, "active party Sim kill", IsVerifiedPartyKiller("Phanty", "Brinon", participants), true);
            AddAttributionSelfTest(results, "unrelated killer rejected", IsVerifiedPartyKiller("Stranger", "Brinon", participants), false);
            AddAttributionSelfTest(results, "active party Sim death", IsActivePartySimName("Phanty", participants), true);
            AddAttributionSelfTest(results, "ungrouped Sim death rejected", IsActivePartySimName("Wanderer", participants), false);
            return results;
        }

        private static void AddAttributionSelfTest(List<string> results, string name, bool actual, bool expected)
        {
            results.Add("[DeepSims Telemetry] " + name + ": " + (actual == expected ? "PASS" : "FAIL"));
        }

        private string DetermineActivity(DateTime now)
        {
            if ((now - _lastCombatUtc).TotalSeconds < 12) return "combat/recent combat";
            if ((now - _lastRestUtc).TotalSeconds < 20) return "resting/meditating";
            if ((now - _lastProgressUtc).TotalSeconds < 30) return "looting/recent progress";
            if ((now - _lastZoneChangeUtc).TotalSeconds < 20) return "traveling/just changed zones";
            return "adventuring/downtime";
        }

        private void ObservePlayerLowHealth(PlayerSnapshot player, DateTime now)
        {
            if (CompetitiveCombatOwnsGenericProxy(now)) return;
            if (player == null || player.HpPercent < 0f || player.IsDead) return;
            if (player.HpPercent > 25f) return;
            if (_playerLowHealthCooldown != DateTime.MinValue && (now - _playerLowHealthCooldown).TotalSeconds < 75) return;
            _playerLowHealthCooldown = now;
            _playerCloseCalls++;
            _dangerEvents++;
            if (_currentEncounter != null) _currentEncounter.CloseCalls++;
            string text = "The player dropped below 25% health in " + (_currentZone.Length == 0 ? "the current area" : _currentZone) + ".";
            AddRecentVerified(text);
        }

        private void ObserveLowHealth(SimSnapshot sim, DateTime now)
        {
            if (CompetitiveCombatOwnsGenericProxy(now)) return;
            if (sim == null) return;
            try
            {
                float ratio;
                if (sim.HpPercent >= 0f) ratio = sim.HpPercent / 100f;
                else if (sim.RuntimeSim != null)
                {
                    Stats stats = sim.RuntimeSim.GetComponent<Stats>();
                    if (stats == null || stats.CurrentHP <= 0) return;
                    float maxHp = ReadNumber(stats, new string[] { "MaxHP", "MaximumHP", "HPMax", "BaseMaxHP" });
                    if (maxHp <= 0) return;
                    ratio = stats.CurrentHP / maxHp;
                }
                else return;

                if (ratio <= 0f || ratio > 0.25f) return;
                DateTime prior;
                if (_lowHealthCooldown.TryGetValue(sim.Key, out prior) && (now - prior).TotalSeconds < 75) return;
                _lowHealthCooldown[sim.Key] = now;
                if (_currentEncounter != null) _currentEncounter.CloseCalls++;
                string text = sim.Name + " dropped below 25% health in " + (_currentZone.Length == 0 ? "the current area" : _currentZone) + ".";
                _plugin.NotifyObservedGameEvent("sim_low_health", text, 45, false, 0.35);
            }
            catch { }
        }

        private void AccumulateZoneTime(DateTime now)
        {
            if (!_active || _lastObserveUtc == DateTime.MinValue || string.IsNullOrWhiteSpace(_currentZone)) return;
            double seconds = Math.Max(0.0, Math.Min(10.0, (now - _lastObserveUtc).TotalSeconds));
            double current;
            if (!_zoneSeconds.TryGetValue(_currentZone, out current)) current = 0.0;
            _zoneSeconds[_currentZone] = current + seconds;
            _lastObserveUtc = now;
        }

        private string BuildZoneHistory(int maxZones)
        {
            List<KeyValuePair<string, double>> values = new List<KeyValuePair<string, double>>(_zoneSeconds);
            values.Sort(delegate(KeyValuePair<string, double> a, KeyValuePair<string, double> b) { return b.Value.CompareTo(a.Value); });
            List<string> parts = new List<string>();
            for (int i = 0; i < values.Count && i < maxZones; i++)
            {
                int minutes = Math.Max(1, (int)Math.Round(values[i].Value / 60.0));
                parts.Add(values[i].Key + " ~" + minutes + "m");
            }
            return string.Join(", ", parts.ToArray());
        }

        private void AddRecentVerified(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            string clean = text.Trim();
            if (_recentVerified.Count > 0 && string.Equals(_recentVerified[_recentVerified.Count - 1], clean, StringComparison.OrdinalIgnoreCase)) return;
            _recentVerified.Add(clean);
            if (_recentVerified.Count > 10) _recentVerified.RemoveRange(0, _recentVerified.Count - 10);
        }

        private static bool LooksGearLike(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return false;
            string n = itemName.ToLowerInvariant();
            string[] gearWords = new string[]
            {
                "sword", "axe", "hatchet", "mace", "hammer", "staff", "wand", "bow", "dagger", "cutlass", "blade", "spear", "shield",
                "helm", "helmet", "hat", "hood", "robe", "armor", "armour", "mail", "tunic", "shirt", "pants", "leggings", "boots", "gloves", "gauntlet",
                "ring", "necklace", "amulet", "charm", "bracelet", "belt", "cloak", "cape"
            };
            for (int i = 0; i < gearWords.Length; i++) if (n.Contains(gearWords[i])) return true;
            return false;
        }

        private static void Increment(Dictionary<string, int> map, string key, int amount)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            int value;
            if (!map.TryGetValue(key, out value)) value = 0;
            map[key] = value + amount;
        }

        private DateTime UtcNow()
        {
            return _utcNow();
        }

        private static int Sum(Dictionary<string, int> map)
        {
            int total = 0;
            foreach (KeyValuePair<string, int> pair in map) total += Math.Max(0, pair.Value);
            return total;
        }

        private static int MaxCount(Dictionary<string, int> map)
        {
            int max = 0;
            if (map == null) return max;
            foreach (int value in map.Values) if (value > max) max = value;
            return max;
        }

        private static void AddTopEntries(List<string> output, Dictionary<string, int> map, string prefix, int max)
        {
            List<KeyValuePair<string, int>> values = new List<KeyValuePair<string, int>>(map);
            values.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value.CompareTo(a.Value); });
            for (int i = 0; i < values.Count && output.Count < max && i < 3; i++)
            {
                string unit = values[i].Value > 1 ? " x" + values[i].Value : string.Empty;
                output.Add(prefix + values[i].Key + unit + ".");
            }
        }

        private static void AddTopEntriesExcluding(List<string> output, Dictionary<string, int> map, Dictionary<string, int> excluded, string prefix, int max)
        {
            List<KeyValuePair<string, int>> values = new List<KeyValuePair<string, int>>(map);
            values.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Value.CompareTo(a.Value); });
            int added = 0;
            for (int i = 0; i < values.Count && output.Count < max && added < 3; i++)
            {
                if (excluded != null && excluded.ContainsKey(values[i].Key)) continue;
                string unit = values[i].Value > 1 ? " x" + values[i].Value : string.Empty;
                output.Add(prefix + values[i].Key + unit + ".");
                added++;
            }
        }

        private static void AddPersonCounts(List<string> output, Dictionary<string, int> map, string middle, string suffix, int max)
        {
            foreach (KeyValuePair<string, int> pair in map)
            {
                if (output.Count >= max) break;
                output.Add(pair.Key + middle + pair.Value + suffix + (pair.Value == 1 ? string.Empty : "s") + " this outing.");
            }
        }

        private static string TopEntry(Dictionary<string, int> map, string prefix)
        {
            string best = null;
            int count = 0;
            foreach (KeyValuePair<string, int> pair in map)
            {
                if (pair.Value > count) { best = pair.Key; count = pair.Value; }
            }
            if (best == null) return string.Empty;
            return prefix + best + (count > 1 ? " x" + count : string.Empty);
        }

        private static string ExtractBefore(string text, string marker)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrEmpty(marker)) return string.Empty;
            int index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            return index <= 0 ? string.Empty : text.Substring(0, index).Trim();
        }

        private static string JoinNatural(List<string> parts)
        {
            if (parts == null || parts.Count == 0) return string.Empty;
            if (parts.Count == 1) return parts[0];
            if (parts.Count == 2) return parts[0] + " and " + parts[1];
            return parts[0] + ", " + parts[1] + ", and " + parts[2];
        }

        private static bool ContainsEquivalent(List<string> facts, string candidate)
        {
            if (facts == null || string.IsNullOrWhiteSpace(candidate)) return false;
            for (int i = 0; i < facts.Count; i++)
            {
                string existing = facts[i];
                if (string.IsNullOrWhiteSpace(existing)) continue;
                if (string.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string CleanName(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            string s = value.Trim().Replace("(Clone)", string.Empty).Trim();
            return s.Length == 0 ? fallback : s;
        }

        private static object ReadMember(object obj, string[] names)
        {
            if (obj == null) return null;
            Type type = obj.GetType();
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    FieldInfo field = type.GetField(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null) return field.GetValue(obj);
                    PropertyInfo prop = type.GetProperty(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null && prop.GetIndexParameters().Length == 0) return prop.GetValue(obj, null);
                }
                catch { }
            }
            return null;
        }

        private static float ReadNumber(object obj, string[] names)
        {
            object value = ReadMember(obj, names);
            if (value == null) return 0f;
            try { return Convert.ToSingle(value); } catch { return 0f; }
        }
    }

    internal static class VerifiedOutingHistoryPolicy
    {
        private static readonly Regex GeneratedOutingLocation = new Regex(
            @"^(Grouped for about\s+\d+\s+minutes)\s+in\s+([^\.]+)\.(.*)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static bool IsDurableGameplayLocation(string scene)
        {
            if (string.IsNullOrWhiteSpace(scene)) return false;
            string compact = Regex.Replace(scene.Trim().ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);
            return compact != "menu" && compact != "mainmenu" && compact != "loading" &&
                compact != "loadingscene" && compact != "characterselect" && compact != "characterselection" &&
                compact != "bootstrap" && compact != "initialization" && compact != "initializingscene";
        }

        internal static string SanitizeForPrompt(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary)) return string.Empty;
            string clean = summary.Trim();
            Match match = GeneratedOutingLocation.Match(clean);
            if (!match.Success || IsDurableGameplayLocation(match.Groups[2].Value)) return clean;
            // Preserve duration and any independently observed result; omit only the invalid
            // pseudo-location clause. This deliberately does not infer the prior or next zone.
            return match.Groups[1].Value.Trim() + "." + match.Groups[3].Value;
        }
    }
}
