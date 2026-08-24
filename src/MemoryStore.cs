using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace ErenshorDeepSims
{
    internal class MemoryStore
    {
        private readonly string _directory;
        private readonly IDeepSimsLog _log;
        private readonly object _ioLock = new object();
        private readonly Dictionary<string, SimMemory> _cache = new Dictionary<string, SimMemory>(StringComparer.OrdinalIgnoreCase);

        // Every recorded event used to serialize a whole memory file synchronously on Unity's main
        // thread. At idle that was one write per slot per party poll; a single party message could
        // trigger a couple of dozen. The in-memory cache is authoritative, so mark dirty here and let
        // the plugin flush on a timer (and force a flush on shutdown).
        private readonly HashSet<string> _dirtyKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private DateTime _nextFlushUtc = DateTime.MinValue;
        private const double FlushIntervalSeconds = 5.0;

        // _ioLock owns the live cache/dirty set. The writer lock owns immutable snapshots waiting
        // for disk. Exactly one background thread serializes and writes them, so Unity's main thread
        // never performs JSON serialization or file replacement during an ordinary flush.
        private readonly object _writerLock = new object();
        private readonly Dictionary<string, SimMemory> _pendingWrites = new Dictionary<string, SimMemory>(StringComparer.OrdinalIgnoreCase);
        private readonly AutoResetEvent _writerSignal = new AutoResetEvent(false);
        private readonly ManualResetEventSlim _writerIdle = new ManualResetEventSlim(true);
        private readonly Thread _writerThread;
        private readonly Func<SimMemory, bool> _writeAttemptGate;
        private volatile bool _writerStopping;
        private const int MaxPendingWrites = 32;
        private const int NormalFlushBatch = 16;

        internal bool WriterAlive { get { return _writerThread != null && _writerThread.IsAlive && !_writerStopping; } }

        internal MemoryStore(string directory, IDeepSimsLog log, Func<SimMemory, bool> writeAttemptGate = null)
        {
            _directory = directory;
            _log = log;
            // Deterministic tests use this seam to simulate one transient I/O failure. Production
            // callers leave it null, so it adds no work to the ordinary persistence path.
            _writeAttemptGate = writeAttemptGate;
            Directory.CreateDirectory(_directory);
            _writerThread = new Thread(WriterLoop);
            _writerThread.IsBackground = true;
            _writerThread.Name = "DeepSims memory writer";
            _writerThread.Start();
        }

        internal SimMemory GetOrCreate(SimSnapshot sim)
        {
            lock (_ioLock)
            {
                SimMemory memory;
                if (!_cache.TryGetValue(sim.Key, out memory))
                {
                    memory = LoadUnsafe(sim.Key);
                    if (memory == null)
                    {
                        memory = new SimMemory();
                        memory.SimKey = sim.Key;
                        memory.Name = sim.Name;
                        memory.FirstSeenUtc = UtcNow();
                        memory.RecentEvents = new List<MemoryEvent>();
                        memory.ImportantMemories = new List<string>();
                        memory.Conversation = new List<ChatMessage>();
                        AddImportantUnsafe(memory, "First observed by Deep Sims around " + FriendlyDate() + ".");
                    }
                    memory.Normalize();
                    _cache[sim.Key] = memory;
                }

                UpdateSnapshotUnsafe(memory, sim);
                // Prompt reads are an identity-system initialization boundary, not merely a cache
                // lookup. Generated fictional-player identity must exist before the first MMO IRL
                // question even when this Sim has never been selected in the Identity Editor.
                SimulatedPlayerBackgroundPolicy.Ensure(memory, sim);
                GeneratedIdentityMotivationPolicy.Ensure(memory, sim);
                MarkDirtyUnsafe(memory);
                return CloneMemory(memory);
            }
        }

        internal bool HasHistory(string simKey)
        {
            lock (_ioLock)
            {
                SimMemory memory;
                if (_cache.TryGetValue(simKey, out memory))
                {
                    IdentitySchema.Normalize(memory);
                    AuthoredIdentityProfile a = memory.AuthoredIdentity;
                    return memory.GroupSessions > 0 || memory.Conversation.Count > 0 || memory.ImportantMemories.Count > 1 ||
                        memory.StructuredMemories.Count > 0 || !string.IsNullOrWhiteSpace(a.CorePersonality) ||
                        !string.IsNullOrWhiteSpace(a.PersonalBackground) || !string.IsNullOrWhiteSpace(a.ErenshorPersona) ||
                        !string.IsNullOrWhiteSpace(a.RelationshipToPlayer) || !string.IsNullOrWhiteSpace(a.LongTermWants) ||
                        !string.IsNullOrWhiteSpace(a.CaresAbout) || a.SharedHistory.Count > 0 || a.PinnedMemories.Count > 0;
                }
                return File.Exists(GetPath(simKey));
            }
        }

        internal SimMemory LoadForPrompt(SimSnapshot sim)
        {
            return GetOrCreate(sim);
        }

        // Speaker scoring only needs the familiarity nudge. Routing it through LoadForPrompt meant a
        // full JSON clone (and formerly a disk write) for every candidate Sim, several times per reply.
        internal float GetFamiliarity(SimSnapshot sim)
        {
            if (sim == null || string.IsNullOrWhiteSpace(sim.Key)) return 0f;
            lock (_ioLock)
            {
                SimMemory memory;
                if (_cache.TryGetValue(sim.Key, out memory) && memory != null) return memory.Familiarity;
            }
            return 0f;
        }

        internal string GetRecentLifeIdentityInfluence(string simName, string simKey)
        {
            lock (_ioLock)
            {
                SimMemory memory;
                if (!_cache.TryGetValue(simKey ?? string.Empty, out memory) || memory == null)
                    memory = FindMemoryUnsafe(string.IsNullOrWhiteSpace(simKey) ? simName : simKey);
                if (memory == null) return string.Empty;
                memory.Normalize();
                AuthoredIdentityProfile identity = memory.AuthoredIdentity;
                return (identity.CorePersonality + " " + identity.PersonalBackground + " " + identity.LongTermWants + " " +
                    identity.CaresAbout + " " + memory.GeneratedLongTermWants + " " + memory.GeneratedCaresAbout).Trim();
            }
        }

        internal RelationshipTone GetRelationshipTone(SimSnapshot sim, string otherSimName)
        {
            if (sim == null || string.IsNullOrWhiteSpace(sim.Key)) return RelationshipModel.Describe(0f, 0f, 0f);
            lock (_ioLock)
            {
                SimMemory memory;
                if (!_cache.TryGetValue(sim.Key, out memory) || memory == null) return RelationshipModel.Describe(0f, 0f, 0f);
                if (string.IsNullOrWhiteSpace(otherSimName)) return RelationshipModel.Describe(memory);
                if (memory.SimRelationships != null)
                {
                    for (int i = 0; i < memory.SimRelationships.Count; i++)
                    {
                        SimRelationshipMemory relation = memory.SimRelationships[i];
                        if (relation != null && string.Equals(relation.OtherName, otherSimName, StringComparison.OrdinalIgnoreCase))
                            return RelationshipModel.Describe(relation);
                    }
                }
                return RelationshipModel.Describe(0f, 0f, 0f);
            }
        }

        internal void RecordGroupJoin(SimSnapshot sim)
        {
            lock (_ioLock)
            {
                SimMemory memory = GetOrCreateUnsafe(sim);
                memory.GroupSessions += 1;
                // Joining is diagnostic history, not relationship progress. A reconnect or party
                // reshuffle must not manufacture familiarity before a real outing is completed.
                AddEventUnsafe(memory, "group_join", sim.Name + " joined the player's group in " + Safe(sim.Scene, "the current zone") + ".", 45);
                MarkDirtyUnsafe(memory);
            }
        }

        internal void RecordGroupLeave(SimSnapshot sim)
        {
            lock (_ioLock)
            {
                SimMemory memory = GetOrCreateUnsafe(sim);
                AddEventUnsafe(memory, "group_leave", sim.Name + " left the player's group in " + Safe(sim.Scene, "the current zone") + ".", 20);
                MarkDirtyUnsafe(memory);
            }
        }

        internal void RecordZone(SimSnapshot sim, string scene)
        {
            lock (_ioLock)
            {
                SimMemory memory = GetOrCreateUnsafe(sim);
                if (!string.Equals(memory.LastKnownScene, scene, StringComparison.OrdinalIgnoreCase))
                {
                    AddEventUnsafe(memory, "zone", "Traveled with the player to " + scene + ".", 30);
                    memory.LastKnownScene = scene;
                    MarkDirtyUnsafe(memory);
                }
            }
        }

        internal void RecordLevelIfChanged(SimSnapshot sim)
        {
            lock (_ioLock)
            {
                SimMemory memory = GetOrCreateUnsafe(sim);
                // This runs for every slot on every party poll, so only touch storage on a real change.
                if (memory.LastKnownLevel == sim.Level) return;
                if (memory.LastKnownLevel > 0 && sim.Level > memory.LastKnownLevel)
                {
                    string text = sim.Name + " reached level " + sim.Level + " while adventuring with the player.";
                    AddEventUnsafe(memory, "level_up", text, 75);
                    AddImportantUnsafe(memory, text);
                }
                memory.LastKnownLevel = sim.Level;
                MarkDirtyUnsafe(memory);
            }
        }

        internal void RecordObservedEvent(SimSnapshot sim, string type, string text, int importance, bool importantMemory)
        {
            if (sim == null || string.IsNullOrWhiteSpace(text)) return;
            lock (_ioLock)
            {
                SimMemory memory = GetOrCreateUnsafe(sim);
                AddEventUnsafe(memory, string.IsNullOrWhiteSpace(type) ? "event" : type, text, importance);
                if (importantMemory) AddImportantUnsafe(memory, text);
                if (string.Equals(type, "friendly_duel", StringComparison.OrdinalIgnoreCase) &&
                    ContainsWholeName(text, sim.Name))
                {
                    memory.VerifiedPracticeDuels = Math.Min(100000, memory.VerifiedPracticeDuels + 1);
                    RefreshPlayerRelationshipUnsafe(memory, sim, "verified practice duel");
                }
                MarkDirtyUnsafe(memory);
            }
        }


        internal void RecordOutingSummary(SimSnapshot sim, string summary, int minutes)
        {
            RecordOutingParticipation(sim, summary, minutes, true);
        }

        internal void RecordOutingParticipation(SimSnapshot sim, string summary, int minutes, bool includeVerifiedSummary)
        {
            if (sim == null || minutes < 2) return;
            lock (_ioLock)
            {
                SimMemory memory = GetOrCreateUnsafe(sim);
                if (includeVerifiedSummary && !string.IsNullOrWhiteSpace(summary))
                {
                    memory.CompletedOutings = Math.Min(100000, memory.CompletedOutings + 1);
                    memory.OutingSummaries.Add(summary.Trim());
                    if (memory.OutingSummaries.Count > 8)
                        memory.OutingSummaries.RemoveRange(0, memory.OutingSummaries.Count - 8);
                    AddEventUnsafe(memory, "outing_summary", summary.Trim(), 65);
                    if (minutes >= 30) AddImportantUnsafe(memory, summary.Trim());
                }
                memory.LastOutingUtc = UtcNow();
                memory.TotalGroupedMinutes += minutes;
                RefreshPlayerRelationshipUnsafe(memory, sim, "completed outing");
                MarkDirtyUnsafe(memory);
            }
        }


        internal void RecordSharedOuting(IList<SimSnapshot> participants, int minutes)
        {
            if (participants == null || participants.Count < 2) return;
            lock (_ioLock)
            {
                // Party refreshes can briefly contain the same Sim twice during invite/rejoin
                // transitions. Treat shared-outing counters as relationships between distinct
                // verified Sim keys, never as a count of list entries.
                List<SimSnapshot> unique = new List<SimSnapshot>();
                HashSet<string> seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < participants.Count; i++)
                {
                    SimSnapshot candidate = participants[i];
                    if (candidate == null || string.IsNullOrWhiteSpace(candidate.Key) || !seenKeys.Add(candidate.Key)) continue;
                    unique.Add(candidate);
                }
                if (unique.Count < 2) return;
                string now = UtcNow();
                for (int i = 0; i < unique.Count; i++)
                {
                    SimSnapshot sim = unique[i];
                    SimMemory memory = GetOrCreateUnsafe(sim);
                    for (int j = 0; j < unique.Count; j++)
                    {
                        if (i == j) continue;
                        SimSnapshot other = unique[j];
                        SimRelationshipMemory relation = GetRelationshipUnsafe(memory, other.Key, other.Name);
                        relation.SharedOutings += 1;
                        relation.SharedMinutes += Math.Max(1, minutes);
                        relation.LastSharedUtc = now;
                        RefreshPairRelationshipUnsafe(relation, sim, other, "completed shared outing");
                    }
                    MarkDirtyUnsafe(memory);
                }
            }
        }

        internal void RecordSharedOutingPair(SimSnapshot first, SimSnapshot second, int minutes)
        {
            if (first == null || second == null || string.IsNullOrWhiteSpace(first.Key) || string.IsNullOrWhiteSpace(second.Key) ||
                string.Equals(first.Key, second.Key, StringComparison.OrdinalIgnoreCase) || minutes < 2) return;
            lock (_ioLock)
            {
                string now = UtcNow();
                SimMemory firstMemory = GetOrCreateUnsafe(first);
                SimRelationshipMemory firstRelation = GetRelationshipUnsafe(firstMemory, second.Key, second.Name);
                firstRelation.SharedOutings = Math.Min(100000, firstRelation.SharedOutings + 1);
                firstRelation.SharedMinutes = Math.Min(100000, firstRelation.SharedMinutes + minutes);
                firstRelation.LastSharedUtc = now;
                RefreshPairRelationshipUnsafe(firstRelation, first, second, "completed shared outing");
                MarkDirtyUnsafe(firstMemory);

                SimMemory secondMemory = GetOrCreateUnsafe(second);
                SimRelationshipMemory secondRelation = GetRelationshipUnsafe(secondMemory, first.Key, first.Name);
                secondRelation.SharedOutings = Math.Min(100000, secondRelation.SharedOutings + 1);
                secondRelation.SharedMinutes = Math.Min(100000, secondRelation.SharedMinutes + minutes);
                secondRelation.LastSharedUtc = now;
                RefreshPairRelationshipUnsafe(secondRelation, second, first, "completed shared outing");
                MarkDirtyUnsafe(secondMemory);
            }
        }

        internal void RecordConversationThread(IList<SimSnapshot> party, IList<ConversationLine> thread, string zone)
        {
            if (party == null || thread == null || thread.Count < 2) return;
            lock (_ioLock)
            {
                Dictionary<string, SimSnapshot> byName = new Dictionary<string, SimSnapshot>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < party.Count; i++)
                {
                    SimSnapshot sim = party[i];
                    if (sim != null && !string.IsNullOrWhiteSpace(sim.Name)) byName[sim.Name] = sim;
                }

                List<SimSnapshot> speakers = new List<SimSnapshot>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < thread.Count; i++)
                {
                    ConversationLine line = thread[i];
                    if (line == null || string.IsNullOrWhiteSpace(line.Speaker)) continue;
                    SimSnapshot sim;
                    if (!byName.TryGetValue(line.Speaker, out sim) || sim == null) continue;
                    if (seen.Add(sim.Key)) speakers.Add(sim);
                }
                if (speakers.Count == 0) return;

                string topic = "general party chat";
                for (int i = 0; i < thread.Count; i++)
                {
                    ConversationLine line = thread[i];
                    if (line == null || string.IsNullOrWhiteSpace(line.Text)) continue;
                    string candidate = PromptBuilder.ClassifyThreadTopic(line.Text);
                    if (!string.Equals(candidate, "general party chat", StringComparison.OrdinalIgnoreCase))
                    {
                        topic = candidate;
                        break;
                    }
                }

                string place = Safe(zone, "the current area");
                string summary = "Party discussed " + topic + " in " + place + ".";
                string now = UtcNow();

                for (int i = 0; i < speakers.Count; i++)
                {
                    SimSnapshot sim = speakers[i];
                    SimMemory memory = GetOrCreateUnsafe(sim);
                    if (memory.ConversationSummaries == null) memory.ConversationSummaries = new List<string>();
                    if (memory.ConversationSummaries.Count == 0 || !string.Equals(memory.ConversationSummaries[memory.ConversationSummaries.Count - 1], summary, StringComparison.Ordinal))
                        memory.ConversationSummaries.Add(summary);
                    if (memory.ConversationSummaries.Count > 10)
                        memory.ConversationSummaries.RemoveRange(0, memory.ConversationSummaries.Count - 10);

                    for (int j = 0; j < speakers.Count; j++)
                    {
                        if (i == j) continue;
                        SimSnapshot other = speakers[j];
                        SimRelationshipMemory relation = GetRelationshipUnsafe(memory, other.Key, other.Name);
                        relation.SharedConversationThreads += 1;
                        relation.LastSharedUtc = now;
                        RefreshPairRelationshipUnsafe(relation, sim, other, "shared conversation thread");
                    }
                    MarkDirtyUnsafe(memory);
                }
            }
        }

        internal void AddConversation(SimSnapshot sim, string userMessage, string reply, int maxMessages)
        {
            lock (_ioLock)
            {
                SimMemory memory = GetOrCreateUnsafe(sim);
                memory.Conversation.Add(new ChatMessage("user", userMessage));
                memory.Conversation.Add(new ChatMessage("assistant", reply));
                int cap = Math.Max(4, maxMessages);
                if (memory.Conversation.Count > cap)
                    memory.Conversation.RemoveRange(0, memory.Conversation.Count - cap);
                memory.ConversationExchanges = Math.Min(100000, memory.ConversationExchanges + 1);
                if (RelationshipModel.IsPositiveAcknowledgement(userMessage))
                    memory.PositivePlayerExchanges = Math.Min(100000, memory.PositivePlayerExchanges + 1);
                RefreshPlayerRelationshipUnsafe(memory, sim, "observed player exchange");
                MarkDirtyUnsafe(memory);
            }
        }

        internal void RecordGroupChatContext(SimSnapshot sim, string speaker, string text)
        {
            if (sim == null || string.IsNullOrWhiteSpace(text)) return;
            lock (_ioLock)
            {
                SimMemory memory = GetOrCreateUnsafe(sim);
                string who = string.IsNullOrWhiteSpace(speaker) ? "A party member" : speaker.Trim();
                memory.RecentGroupChat.Add(who + " said in group chat: \"" + text.Trim() + "\"");
                if (memory.RecentGroupChat.Count > 12)
                    memory.RecentGroupChat.RemoveRange(0, memory.RecentGroupChat.Count - 12);
                MarkDirtyUnsafe(memory);
            }
        }

        internal List<string> Inspect(SimSnapshot live, string nameOrKey)
        {
            lock (_ioLock)
            {
                SimMemory memory = null;
                if (live != null) memory = GetOrCreateUnsafe(live);
                if (memory == null && !string.IsNullOrWhiteSpace(nameOrKey)) memory = FindMemoryUnsafe(nameOrKey);
                List<string> lines = new List<string>();
                if (memory == null) return lines;
                memory.Normalize();

                RelationshipTone playerTone = RelationshipModel.Describe(memory);
                lines.Add(memory.Name + " | player relationship | completed outings=" + memory.CompletedOutings + " | grouped~" + memory.TotalGroupedMinutes + "m");
                lines.Add("Familiarity: " + playerTone.FamiliarityLabel + " | Rapport: " + playerTone.RapportLabel + " | Rivalry: " + playerTone.RivalryLabel +
                    " | verified practice duels=" + memory.VerifiedPracticeDuels);
                if (!string.IsNullOrWhiteSpace(memory.LastKnownClass) || memory.LastKnownLevel > 0)
                    lines.Add("Last known: L" + memory.LastKnownLevel + " " + Safe(memory.LastKnownClass, "unknown class") + " in " + Safe(memory.LastKnownScene, "unknown area") + ".");
                if (memory.Preferences != null && memory.Preferences.Count > 0)
                {
                    int preferenceStart = Math.Max(0, memory.Preferences.Count - 3);
                    for (int i = preferenceStart; i < memory.Preferences.Count; i++)
                    {
                        SimPreferenceMemory preference = memory.Preferences[i];
                        if (preference != null && !string.IsNullOrWhiteSpace(preference.Statement))
                            lines.Add("Flavor preference [" + Safe(preference.TopicKey, "general") + "]: " + preference.Statement);
                    }
                }

                if (memory.OutingSummaries != null && memory.OutingSummaries.Count > 0)
                {
                    int start = Math.Max(0, memory.OutingSummaries.Count - 2);
                    for (int i = start; i < memory.OutingSummaries.Count; i++)
                        if (!string.IsNullOrWhiteSpace(memory.OutingSummaries[i])) lines.Add("Outing: " + VerifiedOutingHistoryPolicy.SanitizeForPrompt(memory.OutingSummaries[i]));
                }
                if (memory.ConversationSummaries != null && memory.ConversationSummaries.Count > 0)
                {
                    lines.Add("Social: " + memory.ConversationSummaries[memory.ConversationSummaries.Count - 1]);
                }
                if (memory.SimRelationships != null && memory.SimRelationships.Count > 0)
                {
                    int shownRelationships = 0;
                    for (int i = 0; i < memory.SimRelationships.Count && shownRelationships < 2; i++)
                    {
                        SimRelationshipMemory r = memory.SimRelationships[i];
                        if (r == null || string.IsNullOrWhiteSpace(r.OtherName)) continue;
                        RelationshipTone pairTone = RelationshipModel.Describe(r);
                        lines.Add(memory.Name + " <-> " + r.OtherName + ": familiarity " + pairTone.FamiliarityLabel + ", rapport " + pairTone.RapportLabel +
                            ", rivalry " + pairTone.RivalryLabel + ".");
                        lines.Add("Shared outings: " + r.SharedOutings + " | shared time: ~" + r.SharedMinutes + "m | conversation threads: " + r.SharedConversationThreads +
                            " | verified duels: " + r.VerifiedPracticeDuels);
                        shownRelationships++;
                    }
                }
                else if (memory.RecentEvents != null && memory.RecentEvents.Count > 0)
                {
                    int shown = 0;
                    for (int i = memory.RecentEvents.Count - 1; i >= 0 && shown < 2; i--)
                    {
                        MemoryEvent evt = memory.RecentEvents[i];
                        if (evt == null || string.IsNullOrWhiteSpace(evt.text)) continue;
                        lines.Add("Memory: " + evt.text);
                        shown++;
                    }
                }
                return lines;
            }
        }

        private SimMemory FindMemoryUnsafe(string nameOrKey)
        {
            if (string.IsNullOrWhiteSpace(nameOrKey)) return null;
            string wanted = nameOrKey.Trim();
            foreach (KeyValuePair<string, SimMemory> pair in _cache)
            {
                SimMemory cached = pair.Value;
                if (cached == null) continue;
                if (string.Equals(pair.Key, wanted, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(cached.Name, wanted, StringComparison.OrdinalIgnoreCase)) return cached;
            }

            string direct = GetPath(wanted);
            if (File.Exists(direct))
            {
                SimMemory directMemory = LoadUnsafe(wanted);
                if (directMemory != null)
                {
                    _cache[directMemory.SimKey] = directMemory;
                    return directMemory;
                }
            }

            try
            {
                string[] files = Directory.GetFiles(_directory, "*.json");
                for (int i = 0; i < files.Length; i++)
                {
                    SimMemory candidate = SimMemoryPersistence.ReadFile(files[i]);
                    if (candidate == null) continue;
                    candidate.Normalize();
                    if (string.Equals(candidate.Name, wanted, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(candidate.SimKey, wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        _cache[candidate.SimKey] = candidate;
                        return candidate;
                    }
                }
            }
            catch { }
            return null;
        }

        internal void ClearConversation(string simKey)
        {
            lock (_ioLock)
            {
                // Prefer the cached copy: reloading from disk would silently drop anything recorded
                // since the last flush.
                SimMemory memory;
                if (!_cache.TryGetValue(simKey, out memory) || memory == null) memory = LoadUnsafe(simKey);
                if (memory == null) return;
                memory.Normalize();
                memory.Conversation.Clear();
                memory.RecentGroupChat.Clear();
                MarkDirtyUnsafe(memory);
            }
        }


        private static SimRelationshipMemory GetRelationshipUnsafe(SimMemory memory, string otherKey, string otherName)
        {
            if (memory.SimRelationships == null) memory.SimRelationships = new List<SimRelationshipMemory>();
            for (int i = 0; i < memory.SimRelationships.Count; i++)
            {
                SimRelationshipMemory existing = memory.SimRelationships[i];
                if (existing == null) continue;
                if ((!string.IsNullOrWhiteSpace(otherKey) && string.Equals(existing.OtherSimKey, otherKey, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(otherName) && string.Equals(existing.OtherName, otherName, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!string.IsNullOrWhiteSpace(otherKey)) existing.OtherSimKey = otherKey;
                    if (!string.IsNullOrWhiteSpace(otherName)) existing.OtherName = otherName;
                    RelationshipModel.Normalize(existing);
                    return existing;
                }
            }
            SimRelationshipMemory relation = new SimRelationshipMemory();
            relation.OtherSimKey = otherKey ?? string.Empty;
            relation.OtherName = otherName ?? string.Empty;
            relation.LastSharedUtc = string.Empty;
            RelationshipModel.Normalize(relation);
            memory.SimRelationships.Add(relation);
            if (memory.SimRelationships.Count > 24)
                memory.SimRelationships.RemoveRange(0, memory.SimRelationships.Count - 24);
            return relation;
        }

        private SimMemory GetOrCreateUnsafe(SimSnapshot sim)
        {
            SimMemory memory;
            if (!_cache.TryGetValue(sim.Key, out memory))
            {
                memory = LoadUnsafe(sim.Key);
                if (memory == null)
                {
                    memory = new SimMemory();
                    memory.SimKey = sim.Key;
                    memory.Name = sim.Name;
                    memory.FirstSeenUtc = UtcNow();
                    memory.RecentEvents = new List<MemoryEvent>();
                    memory.ImportantMemories = new List<string>();
                    memory.Conversation = new List<ChatMessage>();
                    AddImportantUnsafe(memory, "First observed by Deep Sims around " + FriendlyDate() + ".");
                }
                memory.Normalize();
                _cache[sim.Key] = memory;
            }
            UpdateSnapshotUnsafe(memory, sim);
            if (SimulatedPlayerBackgroundPolicy.Ensure(memory, sim)) MarkDirtyUnsafe(memory);
            if (GeneratedIdentityMotivationPolicy.Ensure(memory, sim)) MarkDirtyUnsafe(memory);
            return memory;
        }

        private void UpdateSnapshotUnsafe(SimMemory memory, SimSnapshot sim)
        {
            memory.Name = sim.Name;
            memory.LastSeenUtc = UtcNow();
            memory.LastKnownClass = sim.ClassName;
            memory.LastKnownPersonality = sim.Personality;
            if (memory.LastKnownLevel <= 0) memory.LastKnownLevel = sim.Level;
            if (string.IsNullOrEmpty(memory.LastKnownScene)) memory.LastKnownScene = sim.Scene;
        }

        private void RefreshPlayerRelationshipUnsafe(SimMemory memory, SimSnapshot sim, string reason)
        {
            RelationshipTone before = RelationshipModel.Describe(memory);
            RelationshipModel.RefreshPlayer(memory, sim);
            RelationshipTone after = RelationshipModel.Describe(memory);
            LogRelationshipChange(sim == null ? memory.Name : sim.Name, "player", before, after, reason);
        }

        private void RefreshPairRelationshipUnsafe(SimRelationshipMemory relation, SimSnapshot owner, SimSnapshot other, string reason)
        {
            RelationshipTone before = RelationshipModel.Describe(relation);
            RelationshipModel.RefreshPair(relation, owner, other);
            RelationshipTone after = RelationshipModel.Describe(relation);
            LogRelationshipChange(owner == null ? "Sim" : owner.Name, other == null ? relation.OtherName : other.Name, before, after, reason);
        }

        private void LogRelationshipChange(string owner, string other, RelationshipTone before, RelationshipTone after, string reason)
        {
            if (_log == null || before == null || after == null) return;
            if (string.Equals(before.FamiliarityLabel, after.FamiliarityLabel, StringComparison.Ordinal) &&
                string.Equals(before.RapportLabel, after.RapportLabel, StringComparison.Ordinal) &&
                string.Equals(before.RivalryLabel, after.RivalryLabel, StringComparison.Ordinal)) return;
            _log.LogDebug("DeepSims relationship " + Safe(owner, "Sim") + " <-> " + Safe(other, "unknown") +
                " changed after " + Safe(reason, "observed activity") + ": familiarity=" + after.FamiliarityLabel +
                ", rapport=" + after.RapportLabel + ", rivalry=" + after.RivalryLabel + ".");
        }

        private static bool ContainsWholeName(string text, string name)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(name)) return false;
            int start = 0;
            while (start < text.Length)
            {
                int index = text.IndexOf(name, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0) return false;
                bool left = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
                int end = index + name.Length;
                bool right = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (left && right) return true;
                start = index + 1;
            }
            return false;
        }

        private void AddEventUnsafe(SimMemory memory, string type, string text, int importance)
        {
            MemoryEvent evt = new MemoryEvent();
            evt.utc = UtcNow();
            evt.type = type;
            evt.text = text;
            evt.importance = importance;
            memory.RecentEvents.Add(evt);
            if (memory.RecentEvents.Count > 30)
                memory.RecentEvents.RemoveRange(0, memory.RecentEvents.Count - 30);

            // New structured episodic representation. The legacy RecentEvents list remains intact so
            // old installs and diagnostics keep their exact behavior; this parallel record adds
            // explicit provenance/ownership/importance for bounded retrieval.
            IdentitySchema.Normalize(memory);
            StructuredMemoryRecord structured = IdentitySchema.Create("episodic", text, importance, false, false,
                memory.SimKey, memory.Name, "verified_event:" + (string.IsNullOrWhiteSpace(type) ? "event" : type));
            memory.StructuredMemories.Add(structured);
            if (memory.StructuredMemories.Count > 48)
                memory.StructuredMemories.RemoveRange(0, memory.StructuredMemories.Count - 48);
        }

        private void AddImportantUnsafe(SimMemory memory, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!memory.ImportantMemories.Contains(text)) memory.ImportantMemories.Add(text);
            if (memory.ImportantMemories.Count > 24)
                memory.ImportantMemories.RemoveRange(0, memory.ImportantMemories.Count - 24);
        }

        private SimMemory LoadUnsafe(string simKey)
        {
            string path = GetPath(simKey);
            if (!File.Exists(path)) return null;
            try
            {
                SimMemory memory = SimMemoryPersistence.ReadFile(path);
                if (memory != null)
                {
                    memory.Normalize();
                    CognitionObservability.SafeInfo(_log, "[DeepSims][MemoryPersistence] phase=reload scope=" +
                        CognitionObservability.IdentityToken(memory.SimKey) + " " +
                        MemoryCollectionObservation.Capture(memory).Describe());
                    if (MigrateLegacyDialogueUnsafe(memory)) MarkDirtyUnsafe(memory);
                }
                return memory;
            }
            catch (Exception ex)
            {
                if (_log != null) _log.LogWarning("Could not read DeepSims memory for " + simKey + ": " + ex.GetType().Name);
                return null;
            }
        }

        private bool MigrateLegacyDialogueUnsafe(SimMemory memory)
        {
            if (memory == null || memory.RecentEvents == null) return false;
            bool changed = false;
            for (int i = memory.RecentEvents.Count - 1; i >= 0; i--)
            {
                MemoryEvent evt = memory.RecentEvents[i];
                if (evt == null) continue;
                if (string.Equals(evt.type, "deep_group_chat", StringComparison.OrdinalIgnoreCase))
                {
                    // 0.4.2 stored AI-authored group lines inside the factual event stream. Some of those
                    // lines may contain invented MMO history, so discard the legacy entries on migration.
                    memory.RecentEvents.RemoveAt(i);
                    changed = true;
                }
                else if (string.Equals(evt.type, "conversation", StringComparison.OrdinalIgnoreCase))
                {
                    // Old builds stored a generic conversation marker as if it were a world event.
                    memory.RecentEvents.RemoveAt(i);
                    changed = true;
                }
            }
            if (memory.RecentGroupChat.Count > 12)
                memory.RecentGroupChat.RemoveRange(0, memory.RecentGroupChat.Count - 12);
            return changed;
        }

        private void MarkDirtyUnsafe(SimMemory memory)
        {
            if (memory == null || string.IsNullOrWhiteSpace(memory.SimKey)) return;
            _cache[memory.SimKey] = memory;
            _dirtyKeys.Add(memory.SimKey);
        }

        // Called on Unity's main thread. It only copies bounded immutable snapshots while holding
        // _ioLock, then hands them to the single writer. force=true waits briefly for a best-effort
        // synchronous flush (e.g. a "save now" command); it never stops the writer thread. Use
        // Shutdown() for actual teardown (OnDestroy), which is the only path allowed to end
        // persistence for the rest of the session.
        internal void FlushPending(bool force)
        {
            FlushPendingCore(force, false);
        }

        // Actual-teardown path only. Flushes everything best-effort and then permanently stops the
        // writer thread; failures and timeouts never affect gameplay.
        internal void Shutdown()
        {
            FlushPendingCore(true, true);
        }

        private void FlushPendingCore(bool force, bool stopWriter)
        {
            int passes = force ? 4 : 1;
            for (int pass = 0; pass < passes; pass++)
            {
                Dictionary<string, SimMemory> batch = TakeDirtySnapshots(force ? MaxPendingWrites : NormalFlushBatch, force);
                if (batch.Count == 0) break;
                List<string> rejected = QueueWriteBatch(batch);
                if (rejected.Count > 0)
                {
                    lock (_ioLock)
                        for (int i = 0; i < rejected.Count; i++) _dirtyKeys.Add(rejected[i]);
                }
                if (!force) break;
                _writerSignal.Set();
                try { _writerIdle.Wait(400); } catch { }
            }

            if (force)
            {
                _writerSignal.Set();
                try { _writerIdle.Wait(1200); } catch { }
            }

            if (stopWriter)
            {
                _writerStopping = true;
                _writerSignal.Set();
                try { _writerIdle.Wait(1200); } catch { }
                try { if (_writerThread != null && _writerThread.IsAlive) _writerThread.Join(250); } catch { }
            }
        }

        private Dictionary<string, SimMemory> TakeDirtySnapshots(int maxCount, bool force)
        {
            Dictionary<string, SimMemory> batch = new Dictionary<string, SimMemory>(StringComparer.OrdinalIgnoreCase);
            lock (_ioLock)
            {
                if (_dirtyKeys.Count == 0) return batch;
                DateTime now = DateTime.UtcNow;
                if (!force && now < _nextFlushUtc) return batch;
                _nextFlushUtc = now.AddSeconds(FlushIntervalSeconds);

                List<string> taken = new List<string>();
                foreach (string key in _dirtyKeys)
                {
                    SimMemory memory;
                    if (_cache.TryGetValue(key, out memory) && memory != null)
                    {
                        MemoryCollectionObservation live = MemoryCollectionObservation.Capture(memory);
                        SimMemory clone = CloneMemory(memory);
                        MemoryCollectionObservation cloned = MemoryCollectionObservation.Capture(clone);
                        string token = CognitionObservability.IdentityToken(memory.SimKey);
                        CognitionObservability.SafeInfo(_log, "[DeepSims][MemoryPersistence] phase=live scope=" + token + " " + live.Describe());
                        CognitionObservability.SafeInfo(_log, "[DeepSims][MemoryPersistence] phase=clone scope=" + token + " " + cloned.Describe());
                        batch[key] = clone;
                    }
                    taken.Add(key);
                    if (taken.Count >= Math.Max(1, maxCount)) break;
                }
                for (int i = 0; i < taken.Count; i++) _dirtyKeys.Remove(taken[i]);
            }
            return batch;
        }

        private List<string> QueueWriteBatch(Dictionary<string, SimMemory> batch)
        {
            List<string> rejected = new List<string>();
            lock (_writerLock)
            {
                foreach (KeyValuePair<string, SimMemory> item in batch)
                {
                    if (!_pendingWrites.ContainsKey(item.Key) && _pendingWrites.Count >= MaxPendingWrites)
                    {
                        rejected.Add(item.Key);
                        continue;
                    }
                    _pendingWrites[item.Key] = item.Value;
                }
                if (_pendingWrites.Count > 0) _writerIdle.Reset();
            }
            if (batch.Count > rejected.Count) _writerSignal.Set();
            return rejected;
        }

        private void WriterLoop()
        {
            while (true)
            {
                _writerSignal.WaitOne();
                while (true)
                {
                    Dictionary<string, SimMemory> batch;
                    lock (_writerLock)
                    {
                        if (_pendingWrites.Count == 0)
                        {
                            _writerIdle.Set();
                            break;
                        }
                        batch = new Dictionary<string, SimMemory>(_pendingWrites, StringComparer.OrdinalIgnoreCase);
                        _pendingWrites.Clear();
                    }
                    List<string> failed = null;
                    foreach (KeyValuePair<string, SimMemory> item in batch)
                    {
                        if (WriteSnapshot(item.Value)) continue;
                        if (failed == null) failed = new List<string>();
                        failed.Add(item.Key);
                    }
                    // Do not retry in this inner loop: a persistent disk failure would spin the
                    // background thread. Return failures to the bounded dirty queue so the normal
                    // flush cadence—or one of the bounded forced-flush passes—can try again.
                    if (failed != null)
                    {
                        lock (_ioLock)
                            for (int i = 0; i < failed.Count; i++) _dirtyKeys.Add(failed[i]);
                    }
                }
                if (_writerStopping)
                {
                    lock (_writerLock)
                        if (_pendingWrites.Count == 0) return;
                }
            }
        }

        private bool WriteSnapshot(SimMemory memory)
        {
            if (memory == null || string.IsNullOrWhiteSpace(memory.SimKey)) return true;
            string path = GetPath(memory.SimKey);
            string temp = path + ".tmp";
            try
            {
                if (_writeAttemptGate != null && !_writeAttemptGate(memory))
                    throw new IOException("simulated transient memory-write failure");
                string token = CognitionObservability.IdentityToken(memory.SimKey);
                CognitionObservability.SafeInfo(_log, "[DeepSims][MemoryPersistence] phase=pre_json scope=" + token + " " +
                    MemoryCollectionObservation.Capture(memory).Describe());
                CognitionObservability.SafeInfo(_log, "[DeepSims][MemoryPersistence] phase=dto scope=" + token + " " +
                    SimMemoryPersistence.CaptureDto(memory).Describe());
                JsonWriteObservation serialized = SimMemoryPersistence.WriteFile(temp, memory);
                if (serialized != null && serialized.Fields != null)
                    CognitionObservability.SafeInfo(_log, "[DeepSims][MemoryPersistence] phase=json scope=" + token + " " + serialized.Fields.Describe());
                if (File.Exists(path))
                {
                    try { File.Replace(temp, path, null); }
                    catch
                    {
                        // Some Mono/filesystem combinations do not implement File.Replace. The
                        // single writer still prevents competing writes; use overwrite as fallback.
                        File.Copy(temp, path, true);
                        File.Delete(temp);
                    }
                }
                else File.Move(temp, path);
                try
                {
                    JsonFieldPresenceObservation disk = JsonFieldPresenceObservation.Inspect(File.ReadAllText(path, Encoding.UTF8));
                    CognitionObservability.SafeInfo(_log, "[DeepSims][MemoryPersistence] phase=disk scope=" + token + " " + disk.Describe());
                }
                catch (Exception readbackEx)
                {
                    CognitionObservability.SafeInfo(_log, "[DeepSims][MemoryPersistence] phase=disk scope=" + token +
                        " readback=false error=" + DiagnosticPrivacy.ExceptionType(readbackEx));
                }
                return true;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                if (_log != null) _log.LogWarning("Could not save DeepSims memory for " + memory.Name + ": " + ex.GetType().Name);
                return false;
            }
        }

        internal bool RecordExpressedPreference(SimSnapshot sim, string topicKey, string statement)
        {
            if (sim == null || !PreferenceMemoryPolicy.IsEligible(topicKey, statement)) return false;
            lock (_ioLock)
            {
                SimMemory memory = GetOrCreateUnsafe(sim);
                SimPreferenceMemory existing = null;
                for (int i = 0; i < memory.Preferences.Count; i++)
                {
                    SimPreferenceMemory item = memory.Preferences[i];
                    if (item != null && string.Equals(item.TopicKey, topicKey, StringComparison.OrdinalIgnoreCase))
                    { existing = item; break; }
                }
                if (existing == null)
                {
                    existing = new SimPreferenceMemory { TopicKey = topicKey, TimesExpressed = 0 };
                    memory.Preferences.Add(existing);
                }
                existing.Statement = statement.Trim().Length > 120 ? statement.Trim().Substring(0, 120) : statement.Trim();
                existing.TimesExpressed = Math.Min(1000, existing.TimesExpressed + 1);
                existing.UpdatedUtc = UtcNow();
                // Move the refreshed preference to the newest end while retaining a hard bound.
                memory.Preferences.Remove(existing);
                memory.Preferences.Add(existing);
                if (memory.Preferences.Count > 8) memory.Preferences.RemoveRange(0, memory.Preferences.Count - 8);
                MarkDirtyUnsafe(memory);
                return true;
            }
        }


        internal List<string> InspectIdentity(SimSnapshot live, string nameOrKey)
        {
            lock (_ioLock)
            {
                SimMemory memory = live != null ? GetOrCreateUnsafe(live) : FindMemoryUnsafe(nameOrKey);
                List<string> lines = new List<string>();
                if (memory == null) return lines;
                IdentitySchema.Normalize(memory);
                if (live == null)
                {
                    live = new SimSnapshot
                    {
                        Key = memory.SimKey, Name = memory.Name, ClassName = memory.LastKnownClass, Level = memory.LastKnownLevel,
                        Scene = memory.LastKnownScene, Personality = memory.LastKnownPersonality, AssignedRoles = new List<string>(), DialogueExamples = new List<string>()
                    };
                }
                IdentityEditorModel model = IdentityEditorModelBuilder.Build(live, CloneMemory(memory));
                lines.Add(memory.Name + " | layered identity | schema=" + memory.IdentityDataVersion);
                lines.Add("Verified context: " + model.VerifiedContext);
                lines.Add("Default profile: " + model.DefaultSummary);
                lines.Add("Personality [" + model.Personality.SourceLabel + "]: " + model.Personality.EffectiveText);
                lines.Add("Personal background [" + model.Background.SourceLabel + "]: " + model.Background.EffectiveText);
                lines.Add("Erenshor persona [" + model.Persona.SourceLabel + "]: " + model.Persona.EffectiveText);
                lines.Add("Relationship [" + model.Relationship.SourceLabel + "]: " + model.Relationship.EffectiveText);
                lines.Add("Authored history: " + model.AuthoredHistory.Count + " | shared history: " + model.SharedHistory.Count + " | pinned: " + model.Pinned.Count + " | learned structured: " + model.Learned.Count);
                return lines;
            }
        }

        internal IdentityEditorModel GetIdentityEditorModel(SimSnapshot live)
        {
            if (live == null || string.IsNullOrWhiteSpace(live.Key)) return new IdentityEditorModel();
            lock (_ioLock)
            {
                SimMemory memory = GetOrCreateUnsafe(live);
                IdentitySchema.Normalize(memory);
                return IdentityEditorModelBuilder.Build(live, CloneMemory(memory));
            }
        }

        internal AuthoredIdentityProfile GetAuthoredIdentityProfile(SimSnapshot live)
        {
            if (live == null || string.IsNullOrWhiteSpace(live.Key)) return new AuthoredIdentityProfile();
            lock (_ioLock)
            {
                SimMemory memory = GetOrCreateUnsafe(live);
                IdentitySchema.Normalize(memory);
                return IdentitySchema.CloneProfile(memory.AuthoredIdentity);
            }
        }

        internal bool TrySaveAuthoredIdentity(SimSnapshot live, AuthoredIdentityProfile draft, out string result)
        {
            result = string.Empty;
            if (live == null || string.IsNullOrWhiteSpace(live.Key)) { result = "Sim is not currently resolvable."; return false; }
            if (draft == null) { result = "Identity draft is missing."; return false; }
            string personality, background, persona, relationship, wants, cares;
            if (!TryBoundAuthoredField(draft.CorePersonality, out personality, out result) ||
                !TryBoundAuthoredField(draft.PersonalBackground, out background, out result) ||
                !TryBoundAuthoredField(draft.ErenshorPersona, out persona, out result) ||
                !TryBoundAuthoredField(draft.RelationshipToPlayer, out relationship, out result) ||
                !TryBoundAuthoredField(draft.LongTermWants, out wants, out result) ||
                !TryBoundAuthoredField(draft.CaresAbout, out cares, out result)) return false;
            lock (_ioLock)
            {
                SimMemory memory = GetOrCreateUnsafe(live);
                IdentitySchema.Normalize(memory);
                memory.AuthoredIdentity.CorePersonality = personality;
                memory.AuthoredIdentity.PersonalBackground = background;
                memory.AuthoredIdentity.ErenshorPersona = persona;
                memory.AuthoredIdentity.RelationshipToPlayer = relationship;
                memory.AuthoredIdentity.LongTermWants = wants;
                memory.AuthoredIdentity.CaresAbout = cares;
                MarkDirtyUnsafe(memory);
                result = "Saved authored identity overrides for " + memory.Name + ". Empty fields continue using deterministic defaults.";
                return true;
            }
        }

        internal bool TryResetAllAuthoredIdentity(SimSnapshot live, out string result)
        {
            result = string.Empty;
            if (live == null || string.IsNullOrWhiteSpace(live.Key)) { result = "Sim is not currently resolvable."; return false; }
            lock (_ioLock)
            {
                SimMemory memory = GetOrCreateUnsafe(live);
                IdentitySchema.Normalize(memory);
                memory.AuthoredIdentity.CorePersonality = string.Empty;
                memory.AuthoredIdentity.PersonalBackground = string.Empty;
                memory.AuthoredIdentity.ErenshorPersona = string.Empty;
                memory.AuthoredIdentity.RelationshipToPlayer = string.Empty;
                memory.AuthoredIdentity.LongTermWants = string.Empty;
                memory.AuthoredIdentity.CaresAbout = string.Empty;
                MarkDirtyUnsafe(memory);
                result = "Reset all authored identity fields for " + memory.Name + ". Authored history, pinned memory, and learned memory were preserved.";
                return true;
            }
        }

        internal bool TryUpdateAuthoredMemory(SimSnapshot live, IdentityMemorySection section, string recordId, string text, out string result)
        {
            result = string.Empty;
            if (live == null || string.IsNullOrWhiteSpace(live.Key)) { result = "Sim is not currently resolvable."; return false; }
            string clean;
            if (!TryBoundMemoryText(text, out clean, out result)) return false;
            string id = (recordId ?? string.Empty).Trim();
            if (id.Length == 0) { result = "Memory id is missing."; return false; }
            lock (_ioLock)
            {
                SimMemory owner = GetOrCreateUnsafe(live);
                IdentitySchema.Normalize(owner);
                if (section == IdentityMemorySection.PinnedAuthored)
                {
                    StructuredMemoryRecord record = FindRecord(owner.AuthoredIdentity.PinnedMemories, id);
                    if (record == null || !record.Authored) { result = "Authored pinned memory was not found."; return false; }
                    RewriteRecord(record, clean);
                    MarkDirtyUnsafe(owner);
                    result = "Updated authored pinned memory.";
                    return true;
                }
                if (section == IdentityMemorySection.AuthoredHistory)
                {
                    StructuredMemoryRecord record = FindRecord(owner.AuthoredIdentity.SharedHistory, id);
                    if (record == null || !record.Authored || IdentityEditorModelBuilder.IsSharedHistoryRecord(record))
                    { result = "Authored history was not found."; return false; }
                    RewriteRecord(record, clean);
                    MarkDirtyUnsafe(owner);
                    result = "Updated authored history.";
                    return true;
                }
                if (section == IdentityMemorySection.SharedHistory)
                {
                    StructuredMemoryRecord selected = FindRecord(owner.AuthoredIdentity.SharedHistory, id);
                    if (selected == null || !selected.Authored || !IdentityEditorModelBuilder.IsSharedHistoryRecord(selected)) { result = "Authored shared history was not found."; return false; }
                    int changed = 0;
                    List<SimMemory> memories = LoadAllMemoriesUnsafe();
                    for (int i = 0; i < memories.Count; i++)
                    {
                        StructuredMemoryRecord record = FindRecord(memories[i].AuthoredIdentity.SharedHistory, id);
                        if (record == null || !record.Authored) continue;
                        RewriteRecord(record, clean);
                        MarkDirtyUnsafe(memories[i]);
                        changed++;
                    }
                    result = "Updated authored history" + (changed > 1 ? " for all " + changed + " KnownBy copies." : ".");
                    return changed > 0;
                }
                result = "Learned memory cannot be edited in place. Copy it to an authored pinned memory instead.";
                return false;
            }
        }

        internal bool TryRemoveAuthoredMemory(SimSnapshot live, IdentityMemorySection section, string recordId, out string result)
        {
            result = string.Empty;
            if (live == null || string.IsNullOrWhiteSpace(live.Key)) { result = "Sim is not currently resolvable."; return false; }
            string id = (recordId ?? string.Empty).Trim();
            if (id.Length == 0) { result = "Memory id is missing."; return false; }
            lock (_ioLock)
            {
                SimMemory owner = GetOrCreateUnsafe(live);
                IdentitySchema.Normalize(owner);
                if (section == IdentityMemorySection.PinnedAuthored)
                {
                    bool removed = RemoveRecord(owner.AuthoredIdentity.PinnedMemories, id, true);
                    if (!removed) { result = "Authored pinned memory was not found."; return false; }
                    MarkDirtyUnsafe(owner);
                    result = "Removed authored pinned memory. Learned memory was not changed.";
                    return true;
                }
                if (section == IdentityMemorySection.AuthoredHistory)
                {
                    StructuredMemoryRecord record = FindRecord(owner.AuthoredIdentity.SharedHistory, id);
                    if (record == null || !record.Authored || IdentityEditorModelBuilder.IsSharedHistoryRecord(record))
                    { result = "Authored history was not found."; return false; }
                    if (!RemoveRecord(owner.AuthoredIdentity.SharedHistory, id, true)) { result = "Authored history was not found."; return false; }
                    MarkDirtyUnsafe(owner);
                    result = "Removed authored history. Learned memory was not changed.";
                    return true;
                }
                if (section == IdentityMemorySection.SharedHistory)
                {
                    StructuredMemoryRecord selected = FindRecord(owner.AuthoredIdentity.SharedHistory, id);
                    if (selected == null || !selected.Authored || !IdentityEditorModelBuilder.IsSharedHistoryRecord(selected)) { result = "Authored shared history was not found."; return false; }
                    int removed = 0;
                    List<SimMemory> memories = LoadAllMemoriesUnsafe();
                    for (int i = 0; i < memories.Count; i++)
                    {
                        if (!RemoveRecord(memories[i].AuthoredIdentity.SharedHistory, id, true)) continue;
                        MarkDirtyUnsafe(memories[i]);
                        removed++;
                    }
                    result = "Removed authored history" + (removed > 1 ? " from all " + removed + " KnownBy copies." : ".");
                    return removed > 0;
                }
                result = "Learned memory is removed with Forget, not authored-memory Remove.";
                return false;
            }
        }

        internal bool TryForgetLearnedMemory(SimSnapshot live, string recordId, out string result)
        {
            result = string.Empty;
            if (live == null || string.IsNullOrWhiteSpace(live.Key)) { result = "Sim is not currently resolvable."; return false; }
            string id = (recordId ?? string.Empty).Trim();
            lock (_ioLock)
            {
                SimMemory memory = GetOrCreateUnsafe(live);
                IdentitySchema.Normalize(memory);
                for (int i = memory.StructuredMemories.Count - 1; i >= 0; i--)
                {
                    StructuredMemoryRecord record = memory.StructuredMemories[i];
                    if (record == null || !string.Equals(record.Id, id, StringComparison.OrdinalIgnoreCase)) continue;
                    if (record.Authored) { result = "Authored memory cannot be forgotten through the learned-memory path."; return false; }
                    memory.StructuredMemories.RemoveAt(i);
                    MarkDirtyUnsafe(memory);
                    result = "Forgot learned structured memory. Authored identity/history were preserved.";
                    return true;
                }
                result = "Learned structured memory was not found.";
                return false;
            }
        }

        internal bool TryCopyLearnedToPinned(SimSnapshot live, string recordId, out string result)
        {
            result = string.Empty;
            if (live == null || string.IsNullOrWhiteSpace(live.Key)) { result = "Sim is not currently resolvable."; return false; }
            string id = (recordId ?? string.Empty).Trim();
            lock (_ioLock)
            {
                SimMemory memory = GetOrCreateUnsafe(live);
                IdentitySchema.Normalize(memory);
                StructuredMemoryRecord learned = FindRecord(memory.StructuredMemories, id);
                if (learned == null || learned.Authored) { result = "Learned structured memory was not found."; return false; }
                if (memory.AuthoredIdentity.PinnedMemories.Count >= 128) { result = "Pinned-memory limit reached (128)."; return false; }
                StructuredMemoryRecord copy = IdentitySchema.Create("authored_pinned", learned.Text, 100, true, true, memory.SimKey, memory.Name, "player_authored_from_learned");
                memory.AuthoredIdentity.PinnedMemories.Add(copy);
                memory.AuthoredIdentity.Normalize();
                MarkDirtyUnsafe(memory);
                result = "Copied learned memory into an authored pinned memory. The learned source remains unchanged.";
                return true;
            }
        }

        internal bool TrySetAuthoredField(SimSnapshot live, string field, string value, out string result)
        {
            result = string.Empty;
            if (live == null || string.IsNullOrWhiteSpace(live.Key)) { result = "Sim is not currently resolvable."; return false; }
            string rawValue = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (rawValue.Length > 900) { result = "Authored text is too long (maximum 900 characters); nothing was changed."; return false; }
            string clean = IdentitySchema.Bound(rawValue, 900);
            if (string.IsNullOrWhiteSpace(clean)) { result = "Authored text cannot be empty; use clear instead."; return false; }
            lock (_ioLock)
            {
                SimMemory memory = GetOrCreateUnsafe(live);
                IdentitySchema.Normalize(memory);
                string f = (field ?? string.Empty).Trim().ToLowerInvariant();
                if (f == "personality" || f == "core") memory.AuthoredIdentity.CorePersonality = clean;
                else if (f == "background" || f == "personal") memory.AuthoredIdentity.PersonalBackground = clean;
                else if (f == "persona" || f == "erenshor") memory.AuthoredIdentity.ErenshorPersona = clean;
                else if (f == "relationship" || f == "player") memory.AuthoredIdentity.RelationshipToPlayer = clean;
                else if (f == "wants" || f == "concerns" || f == "goals") memory.AuthoredIdentity.LongTermWants = clean;
                else if (f == "cares" || f == "interests") memory.AuthoredIdentity.CaresAbout = clean;
                else { result = "Unknown field. Use personality, background, persona, relationship, wants, or cares."; return false; }
                MarkDirtyUnsafe(memory);
                result = "Updated authored " + f + " for " + memory.Name + ".";
                return true;
            }
        }

        internal bool TryAddAuthoredMemory(SimSnapshot live, string kind, string text, out string result)
        {
            result = string.Empty;
            if (live == null || string.IsNullOrWhiteSpace(live.Key)) { result = "Sim is not currently resolvable."; return false; }
            string rawText = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (rawText.Length > 700) { result = "Memory text is too long (maximum 700 characters); nothing was changed."; return false; }
            string clean = IdentitySchema.Bound(rawText, 700);
            if (string.IsNullOrWhiteSpace(clean)) { result = "Memory text cannot be empty."; return false; }
            lock (_ioLock)
            {
                SimMemory memory = GetOrCreateUnsafe(live);
                IdentitySchema.Normalize(memory);
                string k = (kind ?? string.Empty).Trim().ToLowerInvariant();
                bool pinned = k == "pinned" || k == "pin";
                bool history = k == "history" || k == "shared";
                if (!pinned && !history) { result = "Unknown memory kind. Use history or pinned."; return false; }
                List<StructuredMemoryRecord> authoredTarget = history ? memory.AuthoredIdentity.SharedHistory : memory.AuthoredIdentity.PinnedMemories;
                if (authoredTarget.Count >= 128) { result = "Authored " + (history ? "history" : "pinned memory") + " limit reached (128). Clear entries before adding more; nothing was deleted."; return false; }
                StructuredMemoryRecord record = IdentitySchema.Create(history ? "authored_history" : "authored_pinned", clean,
                    pinned ? 100 : 90, true, pinned, memory.SimKey, memory.Name, "player_authored");
                authoredTarget.Add(record);
                memory.AuthoredIdentity.Normalize();
                MarkDirtyUnsafe(memory);
                result = "Added authoritative " + (history ? "history" : "pinned memory") + " for " + memory.Name + ".";
                return true;
            }
        }

        internal bool TryAddSharedHistory(IList<SimSnapshot> knowers, string text, out string result)
        {
            result = string.Empty;
            if (knowers == null || knowers.Count < 2) { result = "At least two Sims are required for shared history."; return false; }
            string rawText = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (rawText.Length > 700) { result = "Shared history text is too long (maximum 700 characters); nothing was changed."; return false; }
            string clean = IdentitySchema.Bound(rawText, 700);
            if (string.IsNullOrWhiteSpace(clean)) { result = "Shared history text cannot be empty."; return false; }
            lock (_ioLock)
            {
                List<SimMemory> memories = new List<SimMemory>();
                List<string> knownBy = new List<string>();
                List<string> participants = new List<string>();
                knownBy.Add("player"); participants.Add("player");
                for (int i = 0; i < knowers.Count; i++)
                {
                    SimSnapshot sim = knowers[i];
                    if (sim == null || string.IsNullOrWhiteSpace(sim.Key)) continue;
                    SimMemory memory = GetOrCreateUnsafe(sim);
                    if (!memories.Contains(memory)) memories.Add(memory);
                    AddUnique(knownBy, sim.Key); AddUnique(knownBy, sim.Name);
                    AddUnique(participants, sim.Key); AddUnique(participants, sim.Name);
                }
                if (memories.Count < 2) { result = "Both Sims must be currently resolvable."; return false; }
                for (int i = 0; i < memories.Count; i++)
                {
                    IdentitySchema.Normalize(memories[i]);
                    if (memories[i].AuthoredIdentity.SharedHistory.Count >= 128)
                    {
                        result = "Shared-history limit reached for " + memories[i].Name + " (128). Nothing was changed or deleted.";
                        return false;
                    }
                }
                string sharedId = "shared-" + DateTime.UtcNow.Ticks.ToString("x");
                for (int i = 0; i < memories.Count; i++)
                {
                    SimMemory memory = memories[i];
                    IdentitySchema.Normalize(memory);
                    StructuredMemoryRecord record = IdentitySchema.Create("authored_shared_history", clean, 95, true, false,
                        memory.SimKey, memory.Name, "player_authored_shared");
                    record.Id = sharedId;
                    record.KnownBy = new List<string>(knownBy);
                    record.Participants = new List<string>(participants);
                    memory.AuthoredIdentity.SharedHistory.Add(record);
                    memory.AuthoredIdentity.Normalize();
                    MarkDirtyUnsafe(memory);
                }
                result = "Added authoritative shared history known by " + memories.Count + " Sims and the player.";
                return true;
            }
        }

        internal bool TryClearAuthored(SimSnapshot live, string field, out string result)
        {
            result = string.Empty;
            if (live == null || string.IsNullOrWhiteSpace(live.Key)) { result = "Sim is not currently resolvable."; return false; }
            lock (_ioLock)
            {
                SimMemory memory = GetOrCreateUnsafe(live);
                IdentitySchema.Normalize(memory);
                string f = (field ?? string.Empty).Trim().ToLowerInvariant();
                if (f == "personality" || f == "core") memory.AuthoredIdentity.CorePersonality = string.Empty;
                else if (f == "background" || f == "personal") memory.AuthoredIdentity.PersonalBackground = string.Empty;
                else if (f == "persona" || f == "erenshor") memory.AuthoredIdentity.ErenshorPersona = string.Empty;
                else if (f == "relationship" || f == "player") memory.AuthoredIdentity.RelationshipToPlayer = string.Empty;
                else if (f == "history" || f == "shared")
                {
                    List<string> sharedIds = new List<string>();
                    for (int i = 0; i < memory.AuthoredIdentity.SharedHistory.Count; i++)
                    {
                        StructuredMemoryRecord record = memory.AuthoredIdentity.SharedHistory[i];
                        if (IdentityEditorModelBuilder.IsSharedHistoryRecord(record) && !string.IsNullOrWhiteSpace(record.Id)) AddUnique(sharedIds, record.Id);
                    }
                    List<SimMemory> all = LoadAllMemoriesUnsafe();
                    for (int m = 0; m < all.Count; m++)
                        for (int i = 0; i < sharedIds.Count; i++)
                            if (RemoveRecord(all[m].AuthoredIdentity.SharedHistory, sharedIds[i], true)) MarkDirtyUnsafe(all[m]);
                    memory.AuthoredIdentity.SharedHistory.Clear();
                }
                else if (f == "pinned" || f == "pin") memory.AuthoredIdentity.PinnedMemories.Clear();
                else if (f == "all" || f == "identity")
                {
                    memory.AuthoredIdentity.CorePersonality = string.Empty;
                    memory.AuthoredIdentity.PersonalBackground = string.Empty;
                    memory.AuthoredIdentity.ErenshorPersona = string.Empty;
                    memory.AuthoredIdentity.RelationshipToPlayer = string.Empty;
                }
                else { result = "Unknown field. Use personality, background, persona, relationship, history, pinned, or all."; return false; }
                MarkDirtyUnsafe(memory);
                result = "Cleared authored " + f + " for " + memory.Name + ". Learned memories were not deleted.";
                return true;
            }
        }

        private static bool TryBoundAuthoredField(string value, out string clean, out string result)
        {
            result = string.Empty;
            string raw = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (raw.Length > 900) { clean = string.Empty; result = "Authored identity fields are limited to 900 characters."; return false; }
            clean = IdentitySchema.Bound(raw, 900);
            return true;
        }

        private static bool TryBoundMemoryText(string value, out string clean, out string result)
        {
            result = string.Empty;
            string raw = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (raw.Length == 0) { clean = string.Empty; result = "Memory text cannot be empty."; return false; }
            if (raw.Length > 700) { clean = string.Empty; result = "Memory text is too long (maximum 700 characters)."; return false; }
            clean = IdentitySchema.Bound(raw, 700);
            return true;
        }

        private static StructuredMemoryRecord FindRecord(List<StructuredMemoryRecord> records, string id)
        {
            if (records == null || string.IsNullOrWhiteSpace(id)) return null;
            for (int i = 0; i < records.Count; i++)
            {
                StructuredMemoryRecord record = records[i];
                if (record != null && string.Equals(record.Id, id, StringComparison.OrdinalIgnoreCase)) return record;
            }
            return null;
        }

        private static bool RemoveRecord(List<StructuredMemoryRecord> records, string id, bool requireAuthored)
        {
            if (records == null || string.IsNullOrWhiteSpace(id)) return false;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                StructuredMemoryRecord record = records[i];
                if (record == null || !string.Equals(record.Id, id, StringComparison.OrdinalIgnoreCase)) continue;
                if (requireAuthored && !record.Authored) return false;
                records.RemoveAt(i);
                return true;
            }
            return false;
        }

        private static void RewriteRecord(StructuredMemoryRecord record, string clean)
        {
            if (record == null) return;
            record.Text = clean;
            record.Topics = MemoryTopicPolicy.ExtractTopics(clean);
            record.EmotionalTags = MemoryTopicPolicy.ExtractEmotionalTags(clean);
            record.Normalize();
        }

        private List<SimMemory> LoadAllMemoriesUnsafe()
        {
            try
            {
                string[] files = Directory.GetFiles(_directory, "*.json");
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        SimMemory candidate = SimMemoryPersistence.ReadFile(files[i]);
                        if (candidate == null || string.IsNullOrWhiteSpace(candidate.SimKey)) continue;
                        candidate.Normalize();
                        if (!_cache.ContainsKey(candidate.SimKey)) _cache[candidate.SimKey] = candidate;
                    }
                    catch { }
                }
            }
            catch { }
            List<SimMemory> result = new List<SimMemory>();
            foreach (KeyValuePair<string, SimMemory> pair in _cache)
            {
                if (pair.Value == null) continue;
                pair.Value.Normalize();
                if (!result.Contains(pair.Value)) result.Add(pair.Value);
            }
            return result;
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (values == null || string.IsNullOrWhiteSpace(value)) return;
            for (int i = 0; i < values.Count; i++) if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase)) return;
            values.Add(value.Trim());
        }

        internal bool StoreSocialMemory(SimSnapshot owner, StructuredMemoryRecord record, out string decision)
        {
            decision = "rejected";
            if (owner == null || string.IsNullOrWhiteSpace(owner.Key) || record == null) return false;
            lock (_ioLock)
            {
                SimMemory memory;
                if (!_cache.TryGetValue(owner.Key, out memory) || memory == null) memory = GetOrCreateUnsafe(owner);
                IdentitySchema.Normalize(memory);
                record.Normalize();
                if (string.IsNullOrWhiteSpace(record.OwnerSimKey)) record.OwnerSimKey = owner.Key;
                if (string.IsNullOrWhiteSpace(record.OwnerSimName)) record.OwnerSimName = owner.Name;
                if (!MemoryKnowledgePolicy.CanUse(record, owner)) { decision = "knowledge_denied"; return false; }

                int recentLifeExpired = AgeSocialMemoriesUnsafe(memory, DateTime.UtcNow);
                if (recentLifeExpired > 0)
                    CognitionObservability.SafeInfo(_log, "[DeepSims][RecentLife] " + RecentLifeDiagnosticCounters.Describe());
                StructuredMemoryRecord existing = null;
                for (int i = memory.StructuredMemories.Count - 1; i >= 0; i--)
                {
                    StructuredMemoryRecord candidate = memory.StructuredMemories[i];
                    if (candidate == null || candidate.Authored) continue;
                    candidate.Normalize();
                    if ((!string.IsNullOrWhiteSpace(record.Id) && string.Equals(candidate.Id, record.Id, StringComparison.Ordinal)) ||
                        (!string.IsNullOrWhiteSpace(record.EpisodeId) && string.Equals(candidate.EpisodeId, record.EpisodeId, StringComparison.Ordinal) &&
                         string.Equals(candidate.MemoryType, record.MemoryType, StringComparison.OrdinalIgnoreCase)))
                    { existing = candidate; break; }
                    if (!string.IsNullOrWhiteSpace(record.CallbackConcept) && !string.IsNullOrWhiteSpace(candidate.CallbackConcept) &&
                        string.Equals(candidate.CallbackConcept, record.CallbackConcept, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(candidate.MemoryType, record.MemoryType, StringComparison.OrdinalIgnoreCase))
                    { existing = candidate; break; }
                }
                if (existing != null)
                {
                    if (string.Equals(record.SourceSystem, RecentLifePolicy.SourceSystem, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.Id, record.Id, StringComparison.Ordinal))
                    { decision = "already_stored"; return true; }
                    existing.Importance = Math.Max(existing.Importance, record.Importance);
                    existing.RecurrenceCount = Math.Max(1, existing.RecurrenceCount) + 1;
                    existing.HumorScore = Math.Max(existing.HumorScore, record.HumorScore);
                    existing.ConflictScore = Math.Max(existing.ConflictScore, record.ConflictScore);
                    if (record.InterpretationSummary.Length > 0) existing.InterpretationSummary = record.InterpretationSummary;
                    if (record.Subject.Length > 0) existing.Subject = record.Subject;
                    AddUnique(existing.EvidenceIds, record.EvidenceIds);
                    AddUnique(existing.Participants, record.Participants);
                    // Knowledge consolidation is an intersection, never a union. Repeated evidence
                    // cannot silently broaden who is allowed to recall the memory.
                    existing.KnownBy = IntersectIdentities(existing.KnownBy, record.KnownBy);
                    if (SocialMemoryPolicy.CanPromoteInsideJoke(existing))
                    {
                        existing.InsideJoke = true;
                        existing.MemoryTier = SocialMemoryPolicy.TierInsideJoke;
                    }
                    existing.Normalize();
                    MarkDirtyUnsafe(memory);
                    decision = existing.InsideJoke ? "merged_inside_joke" : "merged";
                    return true;
                }
                memory.StructuredMemories.Add(record);
                if (memory.StructuredMemories.Count > 48) memory.StructuredMemories.RemoveRange(0, memory.StructuredMemories.Count - 48);
                MarkDirtyUnsafe(memory);
                decision = "stored";
                return true;
            }
        }

        internal bool StoreSimulatedRecentLifeMemory(SimSnapshot owner, StructuredMemoryRecord record, out string decision)
        {
            decision = "rejected";
            if (owner == null || string.IsNullOrWhiteSpace(owner.Key) || record == null) return false;
            lock (_ioLock)
            {
                SimMemory memory;
                if (!_cache.TryGetValue(owner.Key, out memory) || memory == null)
                {
                    memory = LoadUnsafe(owner.Key);
                    if (memory == null)
                    {
                        memory = new SimMemory { SimKey = owner.Key, Name = owner.Name };
                        memory.Normalize();
                    }
                    _cache[owner.Key] = memory;
                }
                // Deliberately do not call GetOrCreateUnsafe/UpdateSnapshotUnsafe: an offline
                // roleplay simulation is not a native observation and cannot write LastSeenUtc.
                return StoreSocialMemory(owner, record, out decision);
            }
        }

        internal bool ApplySocialRelationshipEpisode(SimSnapshot owner, string otherKey, string otherName, SocialEpisodeRecord episode)
        {
            if (owner == null || string.IsNullOrWhiteSpace(owner.Key) || episode == null || string.IsNullOrWhiteSpace(otherName)) return false;
            lock (_ioLock)
            {
                SimMemory memory;
                if (!_cache.TryGetValue(owner.Key, out memory) || memory == null) memory = GetOrCreateUnsafe(owner);
                memory.Normalize();
                SocialRelationshipMemory relation = FindSocialRelationship(memory, otherKey, otherName, true);
                if (!SocialRelationshipPolicy.Apply(relation, episode, owner.Key, owner.Name, otherKey, otherName)) return false;
                MarkDirtyUnsafe(memory);
                return true;
            }
        }

        internal SocialRelationshipMemory GetSocialRelationship(SimSnapshot owner, string otherKey, string otherName)
        {
            if (owner == null || string.IsNullOrWhiteSpace(owner.Key)) return null;
            lock (_ioLock)
            {
                SimMemory memory;
                if (!_cache.TryGetValue(owner.Key, out memory) || memory == null) memory = LoadUnsafe(owner.Key);
                if (memory == null) return null;
                memory.Normalize();
                SocialRelationshipMemory found = FindSocialRelationship(memory, otherKey, otherName, false);
                return CloneSocialRelationship(found);
            }
        }

        internal List<StructuredMemoryRecord> GetSocialMemoryMetadata(SimSnapshot owner, int max)
        {
            List<StructuredMemoryRecord> result = new List<StructuredMemoryRecord>();
            if (owner == null || string.IsNullOrWhiteSpace(owner.Key)) return result;
            lock (_ioLock)
            {
                SimMemory memory;
                if (!_cache.TryGetValue(owner.Key, out memory) || memory == null) memory = LoadUnsafe(owner.Key);
                if (memory == null) return result;
                memory.Normalize();
                for (int i = memory.StructuredMemories.Count - 1; i >= 0 && result.Count < Math.Max(0, max); i--)
                {
                    StructuredMemoryRecord record = memory.StructuredMemories[i];
                    if (record == null || !MemoryKnowledgePolicy.CanUse(record, owner)) continue;
                    record.Normalize();
                    if (!record.Source.StartsWith("social_", StringComparison.OrdinalIgnoreCase) && !record.InsideJoke) continue;
                    result.Add(IdentitySchema.CloneRecords(new List<StructuredMemoryRecord> { record })[0]);
                }
            }
            return result;
        }

        private static SocialRelationshipMemory FindSocialRelationship(SimMemory memory, string otherKey, string otherName, bool create)
        {
            if (memory == null) return null;
            if (memory.SocialRelationships == null) memory.SocialRelationships = new List<SocialRelationshipMemory>();
            for (int i = 0; i < memory.SocialRelationships.Count; i++)
            {
                SocialRelationshipMemory relation = memory.SocialRelationships[i]; if (relation == null) continue;
                if ((!string.IsNullOrWhiteSpace(otherKey) && string.Equals(relation.OtherSimKey, otherKey, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(otherName) && string.Equals(relation.OtherName, otherName, StringComparison.OrdinalIgnoreCase))) return relation;
            }
            if (!create) return null;
            SocialRelationshipMemory created = new SocialRelationshipMemory { OtherSimKey = otherKey ?? string.Empty, OtherName = otherName ?? string.Empty, AppliedEpisodeIds = new List<string>(), UpdatedUtc = UtcNow() };
            created.Normalize(); memory.SocialRelationships.Add(created); return created;
        }

        private static SocialRelationshipMemory CloneSocialRelationship(SocialRelationshipMemory source)
        {
            if (source == null) return null; source.Normalize();
            return new SocialRelationshipMemory { OtherSimKey = source.OtherSimKey, OtherName = source.OtherName, Familiarity = source.Familiarity,
                Warmth = source.Warmth, Trust = source.Trust, Tension = source.Tension, Rivalry = source.Rivalry, MeaningfulEpisodes = source.MeaningfulEpisodes,
                UpdatedUtc = source.UpdatedUtc, AppliedEpisodeIds = new List<string>(source.AppliedEpisodeIds) };
        }

        private static int AgeSocialMemoriesUnsafe(SimMemory memory, DateTime nowUtc)
        {
            if (memory == null || memory.StructuredMemories == null) return 0;
            int recentLifeExpired = 0;
            for (int i = memory.StructuredMemories.Count - 1; i >= 0; i--)
            {
                StructuredMemoryRecord record = memory.StructuredMemories[i];
                if (record == null || record.Authored || record.Pinned) continue;
                if (SocialMemoryPolicy.ShouldFade(record, nowUtc))
                {
                    bool recentLife = string.Equals(record.Source, RecentLifePolicy.Source, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(record.SourceSystem, RecentLifePolicy.SourceSystem, StringComparison.OrdinalIgnoreCase);
                    memory.StructuredMemories.RemoveAt(i);
                    if (recentLife) { RecentLifeDiagnosticCounters.Expired(); recentLifeExpired++; }
                }
            }
            return recentLifeExpired;
        }

        private static List<string> IntersectIdentities(List<string> left, List<string> right)
        {
            List<string> result = new List<string>();
            if (left == null || right == null || left.Count == 0 || right.Count == 0) return result;
            for (int i = 0; i < left.Count; i++)
                for (int j = 0; j < right.Count; j++)
                    if (string.Equals(left[i], right[j], StringComparison.OrdinalIgnoreCase)) { AddUnique(result, left[i]); break; }
            return result;
        }

        private static void AddUnique(List<string> target, List<string> source)
        {
            if (target == null || source == null) return;
            for (int i = 0; i < source.Count; i++) AddUnique(target, source[i]);
        }

        private string GetPath(string simKey)
        {
            StringBuilder clean = new StringBuilder();
            string source = string.IsNullOrEmpty(simKey) ? "sim" : simKey;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') clean.Append(c);
            }
            if (clean.Length == 0) clean.Append("sim");
            return Path.Combine(_directory, clean.ToString() + ".json");
        }

        private static SimMemory CloneMemory(SimMemory source)
        {
            if (source == null) return new SimMemory();
            SimMemory copy = new SimMemory();
            copy.SimKey = source.SimKey;
            copy.Name = source.Name;
            copy.FirstSeenUtc = source.FirstSeenUtc;
            copy.LastSeenUtc = source.LastSeenUtc;
            copy.LastKnownScene = source.LastKnownScene;
            copy.LastKnownClass = source.LastKnownClass;
            copy.LastKnownPersonality = source.LastKnownPersonality;
            copy.LastKnownLevel = source.LastKnownLevel;
            copy.GroupSessions = source.GroupSessions;
            copy.CompletedOutings = source.CompletedOutings;
            copy.Familiarity = source.Familiarity;
            copy.Rapport = source.Rapport;
            copy.Rivalry = source.Rivalry;
            copy.ConversationExchanges = source.ConversationExchanges;
            copy.PositivePlayerExchanges = source.PositivePlayerExchanges;
            copy.CompetitivePlayerExchanges = source.CompetitivePlayerExchanges;
            copy.VerifiedPracticeDuels = source.VerifiedPracticeDuels;
            copy.RelationshipDataVersion = source.RelationshipDataVersion;
            copy.IdentityDataVersion = source.IdentityDataVersion;
            copy.GeneratedSimulatedPlayerBackground = source.GeneratedSimulatedPlayerBackground;
            copy.GeneratedSimulatedPlayerBackgroundVersion = source.GeneratedSimulatedPlayerBackgroundVersion;
            copy.GeneratedLongTermWants = source.GeneratedLongTermWants;
            copy.GeneratedCaresAbout = source.GeneratedCaresAbout;
            copy.GeneratedIdentityMotivationVersion = source.GeneratedIdentityMotivationVersion;
            copy.AuthoredIdentity = IdentitySchema.CloneProfile(source.AuthoredIdentity);
            copy.StructuredMemories = IdentitySchema.CloneRecords(source.StructuredMemories);
            copy.LastOutingUtc = source.LastOutingUtc;
            copy.TotalGroupedMinutes = source.TotalGroupedMinutes;
            copy.ImportantMemories = source.ImportantMemories == null ? new List<string>() : new List<string>(source.ImportantMemories);
            copy.RecentGroupChat = source.RecentGroupChat == null ? new List<string>() : new List<string>(source.RecentGroupChat);
            copy.OutingSummaries = source.OutingSummaries == null ? new List<string>() : new List<string>(source.OutingSummaries);
            copy.ConversationSummaries = source.ConversationSummaries == null ? new List<string>() : new List<string>(source.ConversationSummaries);
            copy.RecentEvents = new List<MemoryEvent>();
            if (source.RecentEvents != null)
                for (int i = 0; i < source.RecentEvents.Count; i++)
                {
                    MemoryEvent item = source.RecentEvents[i];
                    if (item != null) copy.RecentEvents.Add(new MemoryEvent { utc = item.utc, type = item.type, text = item.text, importance = item.importance });
                }
            copy.Conversation = new List<ChatMessage>();
            if (source.Conversation != null)
                for (int i = 0; i < source.Conversation.Count; i++)
                {
                    ChatMessage item = source.Conversation[i];
                    if (item != null) copy.Conversation.Add(new ChatMessage(item.role, item.content));
                }
            copy.SimRelationships = new List<SimRelationshipMemory>();
            if (source.SimRelationships != null)
                for (int i = 0; i < source.SimRelationships.Count; i++)
                {
                    SimRelationshipMemory item = source.SimRelationships[i];
                    if (item == null) continue;
                    copy.SimRelationships.Add(new SimRelationshipMemory
                    {
                        OtherSimKey = item.OtherSimKey,
                        OtherName = item.OtherName,
                        SharedOutings = item.SharedOutings,
                        SharedMinutes = item.SharedMinutes,
                        SharedConversationThreads = item.SharedConversationThreads,
                        PositiveExchanges = item.PositiveExchanges,
                        CompetitiveExchanges = item.CompetitiveExchanges,
                        VerifiedPracticeDuels = item.VerifiedPracticeDuels,
                        Familiarity = item.Familiarity,
                        Rapport = item.Rapport,
                        Rivalry = item.Rivalry,
                        LastSharedUtc = item.LastSharedUtc
                    });
                }
            copy.SocialRelationships = new List<SocialRelationshipMemory>();
            if (source.SocialRelationships != null)
                for (int i = 0; i < source.SocialRelationships.Count; i++)
                {
                    SocialRelationshipMemory item = source.SocialRelationships[i];
                    if (item == null) continue;
                    item.Normalize();
                    copy.SocialRelationships.Add(new SocialRelationshipMemory
                    {
                        OtherSimKey = item.OtherSimKey, OtherName = item.OtherName, Familiarity = item.Familiarity, Warmth = item.Warmth,
                        Trust = item.Trust, Tension = item.Tension, Rivalry = item.Rivalry, MeaningfulEpisodes = item.MeaningfulEpisodes,
                        UpdatedUtc = item.UpdatedUtc, AppliedEpisodeIds = new List<string>(item.AppliedEpisodeIds)
                    });
                }
            copy.Preferences = new List<SimPreferenceMemory>();
            if (source.Preferences != null)
                for (int i = 0; i < source.Preferences.Count; i++)
                {
                    SimPreferenceMemory item = source.Preferences[i];
                    if (item == null) continue;
                    copy.Preferences.Add(new SimPreferenceMemory
                    {
                        TopicKey = item.TopicKey,
                        Statement = item.Statement,
                        TimesExpressed = item.TimesExpressed,
                        UpdatedUtc = item.UpdatedUtc
                    });
                }
            copy.Normalize();
            return copy;
        }

        internal static SimMemory CloneMemoryForDiagnostics(SimMemory source) { return CloneMemory(source); }

        private static string UtcNow() { return DateTime.UtcNow.ToString("o"); }
        private static string FriendlyDate() { return DateTime.Now.ToString("yyyy-MM-dd"); }
        private static string Safe(string value, string fallback) { return string.IsNullOrWhiteSpace(value) ? fallback : value; }
    }
}
