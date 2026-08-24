using System;
using System.Collections.Generic;
using System.Text;

namespace ErenshorDeepSims
{
    internal class SocialDirector
    {
        private readonly DeepSimsPlugin _plugin;
        private readonly IDeepSimsLog _log;
        private readonly EventConversationDirector _eventConversations;
        private readonly System.Random _random = new System.Random();
        private readonly Dictionary<string, DateTime> _lastEventUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _lastSimLevels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly SemanticEventDeduplicator _semanticEvents = new SemanticEventDeduplicator();
        private readonly CampmasterCompatibility _campmaster;
        private readonly AutonomousSocialScheduler _socialScheduler = new AutonomousSocialScheduler();
        private readonly DeepSimsConfigEntry<bool> _verboseLogging;
        private HashSet<string> _lastNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _lastScene = string.Empty;
        private int _lastPlayerLevel;
        private bool _initialized;
        private DateTime _lastSocialUtc = DateTime.MinValue;
        private DateTime _nextIdleEvaluationUtc = DateTime.MinValue;
        private DateTime _lastAutonomousUtc = DateTime.MinValue;
        private bool _inOrRecentCombat;
        private DateTime _lastVanillaContinuationUtc = DateTime.MinValue;
        private DateTime _sittingSinceUtc = DateTime.MinValue;
        private bool _campActive;
        private bool _manualCamp;
        private bool _campSuppressedUntilStanding;
        private bool _campmasterActive;
        private bool _relaxActive;
        private bool _softDowntimeActive;
        private DateTime _softDowntimeSinceUtc = DateTime.MinValue;
        private UnityEngine.Vector3 _softDowntimeAnchor;
        private bool _softDowntimeHasAnchor;
        private int _relaxSessionOrdinal;
        private readonly List<string> _recentRelaxTopics = new List<string>();
        private DateTime _partySettlingUntilUtc = DateTime.MinValue;
        private readonly AmbientSeedDiagnostics _seedDiagnostics = new AmbientSeedDiagnostics();
        private readonly TopicFatigueTracker _topicFatigue;
        private readonly PlayerTopicTracker _playerTopics = new PlayerTopicTracker(480.0);
        // Ephemeral "what was actually said" callback memory - distinct from the verified MemoryStore.
        // Nothing added here is ever persisted or promoted into gameplay memory (see ConversationHistory.cs).
        private readonly ConversationMomentStore _conversationMoments = new ConversationMomentStore();
        private DateTime _lastPlayerConversationUtc = DateTime.MinValue;
        // Distinguishes a real empty -> grouped transition from the first roster snapshot after the
        // plugin loads. The former may create a greeting; the latter is silent initialization.
        private bool _observedEmptyParty;
        private SocialActivityPreset _adaptivePreset = SocialActivityPreset.Normal;
        private DateTime _nextAdaptiveMoodUtc = DateTime.MinValue;
        private string _adaptiveSignature = string.Empty;
        private double _adaptiveScore;
        private string _adaptiveReason = "not evaluated";
        private readonly RecentSocialChatBuffer _recentSocialChat = new RecentSocialChatBuffer();
        private readonly UnansweredTurnTracker _unansweredTurns = new UnansweredTurnTracker();
        private readonly SilenceFatigueTracker _silenceFatigue = new SilenceFatigueTracker();
        private readonly List<AmbientSeedCandidate> _campActivitySeeds = new List<AmbientSeedCandidate>();
        private SocialActivityState _activityState = SocialActivityState.Unknown;
        private CampmasterSocialContext _campmasterSocialContext;
        private DateTime _lastObservedChatUtc = DateTime.MinValue;
        private DateTime _lastPartySpeechUtc = DateTime.MinValue;
        private DateTime _lastPlayerSpeechUtc = DateTime.MinValue;
        private DateTime _lastAutonomousSpeechUtc = DateTime.MinValue;
        private bool _contextPulsePending;
        private long _contextPulseOpportunityId;
        private string _activitySource = "unknown";
        private string _activityReason = "not_sampled";
        private string _contextConsistency = "unknown";
        private bool _campmasterStaleWarningLogged;
        private DateTime _nextSchedulerHeartbeatUtc = DateTime.MinValue;
        private DateTime _standaloneOutOfCombatSinceUtc = DateTime.MinValue;
        private double _standaloneStationarySeconds;
        private double _standaloneOutOfCombatSeconds;
        private string _standaloneScene = string.Empty;

        internal SocialDirector(DeepSimsPlugin plugin, IDeepSimsLog log)
        {
            _plugin = plugin;
            _log = log;
            _eventConversations = new EventConversationDirector(plugin);
            _campmaster = new CampmasterCompatibility(log);
            _verboseLogging = plugin == null ? null : plugin.VerboseLoggingConfig;
            _topicFatigue = new TopicFatigueTracker(
                plugin == null || plugin.SeedFatigueSecondsConfig == null ? 300.0 : plugin.SeedFatigueSecondsConfig.Value,
                plugin == null || plugin.SeedRecentTopicWindowMinutesConfig == null
                    ? 600.0 : plugin.SeedRecentTopicWindowMinutesConfig.Value * 60.0);
        }

        internal SocialContextMode ContextMode
        {
            get
            {
                if (_relaxActive) return SocialContextMode.Relax;
                if (_softDowntimeActive) return SocialContextMode.SoftDowntime;
                return _campActive ? SocialContextMode.Camp : SocialContextMode.Normal;
            }
        }

        internal string Describe()
        {
            return "director=" + OnOff(_plugin.DirectorEnabledConfig.Value) +
                ", events=" + OnOff(_plugin.EventChatterConfig.Value) +
                ", idle=" + OnOff(_plugin.IdleChatterConfig.Value) +
                ", party-chat=" + OnOff(_plugin.PartyChatResponsesConfig.Value) +
                ", sim-to-sim=" + OnOff(_plugin.SimToSimConfig.Value) +
                ", vanilla-context=" + OnOff(_plugin.VanillaChatterContinuityConfig == null || _plugin.VanillaChatterContinuityConfig.Value) +
                ", camp=" + (_campActive ? "active" : "inactive") +
                ", camp-source=" + (_campmaster.Healthy ? "Campmaster" : "legacy") +
                ", relax=" + (_relaxActive ? "active" : "inactive") +
                ", activity-state=" + _activityState +
                ", activity=" + DescribeActivityPreset() +
                ", quiet=" + DescribeQuietTime();
        }

        internal string DescribeCamp()
        {
            string source = _campmaster.Healthy ? "Campmaster" : "legacy Deep Sims";
            if (_relaxActive)
                return "Relax is active. Social downtime is explicit; Hunt Camp chatter is suppressed. Semantic source: Campmaster.";
            return (_campActive ? "Hunt Camp social context is active." : (_manualCamp ? "Legacy camp mode is waiting for combat to clear." : "Camp mode is inactive.")) +
                " Semantic source: " + source + ".";
        }

        internal string DescribeEvents() { return _eventConversations.DescribeRecent(); }

        internal string DescribeSeedStatus()
        {
            return "[DeepSims Seeds] context=" + ContextMode +
                ", " + _seedDiagnostics.DescribeStatus() +
                ", silence=" + AmbientSeedSelector.SilenceBase(ContextMode,
                    _plugin.SeedSilenceNormalConfig == null ? AmbientSeedSelector.DefaultSilenceNormal : _plugin.SeedSilenceNormalConfig.Value,
                    _plugin.SeedSilenceCampConfig == null ? AmbientSeedSelector.DefaultSilenceCamp : _plugin.SeedSilenceCampConfig.Value,
                    _plugin.SeedSilenceRelaxConfig == null ? AmbientSeedSelector.DefaultSilenceRelax : _plugin.SeedSilenceRelaxConfig.Value) +
                ", quiet=" + DescribeQuietTime() +
                "\n[DeepSims Seeds] recent topics: " + _topicFatigue.Describe(DateTime.UtcNow);
        }

        internal string DescribeSeedsRecent() { return _seedDiagnostics.DescribeRecent(6); }

        internal string DescribeSchedulerStatus()
        {
            DateTime now = DateTime.UtcNow;
            int party = _plugin == null ? 0 : _plugin.GetActiveDeepSims().Count;
            string campActivity = _campmasterSocialContext == null ? "unavailable" :
                (string.IsNullOrWhiteSpace(_campmasterSocialContext.ActivityState) ? "unknown" : _campmasterSocialContext.ActivityState);
            return "activity=" + _activityState + " source=" + _activitySource + " activityReason=" + _activityReason +
                " contextConsistency=" + _contextConsistency +
                " party=" + party + " stationary=" + Math.Round(CurrentStationarySeconds()) + "s outOfCombat=" +
                Math.Round(CurrentOutOfCombatSeconds()) + "s sincePlayerSpeech=" + Math.Round(SecondsSince(_lastPlayerSpeechUtc, now)) +
                "s sincePartySpeech=" + Math.Round(SecondsSince(_lastPartySpeechUtc, now)) + "s sinceAutonomousSpeech=" +
                Math.Round(SecondsSince(_lastAutonomousSpeechUtc, now)) + "s meaningfulGameplayAge=" +
                Math.Round(CurrentMeaningfulGameplayAge()) + "s\n[DeepSims Social] campmasterAvailable=" +
                (_campmaster != null && _campmaster.Healthy ? "yes" : "no") + " campmasterRelax=" +
                (_campmasterSocialContext != null && _campmasterSocialContext.AutoRelaxActive ? "yes" : "no") +
                " campmasterActivity=" + campActivity + " " + _socialScheduler.DescribeStatus(now);
        }

        internal string DescribeSchedulerRecent() { return _socialScheduler.DescribeRecent(DateTime.UtcNow, 12); }

        internal void OnPresetChanged() { _socialScheduler.RequestRefresh(); }

        internal void ClearTopicFatigue() { _topicFatigue.Clear(); _playerTopics.Clear(); _conversationMoments.Clear(); }

        private static List<string> NamesOf(IList<SimSnapshot> sims)
        {
            List<string> names = new List<string>();
            for (int i = 0; sims != null && i < sims.Count; i++)
                if (sims[i] != null && !string.IsNullOrWhiteSpace(sims[i].Name)) names.Add(sims[i].Name);
            return names;
        }

        // Called only once a generated or template line has actually been accepted for display. A
        // suppressed, rejected, or NO_MESSAGE opportunity must not consume the topic.
        internal AutonomousAdvanceObservation NoteAmbientTopicEmitted(DirectorEvent evt, string speaker, string emittedText)
        {
            AutonomousAdvanceObservation observation = new AutonomousAdvanceObservation();
            if (evt == null || string.IsNullOrWhiteSpace(evt.TopicKey)) return observation;
            DateTime now = DateTime.UtcNow;
            long conversationId = _plugin == null ? 0 : _plugin.CurrentConversationId();
            _topicFatigue.NoteUsed(evt.TopicKey, evt.CooldownGroup, speaker, conversationId, now);
            observation.TopicFatigueAdvanced = true;

            // What a Sim actually said becomes context for the next generation's callback pool, same
            // as a player line does below - never a verified fact, only something worth possibly
            // referencing ("you mentioned...") after real silence.
            observation.ConversationMomentAdded = _conversationMoments.Note(evt.TopicKey, speaker, emittedText, now, ConversationMomentSource.SimSaid, conversationId);

            // Safety net: if the emitted wording collapsed onto the generic waiting idea anyway, the
            // waiting subject is spent too, whatever subject was originally selected.
            string collapsed = AmbientTopics.ClassifyIdleVariant(emittedText);
            if (collapsed != null && !string.Equals(collapsed, evt.TopicKey, StringComparison.OrdinalIgnoreCase))
                _topicFatigue.NoteUsed(collapsed, AmbientTopics.Idle.CooldownGroup, speaker, conversationId, now);

            _seedDiagnostics.NoteEmitted(evt.OpportunityId, speaker);
            observation.CallbackStateAdvanced = false;
            return observation;
        }

        // A failed generation does not mean the topic became "used", but repeating the same
        // topic/speaker immediately is wasteful when the verifier has already shown that this exact
        // attempt shape is unreliable. Keep a short, in-memory negative admission signal that decays
        // on its own; a later successful visible line still uses the ordinary NoteUsed path above.
        internal void NoteAmbientTopicRejected(SocialIntent intent, string speaker, string reason)
        {
            if (intent == null || !string.Equals(intent.Source, "seed", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(intent.TopicKey)) return;
            _topicFatigue.NoteRejected(intent.TopicKey, speaker, reason, DateTime.UtcNow);
            VerboseDebug("ambient topic temporarily penalized after verifier rejection: topic=" +
                intent.TopicKey + " speaker=" + (speaker ?? "?") + " reason=" + (reason ?? "rejected"));
        }

        internal void SetManualCamp(bool enabled)
        {
            if (enabled && _relaxActive)
            {
                _manualCamp = false;
                _campActive = false;
                return;
            }
            _manualCamp = enabled;
            if (enabled)
            {
                _campSuppressedUntilStanding = false;
                _sittingSinceUtc = DateTime.UtcNow.AddSeconds(-Math.Max(0.0, _plugin.CampEnterSecondsConfig.Value));
            }
            else
            {
                _campActive = false;
                _campSuppressedUntilStanding = true;
                _sittingSinceUtc = DateTime.MinValue;
            }
        }

        internal void Observe(WorldSnapshot world, IList<SimSnapshot> active)
        {
            if (world == null || active == null) return;
            DateTime now = DateTime.UtcNow;
            bool nowCombat = world.Outing != null && !string.IsNullOrWhiteSpace(world.Outing.Activity) && world.Outing.Activity.IndexOf("combat", StringComparison.OrdinalIgnoreCase) >= 0;
            _inOrRecentCombat = nowCombat;
            if (nowCombat)
            {
                _silenceFatigue.Reset();
                if (_plugin != null) _plugin.CloseSocialThread("combat");
            }
            ObserveCampmaster(now, nowCombat);
            UpdateCampState(now, nowCombat);
            UpdateActivityContext(now, active, nowCombat);
            UpdateAdaptiveActivity(now, world, active);
            string authorityReason = "plugin_unavailable";
            bool authority = _plugin != null && _plugin.CanOwnAutonomousSocial(out authorityReason);
            _socialScheduler.Observe(now, _plugin != null && _plugin.CharacterScopeReady,
                active.Count, authority, authorityReason, _activityState, _activitySource,
                CurrentSocialPreset(), world.Scene, _plugin.HasActiveSocialThread(now), _random.NextDouble());
            LogSchedulerHeartbeat(now, active.Count, authority, authorityReason);
            // SessionTelemetry emits the completed encounter only after both combat signals have
            // stayed quiet. Do not infer a conversation candidate from this coarse activity flip.

            if (active.Count == 0)
            {
                _silenceFatigue.Reset();
                _observedEmptyParty = true;
                _initialized = false;
                _lastNames.Clear();
                _lastSimLevels.Clear();
                _lastScene = world.Scene ?? string.Empty;
                _lastPlayerLevel = world.Player == null ? 0 : world.Player.Level;
                _lastSocialUtc = now;
                _nextIdleEvaluationUtc = now.AddSeconds(NormalIdleMinimum());
                return;
            }

            if (!_initialized)
            {
                Prime(world, active, now);
                return;
            }

            string scene = world.Scene ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(scene) && !string.Equals(scene, _lastScene, StringComparison.OrdinalIgnoreCase))
            {
                _lastScene = scene;
                _silenceFatigue.Reset();
                NoteSocialActivity(now);
            }

            int playerLevel = world.Player == null ? 0 : world.Player.Level;
            if (_lastPlayerLevel > 0 && playerLevel > _lastPlayerLevel && !IsDuplicate("player_level_up", 5.0))
            {
                string eventText = "The player just reached level " + playerLevel + ".";
                _plugin.RecordSharedEvent("player_level_up", eventText, 80, true);
                SubmitVerifiedCandidate("player_level_up", eventText, 80, 0.90, active, null, new string[] { playerLevel.ToString() }, SocialEventTrust.ObservedNow);
                NoteSocialActivity(now);
            }
            if (playerLevel > 0) _lastPlayerLevel = playerLevel;

            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < active.Count; i++)
            {
                SimSnapshot sim = active[i];
                if (sim == null || string.IsNullOrWhiteSpace(sim.Name)) continue;
                names.Add(sim.Name);

                int previous;
                if (_lastSimLevels.TryGetValue(sim.Name, out previous) && previous > 0 && sim.Level > previous)
                {
                    string eventText = sim.Name + " just reached level " + sim.Level + ".";
                    SubmitVerifiedCandidate("sim_level_up", eventText, 75, 0.85, active, sim.Name, new string[] { sim.Name }, SocialEventTrust.ObservedNow);
                    NoteSocialActivity(now);
                }
                _lastSimLevels[sim.Name] = sim.Level;
            }
            if (!_lastNames.SetEquals(names))
            {
                _silenceFatigue.Reset();
                // Erenshor does a noticeable amount of work while Sim party members spawn and
                // initialize. Do not pile autonomous chat/profile work onto that same burst.
                _partySettlingUntilUtc = now.AddSeconds(6.0);
                foreach (string joined in names)
                    if (!_lastNames.Contains(joined))
                    {
                        SimSnapshot joinedSim = FindByName(active, joined);
                        SubmitPartyArrival(joinedSim, active, now);
                    }
                foreach (string left in _lastNames)
                    if (!names.Contains(left))
                        SubmitVerifiedCandidate("party_leave", left + " just left the current party.", 45, 0.40, active, left, new string[] { left }, SocialEventTrust.ObservedNow);
                NoteSocialActivity(now);
            }
            _lastNames = names;

            _eventConversations.Tick(world, active, _inOrRecentCombat, _partySettlingUntilUtc);
            EvaluateIdlePressure(now, world, active);
        }

        internal void NotifyGameEvent(string type, string description, int importance, bool importantMemory, double baseChance)
        {
            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(description)) return;
            DateTime now = DateTime.UtcNow;

            if (ExpeditionSocialPolicy.IsExpeditionType(type))
            {
                if (!_semanticEvents.ShouldAccept(type, description, now))
                {
                    VerboseDebug("Expedition event suppressed: type=" + type + ", reason=recent semantic duplication");
                    return;
                }

                // Structural lifecycle noise is intentionally not promoted into persistent social
                // memory. Arrival and combat interruption are the two lifecycle facts that currently
                // carry enough semantic value to retain and potentially react to.
                if (!ExpeditionSocialPolicy.ShouldPersistSocialMemory(type))
                {
                    _eventConversations.RejectObservedType(type, "structural Expedition lifecycle event; silence is intentional");
                    return;
                }

                importance = ExpeditionSocialPolicy.NormalizeImportance(type, importance);
                baseChance = ExpeditionSocialPolicy.NormalizeChance(type, baseChance);
                _plugin.RecordSharedEvent(type, description, importance, importantMemory);
                List<SimSnapshot> expeditionActive = _plugin.GetActiveDeepSims();
                if (ExpeditionSocialPolicy.ShouldCreateCandidate(type))
                    SubmitVerifiedCandidate(type, description, importance, baseChance, expeditionActive, null, null, SocialEventTrust.ObservedNow);
                else _eventConversations.RejectObservedType(type, "not promoted: insufficient Expedition social significance");
                NoteSocialActivity(now);
                ResetStandaloneDowntime(now);
                return;
            }

            if (IsDuplicate(type, 2.5)) return;

            _plugin.RecordSharedEvent(type, description, importance, importantMemory);
            List<SimSnapshot> active = _plugin.GetActiveDeepSims();
            if (IsSupportedObservedEvent(type))
                SubmitVerifiedCandidate(type, description, importance, baseChance, active, ExtractLeadingSimName(type, description, active), null, SocialEventTrust.ObservedNow);
            else _eventConversations.RejectObservedType(type, "not promoted: insufficient event-conversation significance");
            NoteSocialActivity(now);
            ResetStandaloneDowntime(now);
        }

        internal void NoteCampActivitySeed(CampEventFact evt)
        {
            if (evt == null) return;
            string type = (evt.Type ?? string.Empty).Trim().ToLowerInvariant();
            if (type != "camp_activity_completed" && type != "camp_watch_event" && type != "camp_preparation_changed") return;
            string fact = CampLivingEventSemantics.FactualSummary(evt);
            if (string.IsNullOrWhiteSpace(fact)) return;
            string[] eligible = string.IsNullOrWhiteSpace(evt.ParticipantName) ? null : new string[] { evt.ParticipantName };
            DateTime now = DateTime.UtcNow;
            double score = type == "camp_watch_event" ? 44.0 : type == "camp_activity_completed" ? 40.0 : 37.0;
            _campActivitySeeds.Add(new AmbientSeedCandidate(type, "camp_activity",
                "react briefly to this exact Campmaster activity, adding no cause, outcome, attacker, buff, or native gameplay effect",
                score, fact, "Campmaster living event contract v1", 50, 0.0, now, now.AddSeconds(90.0), eligible));
            while (_campActivitySeeds.Count > 8) _campActivitySeeds.RemoveAt(0);
        }

        internal void NotifyCompletedEncounter(EncounterSnapshot encounter, IList<string> participants, int primaryEnemyKills)
        {
            if (encounter == null || string.IsNullOrWhiteSpace(encounter.Summary)) return;
            if (!EventConversationDirector.ShouldPromoteCompletedEncounter(encounter))
            {
                _eventConversations.RejectObservedType("encounter_complete", "not promoted: no recorded kill, death, or close call");
                return;
            }
            List<SimSnapshot> active = _plugin.GetActiveDeepSims();
            List<string> eligible = new List<string>();
            for (int i = 0; i < active.Count; i++)
                if (active[i] != null && EventConversationDirector.Contains(participants, active[i].Name)) eligible.Add(active[i].Name);
            if (eligible.Count == 0) return;
            int importance = encounter.Deaths > 0 ? 90 : (encounter.CloseCalls > 0 ? 80 : (primaryEnemyKills >= 3 ? 70 : 45));
            double chance = encounter.Deaths > 0 || encounter.CloseCalls > 0 ? 0.90 : (primaryEnemyKills >= 3 ? 0.72 : 0.30);
            string verifiedContext = encounter.Summary;
            if (primaryEnemyKills >= 3 && !string.IsNullOrWhiteSpace(encounter.PrimaryEnemy))
                verifiedContext += " The completed encounter recorded " + primaryEnemyKills + " kills of " + encounter.PrimaryEnemy + ".";
            SocialEventCandidate candidate = new SocialEventCandidate("encounter_complete", DateTime.UtcNow, participants, eligible,
                encounter.EnemyTypes, SocialEventTrust.Experienced, importance, primaryEnemyKills >= 3 ? 1.0 : 0.75,
                "encounter", verifiedContext, chance);
            _eventConversations.Submit(candidate);
        }

        internal void NotePlayerConversation()
        {
            _silenceFatigue.Reset();
            _eventConversations.NotePlayerConversation();
            DateTime receivedUtc = DateTime.UtcNow;
            _lastPlayerConversationUtc = receivedUtc;
            _lastPlayerSpeechUtc = receivedUtc;
            _contextPulsePending = false;
            _contextPulseOpportunityId = 0;
            _socialScheduler.RequestRefresh();
            ResetStandaloneDowntime(receivedUtc);
            NoteSocialActivity(receivedUtc);
        }

        internal void NotePartyChatActivity()
        {
            _lastPartySpeechUtc = DateTime.UtcNow;
            if (_plugin != null) _plugin.NoteSocialConversationActivity();
            NoteSocialActivity(DateTime.UtcNow);
            _socialScheduler.RequestRefresh();
        }

        internal void HandleVanillaPartyLine(string speaker, string message)
        {
            if (string.IsNullOrWhiteSpace(speaker) || string.IsNullOrWhiteSpace(message)) return;
            DateTime now = DateTime.UtcNow;
            NoteSocialActivity(now);
            if (now < _partySettlingUntilUtc) return;
            if (!_plugin.DirectorEnabledConfig.Value || !_plugin.SimToSimConfig.Value) return;
            if (_plugin.VanillaChatterContinuityConfig != null && !_plugin.VanillaChatterContinuityConfig.Value) return;
            if (_plugin.PauseAutonomousInCombatConfig != null && _plugin.PauseAutonomousInCombatConfig.Value && _inOrRecentCombat) return;
            if (LooksLikeVanillaTacticalChatter(message)) return;
            _conversationMoments.Note(PromptBuilder.ClassifyThreadTopic(message), speaker, message, now,
                ConversationMomentSource.SimSaid, _plugin.CurrentConversationId());
            if ((now - _lastVanillaContinuationUtc).TotalSeconds < 22.0) return;

            double baseChance = _plugin.VanillaChatterReplyChanceConfig == null ? 0.18 : Math.Max(0.0, Math.Min(1.0, _plugin.VanillaChatterReplyChanceConfig.Value));
            double chance = baseChance;
            if (LooksLikeQuestion(message)) chance = Math.Min(0.70, baseChance * 2.6);
            else if (LooksTrivial(message) || LooksLikeGreeting(message)) chance = Math.Min(0.04, baseChance * 0.25);
            else if (message.Length >= 28) chance = Math.Min(0.45, baseChance * 1.55);
            if (_random.NextDouble() > chance) return;

            string socialReason;
            if (!_plugin.TryAdmitAutonomousOpportunity("vanilla_continuation", SocialPriority.Low,
                "vanilla|" + speaker + "|" + message, _inOrRecentCombat, out socialReason))
            {
                VerboseDebug("Vanilla continuation suppressed: " + socialReason);
                return;
            }
            _lastVanillaContinuationUtc = now;
            _plugin.QueueVanillaPartyContinuation(speaker, message);
        }

        internal void HandlePlayerPartyMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            // Keep every player-authored social turn on the same integration path. This updates
            // the event director/central SocialBudget, the local ambient deferral timestamp, and
            // the ordinary social-activity cadence exactly once.
            NotePlayerConversation();

            List<SimSnapshot> active = _plugin.GetActiveDeepSims();
            // Recording the topic is independent of whether a reply gets queued below: a later quiet
            // moment may still pick this subject back up. Scope is fixed to who was present now.
            DateTime playerNow = DateTime.UtcNow;
            _unansweredTurns.NotePlayer(message, playerNow);
            _lastPartySpeechUtc = playerNow;
            _recentSocialChat.ObserveVisible("You tell the group: " + message, playerNow);
            _lastObservedChatUtc = playerNow;
            if (_plugin.DirectorEnabledConfig.Value) _playerTopics.NotePartyMessage(message, NamesOf(active), playerNow);

            // Player input strongly refreshes the social situation: it becomes a callback candidate in
            // its own right, and a genuinely new subject invalidates older, now-stale callbacks so a
            // delayed callback can never resurrect a topic the player has already moved past.
            string playerTopicKey = PromptBuilder.ClassifyThreadTopic(message);
            _conversationMoments.Note(playerTopicKey, "Player", message, playerNow, ConversationMomentSource.PlayerSaid,
                _plugin.CurrentConversationId());
            if (!string.Equals(playerTopicKey, "general party chat", StringComparison.OrdinalIgnoreCase))
            {
                List<ConversationMoment> rejected = _conversationMoments.InvalidateConflicting(playerTopicKey, playerNow);
                for (int i = 0; i < rejected.Count; i++)
                    VerboseDebug("callback=" + rejected[i].TopicKey + " age=" + Math.Round(rejected[i].AgeSeconds(playerNow)) +
                        "s rejected=newer_active_topic");
            }

            if (!_plugin.DirectorEnabledConfig.Value || !_plugin.PartyChatResponsesConfig.Value) return;
            if (active == null || active.Count == 0) return;

            string preferred = FindAddressedSim(message, active);
            bool direct = !string.IsNullOrWhiteSpace(preferred);
            if (!direct) preferred = FindClassRelevantSim(message, active);
            bool question = LooksLikeQuestion(message);
            bool trivial = LooksTrivial(message);

            // Anything reaching this handler is a normal player-authored social turn; tactical
            // commands were already handed back to Erenshor. Player turns therefore own a visible
            // response path. Silence remains valid only for autonomous opportunities.
            bool allowFollowUp = !trivial && active.Count > 1;
            bool guaranteeResponse = true;
            _plugin.QueuePartyChatResponse(message, preferred, allowFollowUp, guaranteeResponse);
        }

        internal void ObserveVisibleChat(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            DateTime now = DateTime.UtcNow;
            _recentSocialChat.ObserveVisible(raw, now);
            _lastObservedChatUtc = now;
        }

        internal void ObserveVisibleChat(string raw, RecentChatChannel channel)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            DateTime now = DateTime.UtcNow;
            _recentSocialChat.ObserveVisible(raw, now, channel);
            _lastObservedChatUtc = now;
            if (channel == RecentChatChannel.Party) _lastPartySpeechUtc = now;
        }

        internal void NoteVisibleDeepSimLine(string speaker, string text)
        {
            _silenceFatigue.Reset();
            DateTime now = DateTime.UtcNow;
            _lastAutonomousSpeechUtc = now;
            _lastPartySpeechUtc = now;
            _recentSocialChat.ObserveVisible((speaker ?? "Sim") + " tells the group: " + (text ?? string.Empty), now);
            _lastObservedChatUtc = now;
            _unansweredTurns.NoteAcknowledgement(now);
            if (_contextPulseOpportunityId > 0)
            {
                _socialScheduler.NoteTerminal(_contextPulseOpportunityId, "visible", "emitted", true, true);
                _contextPulseOpportunityId = 0;
            }
        }

        internal void ForceTalk(string requestedSpeaker)
        {
            _plugin.QueueAutonomousReaction(BuildForcedEvent("manual_talk_test"), requestedSpeaker, false, true);
            _lastAutonomousUtc = DateTime.UtcNow;
            NoteSocialActivity(DateTime.UtcNow);
        }

        internal void ForceBanter()
        {
            // Manual banter now uses the same seed -> grounding -> shared queue -> continuation
            // architecture as normal social speech. DeepSimsPlugin arms exactly one continuation only
            // after the opener is actually visible, so this remains a bounded A -> B diagnostic thread.
            _plugin.QueueAutonomousReaction(BuildForcedEvent("manual_banter_test"), null, true, true);
            _lastAutonomousUtc = DateTime.UtcNow;
            NoteSocialActivity(DateTime.UtcNow);
        }

        // The manual test commands are an explicit request for a line, so silence is bypassed, but
        // the subject still comes from the same selector rather than from a random prompt hint.
        private DirectorEvent BuildForcedEvent(string type)
        {
            DirectorEvent evt = new DirectorEvent(type, "Quiet moment with the current visible party.", 0);
            DateTime now = DateTime.UtcNow;
            AmbientSeedDecision decision = EvaluateSeeds(now, _plugin.BuildDiagnosticWorld(),
                _plugin.GetActiveDeepSims(), 1.0, true);
            if (decision == null || decision.SilenceWon) return evt;
            _seedDiagnostics.NoteOutcome(decision.OpportunityId, "forced");
            evt.OpportunityId = decision.OpportunityId;
            evt.TopicKey = decision.SelectedTopicKey;
            evt.CooldownGroup = decision.SelectedCooldownGroup;
            evt.PromptHint = decision.SelectedPromptHint;
            evt.VerifiedFact = decision.SelectedFact;
            return evt;
        }

        private void SubmitVerifiedCandidate(string type, string description, int importance, double baseChance,
            IList<SimSnapshot> active, string excludedSpeaker, IEnumerable<string> entities, SocialEventTrust trust)
        {
            if (active == null || active.Count == 0) return;
            List<string> participants = new List<string>();
            List<string> eligible = new List<string>();
            for (int i = 0; i < active.Count; i++)
            {
                SimSnapshot sim = active[i];
                if (sim == null || string.IsNullOrWhiteSpace(sim.Name)) continue;
                participants.Add(sim.Name);
                bool namedDuelParticipant = string.Equals(type, "friendly_duel", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(description) &&
                    description.IndexOf(sim.Name, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!string.Equals(sim.Name, excludedSpeaker, StringComparison.OrdinalIgnoreCase) && !namedDuelParticipant)
                    eligible.Add(sim.Name);
            }
            if (eligible.Count == 0)
            {
                if (string.Equals(type, "friendly_duel", StringComparison.OrdinalIgnoreCase))
                {
                    _eventConversations.RejectObservedType(type, "no present non-participant spectator");
                    return;
                }
                eligible.AddRange(participants);
            }
            _eventConversations.Submit(new SocialEventCandidate(type, DateTime.UtcNow, participants, eligible, entities,
                trust, importance, 1.0, EventCategory(type), description, baseChance));
        }

        private static bool IsSupportedObservedEvent(string type)
        {
            string value = type == null ? string.Empty : type.Trim().ToLowerInvariant();
            return value == "player_level_up" || value == "sim_level_up" || value == "quest_complete" ||
                value == "player_death" || value == "sim_death" || value == "player_revive" || value == "friendly_duel" ||
                ExpeditionSocialPolicy.ShouldCreateCandidate(value);
        }

        private static string EventCategory(string type)
        {
            string value = type == null ? string.Empty : type.Trim().ToLowerInvariant();
            if (ExpeditionSocialPolicy.IsExpeditionType(value)) return "expedition";
            if (value.Contains("level")) return "milestone";
            if (value.Contains("death") || value.Contains("revive")) return "danger";
            if (value.Contains("party_")) return "party_change";
            return value;
        }

        private static string ExtractLeadingSimName(string type, string description, IList<SimSnapshot> active)
        {
            if (active == null || string.IsNullOrWhiteSpace(description)) return null;
            string value = type == null ? string.Empty : type.Trim().ToLowerInvariant();
            if (value != "sim_level_up" && value != "sim_death") return null;
            for (int i = 0; i < active.Count; i++)
                if (active[i] != null && !string.IsNullOrWhiteSpace(active[i].Name) &&
                    description.StartsWith(active[i].Name, StringComparison.OrdinalIgnoreCase)) return active[i].Name;
            return null;
        }

        private void Prime(WorldSnapshot world, IList<SimSnapshot> active, DateTime now)
        {
            _silenceFatigue.Reset();
            bool arrivedAfterObservedEmpty = _observedEmptyParty;
            _observedEmptyParty = false;
            _initialized = true;
            _lastScene = world.Scene ?? string.Empty;
            _lastPlayerLevel = world.Player == null ? 0 : world.Player.Level;
            _lastNames.Clear();
            _lastSimLevels.Clear();
            for (int i = 0; i < active.Count; i++)
            {
                SimSnapshot sim = active[i];
                if (sim == null || string.IsNullOrWhiteSpace(sim.Name)) continue;
                _lastNames.Add(sim.Name);
                _lastSimLevels[sim.Name] = sim.Level;
            }
            if (arrivedAfterObservedEmpty)
            {
                _partySettlingUntilUtc = now.AddSeconds(6.0);
                for (int i = 0; i < active.Count; i++) SubmitPartyArrival(active[i], active, now);
            }
            _lastSocialUtc = now;
            _nextIdleEvaluationUtc = _relaxActive
                ? now.AddSeconds(RelaxSocialPolicy.InitialSeconds(CurrentSocialPreset()))
                : now.AddSeconds(NormalIdleMinimum());
        }

        private void SubmitPartyArrival(SimSnapshot joined, IList<SimSnapshot> active, DateTime now)
        {
            if (joined == null || string.IsNullOrWhiteSpace(joined.Name)) return;
            SimMemory memory = _plugin.LoadMemoryForSeeding(joined);
            SocialEventCandidate reunion;
            if (ReunionPolicy.TryBuildCandidate(joined, memory, now, out reunion))
            {
                _eventConversations.Submit(reunion);
                return;
            }
            SubmitVerifiedCandidate("party_join", joined.Name + " just joined the current party.",
                55, 0.55, active, joined.Name, new string[] { joined.Name }, SocialEventTrust.ObservedNow);
        }

        private static SimSnapshot FindByName(IList<SimSnapshot> active, string name)
        {
            for (int i = 0; active != null && i < active.Count; i++)
                if (active[i] != null && string.Equals(active[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return active[i];
            return null;
        }

        private void EvaluateIdlePressure(DateTime now, WorldSnapshot world, IList<SimSnapshot> active)
        {
            if (!_plugin.DirectorEnabledConfig.Value || !_plugin.IdleChatterConfig.Value) return;
            if (ContextPulsePolicy.IsEligibleActivity(_activityState))
            {
                EvaluateContextPulsePressure(now, world, active);
                return;
            }
            if (_plugin.SeedingEnabledConfig != null && !_plugin.SeedingEnabledConfig.Value) return;
            if (now < _nextIdleEvaluationUtc) return;

            // Player conversation always has first claim on the next social moment.  This avoids
            // the visibly bad sequence "substantive party line -> silence -> random ambient line".
            if ((now - _lastPlayerConversationUtc).TotalSeconds < 20.0)
            {
                _nextIdleEvaluationUtc = _lastPlayerConversationUtc.AddSeconds(20.0);
                VerboseDebug("ambient deferred reason=fresh_player_conversation");
                return;
            }

            // A verified event candidate is already competing for this moment; do not evaluate a
            // second autonomous subject alongside it.
            if (_eventConversations.HasPendingCandidate)
            {
                _nextIdleEvaluationUtc = now.AddSeconds(6.0);
                return;
            }
            if (!CanAutonomouslySpeak(now, false))
            {
                _nextIdleEvaluationUtc = now.AddSeconds(15.0);
                return;
            }

            double min = _campActive
                ? Math.Max(10.0, _plugin.CampIdleMinSecondsConfig == null ? 25.0 : _plugin.CampIdleMinSecondsConfig.Value)
                : NormalIdleMinimum();
            double max = _campActive
                ? Math.Max(min + 1.0, _plugin.CampIdleMaxSecondsConfig == null ? 75.0 : _plugin.CampIdleMaxSecondsConfig.Value)
                : Math.Max(min + 1.0, NormalIdleMaximum());
            double quiet = (now - _lastSocialUtc).TotalSeconds;
            if (quiet < min)
            {
                _nextIdleEvaluationUtc = _lastSocialUtc.AddSeconds(min);
                return;
            }

            double pressure = Math.Max(0.0, Math.Min(1.0, (quiet - min) / (max - min)));
            AmbientSeedDecision decision = EvaluateSeeds(now, world, active, pressure, false);
            if (_log != null) _log.LogDebug("[DeepSims Cadence] utc=" + now.ToString("HH:mm:ss.fff") +
                " context=" + ContextMode + " quiet=" + Math.Round(quiet, 1) + "s window=" +
                Math.Round(min, 1) + "-" + Math.Round(max, 1) + "s decision=" +
                (decision == null ? "none" : (decision.SilenceWon ? "silence" : decision.SelectedTopicKey)));
            _nextIdleEvaluationUtc = now.AddSeconds(20.0 + (_random.NextDouble() * 15.0));
            if (decision == null || decision.SilenceWon)
            {
                // No model request at all: nothing was worth saying.
                if (decision != null) VerboseDebug("Ambient opportunity #" + decision.OpportunityId +
                    " chose silence: " + decision.Reason);
                return;
            }

            string socialReason;
            DirectorEvent ambient = BuildAmbientEvent(decision);
            if (!_plugin.TryAdmitAutonomousOpportunity(ambient.Type, SocialPriority.Low,
                "ambient|" + decision.SelectedTopicKey, _inOrRecentCombat, out socialReason))
            {
                // Budget suppression is not topic usage: the subject was never actually raised.
                _seedDiagnostics.NoteOutcome(decision.OpportunityId, "social budget suppressed: " + socialReason);
                VerboseDebug("Ambient seed suppressed: " + socialReason);
                return;
            }

            _seedDiagnostics.NoteOutcome(decision.OpportunityId, "queued");
            LogSocialOpportunityDiagnostic(decision, quiet, now);
            _plugin.QueueAutonomousReaction(ambient, decision.SelectedSpeaker, true, false);
        }

        // opportunity id, mode, elapsed silence, selected source (callback/ambient/Relax/event),
        // selected TopicKey, callback age if applicable, speaker, SocialBudget result, silence result,
        // thread id - routed through the existing debug/manual log source, never into party chat.
        private void LogSocialOpportunityDiagnostic(AmbientSeedDecision decision, double silenceSeconds, DateTime now)
        {
            if (decision == null) return;
            bool isCallback = !string.IsNullOrEmpty(decision.SelectedTopicKey) &&
                decision.SelectedTopicKey.StartsWith("callback_", StringComparison.OrdinalIgnoreCase);
            string source = isCallback ? "callback" : (ContextMode == SocialContextMode.Relax ? "relax" : (ContextMode == SocialContextMode.SoftDowntime ? "soft_downtime" : "ambient"));
            string callbackAge = string.Empty;
            if (isCallback)
            {
                string underlyingTopic = decision.SelectedTopicKey.Substring("callback_".Length);
                List<ConversationMoment> snapshot = _conversationMoments.Snapshot(now);
                for (int i = 0; i < snapshot.Count; i++)
                {
                    if (!string.Equals(snapshot[i].TopicKey, underlyingTopic, StringComparison.OrdinalIgnoreCase)) continue;
                    callbackAge = " callbackAge=" + Math.Round(snapshot[i].AgeSeconds(now)) + "s";
                    break;
                }
            }
            if (!string.IsNullOrWhiteSpace(decision.SelectedSource)) source = decision.SelectedSource;
            VerboseDebug("social opportunity #" + decision.OpportunityId + " context=" + ContextMode +
                " silence=" + Math.Round(silenceSeconds) + "s source=" + source +
                " topic=" + decision.SelectedTopicKey + callbackAge +
                " speaker=" + decision.SelectedSpeaker + " budget=accepted outcome=emitted");
        }

        private AmbientSeedDecision EvaluateSeeds(DateTime now, WorldSnapshot world,
            IList<SimSnapshot> active, double pressure, bool forceSpeech, double additionalSilenceAdjustment = 0.0)
        {
            if (active == null || active.Count == 0) return null;
            long opportunityId = _seedDiagnostics.NextOpportunityId();
            SocialContextMode mode = ContextMode;

            List<AmbientSeedCandidate> candidates;
            if (mode == SocialContextMode.Relax)
            {
                string outingFact = PickVerifiedOutingFact(world, opportunityId);
                string historyFact = PickVerifiedHistoryFact(active, opportunityId);
                candidates = AmbientSeedProducers.BuildRelaxCandidates(outingFact, historyFact, now);
            }
            else
            {
                string factSource;
                string fact = PickVerifiedSessionFact(world, opportunityId, out factSource);
                candidates = AmbientSeedProducers.BuildDowntimeCandidates(mode, fact, factSource, now);
                // TODO: a verified low-resource/role producer belongs here once Erenshor exposes an
                // authoritative current/max mana and Manage Roles assignment. AmbientSeedProducers
                // .TryBuildLowResourceSeed already accepts such a reading; nothing supplies one yet.
            }
            for (int ci = _campActivitySeeds.Count - 1; ci >= 0; ci--)
            {
                AmbientSeedCandidate campSeed = _campActivitySeeds[ci];
                if (campSeed == null || campSeed.ExpiresUtc < now) _campActivitySeeds.RemoveAt(ci);
                else if (mode == SocialContextMode.Camp || mode == SocialContextMode.Relax || mode == SocialContextMode.SoftDowntime)
                    candidates.Add(campSeed);
            }
            candidates.AddRange(_playerTopics.BuildCandidates(now));
            AmbientSeedCandidate ambientChat = AmbientSeedProducers.BuildAmbientChatCandidate(
                _recentSocialChat.Snapshot(now, 90.0), now);
            if (ambientChat != null) candidates.Add(ambientChat);

            // Priority order step 4 (callback candidate): only offered once no current player turn or
            // active Sim-to-Sim thread already owns this moment (SocialBudget's conversation-thread
            // window enforces that upstream); the excluded topic keeps a callback from competing
            // against a callback of the very same subject the thread just covered.
            ConversationMoment callback;
            if (_conversationMoments.TryPickCallback(now, null, out callback))
                candidates.Add(BuildCallbackCandidate(callback, now));

            string seedContext = BuildSeedContext(world, _playerTopics.BuildRelevanceContext(now));
            List<SimSnapshot> eligible = new List<SimSnapshot>();
            Dictionary<string, double> familiarityBySpeaker = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < active.Count; i++)
            {
                SimSnapshot sim = active[i];
                if (sim == null || string.IsNullOrWhiteSpace(sim.Name)) continue;
                if (!forceSpeech && _plugin.IsSocialSpeakerCoolingDown(sim.Name)) continue;
                eligible.Add(sim);
                familiarityBySpeaker[sim.Name] = _plugin.GetSimFamiliarity(sim);

                // Each Sim's own remembered outings/important memories are eligible only to that Sim
                // (see MemoryStore/8.3): a plain remembered string cannot prove who else witnessed it.
                SimMemory memory = _plugin.LoadMemoryForSeeding(sim);
                if (memory != null)
                {
                    candidates.AddRange(AmbientSeedProducers.BuildSharedMemoryCandidates(sim, memory, opportunityId, now, seedContext));
                    candidates.AddRange(AmbientSeedProducers.BuildIdentityCandidates(sim, memory, seedContext, SocialPerspectiveState.RoleplayActive, now));
                    AmbientSeedCandidate recentLife = AmbientSeedProducers.BuildRecentLifeCandidate(sim, memory, now);
                    if (recentLife != null) candidates.Add(recentLife);
                }
            }

            double silenceNormal = _plugin.SeedSilenceNormalConfig == null
                ? AmbientSeedSelector.DefaultSilenceNormal : _plugin.SeedSilenceNormalConfig.Value;
            double silenceCamp = _plugin.SeedSilenceCampConfig == null
                ? AmbientSeedSelector.DefaultSilenceCamp : _plugin.SeedSilenceCampConfig.Value;
            double silenceRelax = _plugin.SeedSilenceRelaxConfig == null
                ? AmbientSeedSelector.DefaultSilenceRelax : _plugin.SeedSilenceRelaxConfig.Value;
            // Quiet/Normal/Lively keeps its meaning by moving the silence threshold rather than by
            // adding a second cadence system.
            double silenceAdjust = ((1.0 - _plugin.GetSocialOpportunityMultiplier()) * 12.0) + additionalSilenceAdjustment;

            bool livelyFullPartyPreference = active.Count >= 3 && active.Count <= 5 &&
                CurrentSocialPreset() == SocialActivityPreset.Lively && !_inOrRecentCombat &&
                (_activityState == SocialActivityState.SocialDowntime ||
                 _activityState == SocialActivityState.ExtendedDowntime);
            AmbientSeedDecision decision = AmbientSeedSelector.Select(opportunityId, mode, candidates,
                eligible, _topicFatigue, _plugin.CurrentConversationId(), now,
                silenceNormal, silenceCamp, silenceRelax, pressure, silenceAdjust, forceSpeech,
                _plugin.SeedDiagnosticsConfig == null || _plugin.SeedDiagnosticsConfig.Value, familiarityBySpeaker,
                livelyFullPartyPreference ? 1.0 : 0.0);
            _seedDiagnostics.Record(decision);
            if (decision != null && !decision.SilenceWon &&
                decision.SelectedTopicKey.StartsWith("recent_life:", StringComparison.OrdinalIgnoreCase))
            {
                RecentLifeDiagnosticCounters.SelectedAsSeed();
                if (_log != null) _log.LogInfo("[DeepSims][RecentLife] " + RecentLifeDiagnosticCounters.Describe());
            }
            if (_log != null && decision != null)
            {
                _log.LogDebug("[DeepSims][SocialSeed] candidates=" + DescribeSeedCandidateSources(decision) +
                    " selected=" + SeedSourceToken(decision) + " result=" + (decision.SilenceWon ? "skipped" : "selected"));
                _log.LogInfo("[DeepSims][CognitionCorrelation] phase=seed opportunityId=" + decision.OpportunityId +
                    " seedSource=" + CognitionObservability.BoundedToken(decision.SelectedSource) +
                    " seedTopicKey=" + CognitionObservability.BoundedToken(decision.SelectedTopicKey) +
                    " candidateSelected=" + !decision.SilenceWon + " candidateSuppressed=" + decision.SilenceWon +
                    " suppressionReason=" + CognitionObservability.BoundedToken(decision.Reason));
            }
            return decision;
        }

        private static string SeedSourceToken(AmbientSeedDecision decision)
        {
            if (decision == null || decision.SilenceWon) return "none";
            return SeedSourceToken(decision.SelectedTopicKey, decision.SelectedSource);
        }

        private static string SeedSourceToken(string topicValue, string sourceValue)
        {
            string source = (sourceValue ?? string.Empty).ToLowerInvariant();
            string topic = (topicValue ?? string.Empty).ToLowerInvariant();
            if (topic.StartsWith("free_social_question")) return "free_social_question";
            if (topic.StartsWith("free_social_hypothetical")) return "free_social_hypothetical";
            if (topic.StartsWith("free_social_impulse")) return "free_social_impulse";
            if (topic.StartsWith("recent_life:") || source.IndexOf("recent life") >= 0) return "recent_life";
            if (topic.StartsWith("ambient_chat:") || source.IndexOf("ambient chat") >= 0) return "ambient";
            if (topic.StartsWith("identity:") || source.IndexOf("identity") >= 0) return "interest";
            if (topic.StartsWith("callback_")) return "callback";
            if (topic.StartsWith("camp_") || source.IndexOf("campmaster") >= 0) return "camp";
            if (source.IndexOf("news") >= 0) return "current_events";
            if (topic == "ordinary_downtime" || topic == AmbientTopics.IdleWaiting) return "generic_fallback";
            if (topic.StartsWith("memory:") || source.IndexOf("memory") >= 0) return "memory";
            if (topic.StartsWith("session:") || source.IndexOf("session") >= 0) return "verified_event";
            return "downtime_preference";
        }

        private static string DescribeSeedCandidateSources(AmbientSeedDecision decision)
        {
            if (decision == null || decision.Candidates == null || decision.Candidates.Count == 0) return "none";
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < decision.Candidates.Count; i++)
            {
                AmbientSeedScore candidate = decision.Candidates[i];
                if (candidate == null || candidate.Excluded) continue;
                string source = SeedSourceToken(candidate.TopicKey, candidate.Source);
                int count; counts.TryGetValue(source, out count); counts[source] = count + 1;
            }
            if (counts.Count == 0) return "none";
            List<string> keys = new List<string>(counts.Keys); keys.Sort(StringComparer.Ordinal);
            StringBuilder result = new StringBuilder();
            for (int i = 0; i < keys.Count && i < 8; i++)
            {
                if (result.Length > 0) result.Append(',');
                result.Append(keys[i]).Append(':').Append(counts[keys[i]]);
            }
            return result.ToString();
        }

        private static string BuildSeedContext(WorldSnapshot world, string recentPlayerTopicContext)
        {
            StringBuilder sb = new StringBuilder();
            if (world == null)
            {
                if (!string.IsNullOrWhiteSpace(recentPlayerTopicContext)) sb.Append(recentPlayerTopicContext);
                return sb.ToString().Trim();
            }
            if (!string.IsNullOrWhiteSpace(world.Scene)) sb.Append(world.Scene).Append(' ');
            if (world.Outing != null)
            {
                if (!string.IsNullOrWhiteSpace(world.Outing.CurrentZone)) sb.Append(world.Outing.CurrentZone).Append(' ');
                if (!string.IsNullOrWhiteSpace(world.Outing.Activity)) sb.Append(world.Outing.Activity).Append(' ');
                if (!string.IsNullOrWhiteSpace(world.Outing.CurrentCombatTarget)) sb.Append(world.Outing.CurrentCombatTarget).Append(' ');
                if (!string.IsNullOrWhiteSpace(world.Outing.CurrentEncounter)) sb.Append(world.Outing.CurrentEncounter).Append(' ');
                if (!string.IsNullOrWhiteSpace(world.Outing.LastEncounter)) sb.Append(world.Outing.LastEncounter).Append(' ');
                if (world.Outing.Facts != null)
                {
                    int start = Math.Max(0, world.Outing.Facts.Count - 4);
                    for (int i = start; i < world.Outing.Facts.Count; i++)
                        if (!string.IsNullOrWhiteSpace(world.Outing.Facts[i])) sb.Append(world.Outing.Facts[i]).Append(' ');
                }
            }
            if (!string.IsNullOrWhiteSpace(recentPlayerTopicContext))
                sb.Append(recentPlayerTopicContext).Append(' ');
            return sb.ToString().Trim();
        }

        private DirectorEvent BuildAmbientEvent(AmbientSeedDecision decision)
        {
            DirectorEvent ambient = _campActive
                ? new DirectorEvent("camp_idle", "Current situation: the player and visible party are stopped for a quiet camp outside combat. Use only verified outing facts for any recollection.", 20)
                : new DirectorEvent("idle", "Quiet moment with the current visible party.", 10);
            ambient.OpportunityId = decision.OpportunityId;
            ambient.TopicKey = decision.SelectedTopicKey;
            ambient.CooldownGroup = decision.SelectedCooldownGroup;
            ambient.PromptHint = decision.SelectedPromptHint;
            ambient.VerifiedFact = decision.SelectedFact;
            return ambient;
        }

        // One verified outing fact, chosen deterministically for this opportunity. The fact is
        // supporting context supplied by SessionTelemetry, never invented here.
        private static string PickVerifiedSessionFact(WorldSnapshot world, long opportunityId, out string source)
        {
            source = string.Empty;
            if (world == null || world.Outing == null || !world.Outing.Active) return string.Empty;
            if (world.Outing.Facts == null || world.Outing.Facts.Count == 0) return string.Empty;
            int count = Math.Min(6, world.Outing.Facts.Count);
            string fact = world.Outing.Facts[(int)(Math.Abs(opportunityId) % count)];
            if (string.IsNullOrWhiteSpace(fact)) return string.Empty;
            source = "verified current-session outing telemetry";
            return fact;
        }

        // Relax now flows through the same candidate creation -> topic scoring -> personality ->
        // topic fatigue -> silence -> SocialBudget -> conversation thread pipeline as Normal/Camp
        // ambient chatter (AmbientSeedSelector, shared TopicFatigueTracker, /dsseeds diagnostics).
        // Only cadence (min/max delay) and the eventual prompt wording stay Relax-specific.
        private void EvaluateContextPulsePressure(DateTime now, WorldSnapshot world, IList<SimSnapshot> active)
        {
            long opportunityId;
            if (!_socialScheduler.TryTakeDue(now, active == null ? 0 : active.Count, _activityState,
                CurrentSocialPreset(), _activitySource, _random.NextDouble(), out opportunityId)) return;
            if (!CanAutonomouslySpeak(now, false))
            {
                LogOpportunityTerminal(0, "blocked", "local_autonomous_gate", null);
                _socialScheduler.NoteTerminal(opportunityId, "blocked", "local_autonomous_gate", false, false);
                return;
            }

            SocialSituationSnapshot situation = BuildSocialSituation(now, world, active);
            string capacityReason;
            if (!_plugin.CanAssessContextPulse(_inOrRecentCombat, out capacityReason))
            {
                LogOpportunityTerminal(0, "blocked", capacityReason, null);
                _socialScheduler.NoteTerminal(opportunityId, "blocked", capacityReason, false, false);
                return;
            }
            if (_contextPulsePending)
            {
                LogOpportunityTerminal(0, "blocked", "context_pulse_pending", null);
                _socialScheduler.NoteTerminal(opportunityId, "blocked", "context_pulse_pending", false, false);
                return;
            }
            _contextPulseOpportunityId = opportunityId;
            _contextPulsePending = _plugin.QueueContextPulse(situation, delegate
            {
                _socialScheduler.NoteInferenceStarted(opportunityId, DateTime.UtcNow);
                if (_log != null) _log.LogInfo("[DeepSims][ContextPulse] opportunity=" + opportunityId + " context_inference_started");
            }, delegate(ContextPulseDecision pulse)
            {
                _contextPulsePending = false;
                ApplyContextPulseDecision(opportunityId, pulse, situation, world, active);
            });
            if (!_contextPulsePending)
            {
                LogOpportunityTerminal(0, "blocked", "pulse_queue_unavailable", null);
                _socialScheduler.NoteTerminal(opportunityId, "blocked", "pulse_queue_unavailable", false, false);
            }
        }

        private void ApplyContextPulseDecision(long opportunityId, ContextPulseDecision pulse, SocialSituationSnapshot situation,
            WorldSnapshot world, IList<SimSnapshot> active)
        {
            DateTime now = DateTime.UtcNow;
            if (pulse != null && pulse.Cancelled)
            {
                LogOpportunityTerminal(opportunityId, "failed", pulse.TerminalReason, null);
                _socialScheduler.NoteTerminal(opportunityId, pulse.TerminalReason == "stale" ? "stale" : "failed",
                    pulse.TerminalReason, true, false);
                _contextPulseOpportunityId = 0;
                return;
            }
            if (_inOrRecentCombat || situation == null || !ContextPulsePolicy.IsEligibleActivity(_activityState) ||
                _activityState != situation.ActivityState)
            {
                LogOpportunityTerminal(0, "stale", "activity_changed", null);
                _socialScheduler.NoteTerminal(opportunityId, "stale", "activity_changed", true, false);
                _contextPulseOpportunityId = 0;
                return;
            }
            if (pulse == null)
            {
                LogOpportunityTerminal(0, "llm_rejected", "invalid_structured_pulse", null);
                _socialScheduler.NoteTerminal(opportunityId, "failed", "invalid_structured_pulse", true, false);
                _contextPulseOpportunityId = 0;
                return;
            }
            if (!pulse.SpeakNow)
            {
                bool fatigueEligible = active != null && active.Count >= 3 && active.Count <= 5 &&
                    CurrentSocialPreset() == SocialActivityPreset.Lively && !_inOrRecentCombat &&
                    (_activityState == SocialActivityState.SocialDowntime ||
                     _activityState == SocialActivityState.ExtendedDowntime);
                SilenceFatiguePressure fatiguePressure = _silenceFatigue.NoteSilence(fatigueEligible);
                if (_log != null) _log.LogInfo("[DeepSims][SilenceFatigue] decision=silence consecutiveSilence=" +
                    _silenceFatigue.Consecutive + " pressure=" + fatiguePressure + " seedSource=pending");
                if (fatiguePressure == SilenceFatiguePressure.Normal)
                {
                    _plugin.TryQueueOrganicCurrentEvent(situation);
                    LogOpportunityTerminal(0, "context_chose_silence", pulse.ReasonCategory, null);
                    _socialScheduler.NoteTerminal(opportunityId, "silence", pulse.ReasonCategory, true, false);
                    _contextPulseOpportunityId = 0;
                    return;
                }
            }

            bool forceSafeSeed = !pulse.SpeakNow && _silenceFatigue.Consecutive >= 3;
            double fatigueAdjust = !pulse.SpeakNow ? _silenceFatigue.SilenceAdjustment : 0.0;
            AmbientSeedDecision decision = EvaluateSeeds(now, world, active, 1.0, forceSafeSeed, fatigueAdjust);
            if (decision == null || decision.SilenceWon)
            {
                _plugin.TryQueueOrganicCurrentEvent(situation);
                LogOpportunityTerminal(decision == null ? 0 : decision.OpportunityId, "no_topic", "seed_selector", null);
                _socialScheduler.NoteTerminal(opportunityId, "no_topic", "seed_selector", true, false);
                _contextPulseOpportunityId = 0;
                return;
            }
            if (_log != null) _log.LogInfo("[DeepSims][SilenceFatigue] decision=speak consecutiveSilence=" +
                _silenceFatigue.Consecutive + " pressure=" + (forceSafeSeed ? "ForceSafeSeed" : "seed_selected") +
                " seedSource=" + SeedSourceToken(decision));
            string type = "context_pulse_" + decision.SelectedTopicKey;
            string semantic = "context-pulse|" + _relaxSessionOrdinal.ToString() + "|" + decision.SelectedTopicKey;
            string reason;
            if (!_plugin.TryAdmitAutonomousOpportunity(type, SocialPriority.Low, semantic, _inOrRecentCombat, out reason))
            {
                _seedDiagnostics.NoteOutcome(decision.OpportunityId, "social budget suppressed: " + reason);
                LogOpportunityTerminal(decision.OpportunityId, "blocked", reason, decision.SelectedSpeaker);
                _socialScheduler.NoteTerminal(opportunityId, "blocked", reason, true, false);
                _contextPulseOpportunityId = 0;
                return;
            }
            DirectorEvent evt = BuildRelaxEvent(decision, type, world);
            if (!string.IsNullOrWhiteSpace(pulse.CurrentTopic))
                evt.PromptHint = (evt.PromptHint ?? string.Empty) + " The current conversation assessment favors: " + pulse.CurrentTopic + ".";
            _seedDiagnostics.NoteOutcome(decision.OpportunityId, "queued");
            LogSocialOpportunityDiagnostic(decision, (now - _lastSocialUtc).TotalSeconds, now);
            _plugin.QueueAutonomousReaction(evt, decision.SelectedSpeaker, true, false);
            _socialScheduler.NoteTerminal(opportunityId, "speak", "queued_normal_path", true, false);
        }

        private void LogOpportunityTerminal(long id, string result, string reason, string speaker)
        {
            if (_log == null) return;
            _log.LogDebug("[DeepSims][SocialOpportunity] id=" + id + " activity=" + _activityState +
                " preset=" + CurrentSocialPreset() + " party=" + (_plugin == null ? 0 : _plugin.GetActiveDeepSims().Count) +
                " source=context_pulse result=" + (result ?? "unknown") + " reason=" +
                (string.IsNullOrWhiteSpace(reason) ? "none" : reason.Replace(' ', '_')) +
                (string.IsNullOrWhiteSpace(speaker) ? string.Empty : " speaker=" + speaker));
        }

        private void EvaluateSoftDowntimePressure(DateTime now, WorldSnapshot world, IList<SimSnapshot> active)
        {
            if (now < _nextIdleEvaluationUtc) return;
            if (!CanAutonomouslySpeak(now, false)) { _nextIdleEvaluationUtc = now.AddSeconds(12.0); return; }
            AmbientSeedDecision decision = EvaluateSeeds(now, world, active, 1.0, false);
            // Soft downtime is more conversational than Normal, but remains a chance to speak and
            // uses the same seed selector, fatigue tracker, and central SocialBudget.
            _nextIdleEvaluationUtc = now.AddSeconds(45.0 + (_random.NextDouble() * 75.0));
            if (decision == null || decision.SilenceWon) return;
            string socialReason;
            DirectorEvent evt = BuildAmbientEvent(decision);
            evt.Type = "soft_downtime";
            evt.Description = "SOFT DOWNTIME: the player has remained sitting outside combat for a sustained quiet moment. This is a social waiting opportunity, not a Hunt Camp, meditation, party-proximity, pull, route, or mana claim.";
            if (!_plugin.TryAdmitAutonomousOpportunity(evt.Type, SocialPriority.Low,
                "soft-downtime|" + decision.SelectedTopicKey, _inOrRecentCombat, out socialReason)) return;
            _seedDiagnostics.NoteOutcome(decision.OpportunityId, "queued");
            LogSocialOpportunityDiagnostic(decision, (now - _lastSocialUtc).TotalSeconds, now);
            _plugin.QueueAutonomousReaction(evt, decision.SelectedSpeaker, true, false);
        }

        private DirectorEvent BuildRelaxEvent(AmbientSeedDecision decision, string type, WorldSnapshot world)
        {
            string situation = RelaxSocialPolicy.BuildSituation(decision.SelectedTopicKey,
                world == null ? string.Empty : world.Scene, decision.SelectedFact);
            DirectorEvent evt = new DirectorEvent(type, situation, 20);
            evt.OpportunityId = decision.OpportunityId;
            evt.TopicKey = decision.SelectedTopicKey;
            evt.CooldownGroup = decision.SelectedCooldownGroup;
            evt.PromptHint = decision.SelectedPromptHint;
            evt.VerifiedFact = decision.SelectedFact;
            return evt;
        }

        internal SocialActivityPreset CurrentSocialPreset()
        {
            string configured = _plugin.SocialActivityPresetConfig == null ? "Adaptive" : _plugin.SocialActivityPresetConfig.Value;
            return AdaptiveActivityPolicy.IsAdaptive(configured) ? _adaptivePreset : SocialPolicy.ParsePreset(configured);
        }

        internal string DescribeActivityPreset()
        {
            string configured = _plugin.SocialActivityPresetConfig == null ? "Adaptive" : _plugin.SocialActivityPresetConfig.Value;
            if (!AdaptiveActivityPolicy.IsAdaptive(configured)) return configured + " (manual)";
            return "Adaptive -> " + _adaptivePreset + " (score=" + Math.Round(_adaptiveScore, 1) + "; " + _adaptiveReason + ")";
        }

        private void UpdateAdaptiveActivity(DateTime now, WorldSnapshot world, IList<SimSnapshot> active)
        {
            string configured = _plugin.SocialActivityPresetConfig == null ? "Adaptive" : _plugin.SocialActivityPresetConfig.Value;
            if (!AdaptiveActivityPolicy.IsAdaptive(configured)) return;
            string scene = world == null ? string.Empty : world.Scene ?? string.Empty;
            bool town = AdaptiveActivityPolicy.IsConfiguredTown(scene,
                _plugin.AdaptiveTownZonesConfig == null ? "Port Azure" : _plugin.AdaptiveTownZonesConfig.Value);
            string signature = scene.Trim().ToLowerInvariant() + "|" + ContextMode + "|" + PartyPersonalitySignature(active);
            if (now < _nextAdaptiveMoodUtc && string.Equals(signature, _adaptiveSignature, StringComparison.Ordinal)) return;

            AdaptiveActivityDecision decision = AdaptiveActivityPolicy.Decide(active, ContextMode, town, _random.NextDouble());
            SocialActivityPreset previous = _adaptivePreset;
            _adaptivePreset = decision.Preset;
            _adaptiveScore = decision.Score;
            _adaptiveReason = decision.Reason;
            _adaptiveSignature = signature;
            _nextAdaptiveMoodUtc = now.AddSeconds(180.0 + (_random.NextDouble() * 180.0));
            if (_plugin != null) _plugin.ApplyEffectiveSocialPreset(_adaptivePreset);
            if (previous != _adaptivePreset || _verboseLogging != null && _verboseLogging.Value)
                VerboseDebug("adaptive activity=" + _adaptivePreset + " score=" + Math.Round(_adaptiveScore, 1) + " " + _adaptiveReason);
        }

        private static string PartyPersonalitySignature(IList<SimSnapshot> active)
        {
            List<string> parts = new List<string>();
            for (int i = 0; active != null && i < active.Count; i++)
            {
                SimSnapshot s = active[i];
                if (s == null) continue;
                parts.Add((s.Name ?? string.Empty) + ":" + s.PersonalityCode + ":" + s.Patience + ":" +
                    (s.Rival ? "r" : "-") + (s.Abbreviates ? "a" : "-") + (s.LovesEmojis ? "e" : "-"));
            }
            parts.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(",", parts.ToArray());
        }

        private double NextRelaxDelay(SocialActivityPreset preset)
        {
            int partyCount = _plugin == null ? 0 : _plugin.GetActiveDeepSims().Count;
            if (preset == SocialActivityPreset.Lively)
            {
                double livelyMin, livelyMax;
                AmbientCadence.LivelyPartyRange(partyCount, out livelyMin, out livelyMax);
                if (partyCount >= 3) { livelyMin *= 0.85; livelyMax *= 0.85; }
                return livelyMin + (_random.NextDouble() * (livelyMax - livelyMin));
            }
            double min = RelaxSocialPolicy.MinimumSeconds(preset);
            double max = Math.Max(min, RelaxSocialPolicy.MaximumSeconds(preset));
            return min + (_random.NextDouble() * (max - min));
        }

        // Relax topic choice is now the shared AmbientSeedSelector's job (see EvaluateSeeds); topic
        // recency/repetition is tracked by the shared TopicFatigueTracker instead of a separate ring.
        private string PickVerifiedOutingFact(WorldSnapshot world, long opportunityId)
        {
            if (world == null || world.Outing == null || world.Outing.Facts == null || world.Outing.Facts.Count == 0) return string.Empty;
            int count = Math.Min(6, world.Outing.Facts.Count);
            if (count <= 0) return string.Empty;
            string fact = world.Outing.Facts[(int)(Math.Abs(opportunityId) % count)];
            return fact == null ? string.Empty : fact.Trim();
        }

        // verified_history draws on the same verified per-Sim memory records the Normal/Camp
        // shared-memory candidates use (MemoryStore-written outing summaries/important memories),
        // deterministically picked so a fixed (opportunity, state) pair reproduces the same choice.
        private string PickVerifiedHistoryFact(IList<SimSnapshot> active, long opportunityId)
        {
            if (active == null) return string.Empty;
            for (int i = 0; i < active.Count; i++)
            {
                SimSnapshot sim = active[i];
                if (sim == null || string.IsNullOrWhiteSpace(sim.Name)) continue;
                SimMemory memory = _plugin.LoadMemoryForSeeding(sim);
                if (memory == null) continue;
                List<string> items = memory.OutingSummaries != null && memory.OutingSummaries.Count > 0
                    ? memory.OutingSummaries : memory.ImportantMemories;
                if (items == null || items.Count == 0) continue;
                int count = Math.Min(4, items.Count);
                int start = items.Count - count;
                string value = items[start + (int)(Math.Abs(opportunityId + i) % count)];
                if (!string.IsNullOrWhiteSpace(value))
                    return items == memory.OutingSummaries ? VerifiedOutingHistoryPolicy.SanitizeForPrompt(value) : value.Trim();
            }
            return string.Empty;
        }

        // A callback is offered to the same AmbientSeedSelector pipeline as any other subject, with a
        // prompt hint that only ever licenses safe "this was said" wording - never an invented shared
        // event. TopicKey/CooldownGroup are namespaced under "callback" so fatigue treats callbacks as
        // their own family rather than colliding with the underlying subject's own cooldown.
        private static AmbientSeedCandidate BuildCallbackCandidate(ConversationMoment moment, DateTime now)
        {
            string hint = "A few minutes ago " + (string.IsNullOrWhiteSpace(moment.Speaker) ? "someone" : moment.Speaker) +
                " said (unverified, HEARD only): \"" + moment.TextSummary + "\". If it still feels natural, you may " +
                "briefly pick that back up using safe wording like 'you were saying...', 'you mentioned...', or " +
                "'still think...'. Never say 'remember when we did that' or that it happened 'again' - you only know it was SAID.";
            return new AmbientSeedCandidate("callback_" + moment.TopicKey, "callback", hint,
                18.0 + Math.Min(10.0, moment.InterestScore / 4.0), null, null, 0, 0.0, now, moment.ExpiresUtc, null);
        }

        private void MaybeReact(string type, string description, double baseChance, string preferredSpeaker, bool allowBanter)
        {
            if (!_plugin.DirectorEnabledConfig.Value || !_plugin.EventChatterConfig.Value) return;
            DateTime now = DateTime.UtcNow;
            if (!CanAutonomouslySpeak(now, true)) return;

            double chance = _plugin.GetEventReactionChance(type, baseChance) * _plugin.GetSocialOpportunityMultiplier();
            if (_random.NextDouble() > Math.Min(1.0, chance)) return;

            string socialReason;
            if (!_plugin.TryAdmitAutonomousOpportunity(type, SocialPolicy.PriorityOf(type, 50),
                type + "|" + (description ?? string.Empty), _inOrRecentCombat, out socialReason))
            {
                VerboseDebug("Autonomous reaction suppressed: type=" + type + ", reason=" + socialReason);
                return;
            }
            _plugin.QueueAutonomousReaction(new DirectorEvent(type, description, 50), preferredSpeaker, allowBanter, false);
        }

        private bool CanAutonomouslySpeak(DateTime now, bool eventDriven)
        {
            if (!_plugin.DirectorEnabledConfig.Value) return false;
            if (now < _partySettlingUntilUtc) return false;
            if (IsPracticeDuelActive()) return false;
            if (_plugin.PauseAutonomousInCombatConfig != null && _plugin.PauseAutonomousInCombatConfig.Value && _inOrRecentCombat) return false;
            double cooldown = eventDriven
                ? Math.Max(3.0, _plugin.EventCooldownSecondsConfig == null ? 12.0 : _plugin.EventCooldownSecondsConfig.Value)
                : Math.Max(5.0, _plugin.AutonomousCooldownSecondsConfig.Value);
            if ((now - _lastAutonomousUtc).TotalSeconds < cooldown) return false;
            return true;
        }

        private static bool IsPracticeDuelActive()
        {
            try
            {
                foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type type = assembly.GetType("ErenshorDuel.DuelController", false);
                    if (type == null) continue;
                    System.Reflection.PropertyInfo active = type.GetProperty("Active", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    return active != null && active.PropertyType == typeof(bool) && (bool)active.GetValue(null, null);
                }
            }
            catch { }
            return false;
        }

        private void NoteSocialActivity(DateTime now)
        {
            _lastSocialUtc = now;
            double delay;
            if (_relaxActive) delay = RelaxSocialPolicy.MinimumSeconds(CurrentSocialPreset());
            else if (_campActive && _plugin.CampIdleMinSecondsConfig != null)
                delay = Math.Max(10.0, _plugin.CampIdleMinSecondsConfig.Value);
            else
            {
                // Variable, weighted-band delay instead of one fixed cooldown: this only decides WHEN
                // the party gets another chance to consider something social, not that it must speak.
                delay = Math.Max(NormalIdleMinimum(), AmbientCadence.NextDelaySeconds(CurrentSocialPreset(),
                    _plugin == null ? 0 : _plugin.GetActiveDeepSims().Count, _random));
            }
            _nextIdleEvaluationUtc = now.AddSeconds(delay);
            if (_log != null && CurrentSocialPreset() == SocialActivityPreset.Lively)
                _log.LogDebug("[DeepSims][SocialPacing] preset=Lively eligibleParty=" +
                    (_plugin == null ? 0 : _plugin.GetActiveDeepSims().Count) + " state=idle nextOpportunitySeconds=" + Math.Round(delay));
        }

        private void ObserveCampmaster(DateTime now, bool inCombat)
        {
            if (_campmaster == null || !_campmaster.Healthy)
            {
                _campmasterActive = false;
                if (_relaxActive)
                {
                    _relaxActive = false;
                    SocialDowntimeContext.SetRelaxActive(false);
                }
                return;
            }

            List<CampmasterSemanticEvent> events = _campmaster.Poll(now);
            bool sawStart = false;
            for (int i = 0; i < events.Count; i++)
            {
                CampmasterSemanticEvent evt = events[i];
                if (evt == null || string.IsNullOrWhiteSpace(evt.Type)) continue;
                string type = evt.Type.Trim().ToLowerInvariant();
                if (type == "camp_started")
                {
                    sawStart = true;
                    RecordCampmasterStart(evt, now);
                }
                else if (type == "camp_ended" || type == "camp_suspended")
                {
                    _campmasterActive = false;
                    if (!_manualCamp) _campActive = false;
                }
                else if (type == "camp_resumed") _campmasterActive = true;
            }

            bool relax = _campmaster.IsRelaxActive;
            if (relax != _relaxActive)
            {
                _relaxActive = relax;
                SocialDowntimeContext.SetRelaxActive(relax);
                if (relax)
                {
                    _relaxSessionOrdinal++;
                    _recentRelaxTopics.Clear();
                    _manualCamp = false;
                    _campActive = false;
                    _lastSocialUtc = now;
                    _nextIdleEvaluationUtc = now.AddSeconds(RelaxSocialPolicy.InitialSeconds(CurrentSocialPreset()));
                    VerboseDebug("Campmaster Relax became active; first social opportunity scheduled.");
                }
                else
                {
                    _recentRelaxTopics.Clear();
                    _lastSocialUtc = now;
                    _nextIdleEvaluationUtc = now.AddSeconds(NormalIdleMinimum());
                    VerboseDebug("Campmaster Relax ended; normal social cadence restored.");
                }
            }
            else SocialDowntimeContext.SetRelaxActive(relax);

            bool active = _campmaster.IsHuntCampActive;
            if (active && !_campmasterActive && !sawStart)
            {
                // Deep Sims may bind after a Campmaster session already began. The current Campmaster
                // snapshot is sufficient deterministic evidence for one canonical semantic start.
                RecordCampmasterStart(null, now);
            }
            _campmasterActive = active;
            if (_relaxActive) _campActive = false;
            else if (!_manualCamp) _campActive = active && !inCombat;
        }

        private void RecordCampmasterStart(CampmasterSemanticEvent evt, DateTime now)
        {
            string detail = evt == null ? string.Empty : evt.Detail;
            string zone = evt == null ? string.Empty : evt.Zone;
            string description = "Campmaster verified that the current Hunt Camp is active";
            if (!string.IsNullOrWhiteSpace(zone)) description += " in " + zone;
            if (!string.IsNullOrWhiteSpace(detail)) description += ": " + detail.Trim().TrimEnd('.');
            description += ".";

            // Campmaster itself sequences events. If Deep Sims already recognized the active session
            // from current state, an immediately-following retained/start notification is equivalent.
            if (_campmasterActive) return;
            _plugin.RecordSharedEvent("hunt_camp_start", description, 30, false);
            _campmasterActive = true;
            _campActive = !_inOrRecentCombat;
            _lastSocialUtc = now;
            _nextIdleEvaluationUtc = now.AddSeconds(Math.Max(10.0, _plugin.CampIdleMinSecondsConfig == null ? 25.0 : _plugin.CampIdleMinSecondsConfig.Value));
        }

        private void UpdateCampState(DateTime now, bool inCombat)
        {
            bool sitting = false;
            try { sitting = GameData.PlayerControl != null && GameData.PlayerControl.Sitting; } catch { }
            if (!sitting) _campSuppressedUntilStanding = false;

            if (_relaxActive)
            {
                _campActive = false;
                _sittingSinceUtc = DateTime.MinValue;
                return;
            }

            // A healthy Campmaster installation owns automatic Hunt Camp semantics. Deep Sims still
            // honors an explicit /dscamp command, but it does not create a second sitting-derived
            // camp_start beside Campmaster's deterministic session event.
            if (_campmaster != null && _campmaster.Healthy && !_manualCamp)
            {
                _campActive = !_relaxActive && _campmasterActive && !inCombat;
                _sittingSinceUtc = DateTime.MinValue;
                return;
            }

            if (inCombat)
            {
                _campActive = false;
                _sittingSinceUtc = DateTime.MinValue;
                return;
            }

            bool automatic = _plugin.CampModeConfig != null && _plugin.CampModeConfig.Value && sitting && !_campSuppressedUntilStanding;
            bool requested = _manualCamp || automatic;
            if (!requested)
            {
                _campActive = false;
                _sittingSinceUtc = DateTime.MinValue;
                return;
            }
            if (_sittingSinceUtc == DateTime.MinValue) _sittingSinceUtc = now;
            double enterDelay = _manualCamp ? 0.0 : Math.Max(0.0, _plugin.CampEnterSecondsConfig == null ? 8.0 : _plugin.CampEnterSecondsConfig.Value);
            if (_campActive || (now - _sittingSinceUtc).TotalSeconds < enterDelay) return;

            _campActive = true;
            _lastSocialUtc = now;
            _nextIdleEvaluationUtc = now.AddSeconds(Math.Max(10.0, _plugin.CampIdleMinSecondsConfig == null ? 25.0 : _plugin.CampIdleMinSecondsConfig.Value));
            DirectorEvent campStart = new DirectorEvent("camp_start", "Current situation: the player has stopped and is sitting or meditating with the visible party in a safe moment outside combat.", 20);
            if (_manualCamp)
            {
                // /dscamp on is an explicit request to make downtime social. Give it one prompt
                // line immediately instead of losing the opportunity to a prior idle cooldown.
                _plugin.QueueAutonomousReaction(campStart, null, true, true);
                _lastAutonomousUtc = now;
            }
            else
                MaybeReact("camp_start", campStart.Description, 0.85, null, false);
        }

        private void UpdateActivityContext(DateTime now, IList<SimSnapshot> active, bool inCombat)
        {
            _campmasterSocialContext = _campmaster == null ? null : _campmaster.ReadSocialContext();
            if (_campmasterSocialContext != null && !string.IsNullOrWhiteSpace(_campmasterSocialContext.ActivityState))
            {
                SocialActivityState prior = _activityState;
                SocialActivityState parsed = ParseActivityState(_campmasterSocialContext.ActivityState);
                if (parsed == SocialActivityState.Unknown)
                {
                    _activityReason = "campmaster_unknown_fallback";
                    _contextConsistency = "fallback";
                    UpdateStandaloneActivity(now, active, inCombat);
                    return;
                }
                if (CampmasterContextConsistencyPolicy.IsStale(parsed, _campmasterSocialContext.ActivityReason,
                    _campmasterSocialContext.SecondsStationary, _campmasterSocialContext.SecondsOutOfCombat,
                    _campmasterSocialContext.SecondsSinceMeaningfulGameplay))
                {
                    if (!_campmasterStaleWarningLogged && _log != null)
                    {
                        _campmasterStaleWarningLogged = true;
                        _log.LogWarning("[DeepSims][SocialScheduler] Campmaster activity snapshot was internally stale; using standalone downtime fallback.");
                    }
                    UpdateStandaloneActivity(now, active, inCombat);
                    if (!inCombat && active != null && active.Count > 0 &&
                        _campmasterSocialContext.SecondsStationary >= 60.0 &&
                        _campmasterSocialContext.SecondsOutOfCombat >= 60.0)
                    {
                        _softDowntimeActive = true;
                        _standaloneStationarySeconds = _campmasterSocialContext.SecondsStationary;
                        _standaloneOutOfCombatSeconds = _campmasterSocialContext.SecondsOutOfCombat;
                        _activityState = SocialActivityState.SocialDowntime;
                    }
                    _activitySource = "standalone";
                    _activityReason = "stale_campmaster_" + SafeToken(_campmasterSocialContext.ActivityReason);
                    _contextConsistency = "fallback";
                    return;
                }
                _activityState = parsed;
                _activitySource = "campmaster";
                _activityReason = string.IsNullOrWhiteSpace(_campmasterSocialContext.ActivityReason)
                    ? "campmaster_activity" : SafeToken(_campmasterSocialContext.ActivityReason);
                _contextConsistency = "fresh";
                _campmasterStaleWarningLogged = false;
                if (_activityState != SocialActivityState.SocialDowntime && _activityState != SocialActivityState.ExtendedDowntime)
                    _contextPulsePending = false;
                _softDowntimeActive = false;
                _softDowntimeSinceUtc = DateTime.MinValue;
                _softDowntimeHasAnchor = false;
                _standaloneStationarySeconds = 0;
                _standaloneOutOfCombatSeconds = 0;
                _standaloneOutOfCombatSinceUtc = DateTime.MinValue;
                if (prior != _activityState)
                    VerboseDebug("[DeepSims][Activity] " + prior + " -> " + _activityState +
                        " stationary=" + Math.Round(_campmasterSocialContext.SecondsStationary) + "s outOfCombat=" +
                        Math.Round(_campmasterSocialContext.SecondsOutOfCombat) + "s");
                return;
            }

            // Campmaster is optional. Without its additive activity contract, preserve the existing
            // local soft-downtime behavior as a fail-soft pacing fallback, never as competing camp truth.
            UpdateStandaloneActivity(now, active, inCombat);
        }

        private void UpdateStandaloneActivity(DateTime now, IList<SimSnapshot> active, bool inCombat)
        {
            UpdateSoftDowntime(now, active, inCombat);
            _activitySource = "standalone";
            _activityState = inCombat ? SocialActivityState.Combat :
                _softDowntimeActive ? SocialActivityState.SocialDowntime : SocialActivityState.ActiveGameplay;
            _activityReason = inCombat ? "combat_or_grace" : _softDowntimeActive ? "stationary_safe_threshold_met" : "downtime_threshold_not_met";
            _contextConsistency = "fallback";
        }

        private static SocialActivityState ParseActivityState(string value)
        {
            SocialActivityState parsed;
            return Enum.TryParse<SocialActivityState>(value ?? string.Empty, true, out parsed) ? parsed : SocialActivityState.Unknown;
        }

        private SocialSituationSnapshot BuildSocialSituation(DateTime now, WorldSnapshot world, IList<SimSnapshot> active)
        {
            SocialSituationSnapshot value = new SocialSituationSnapshot();
            value.ActivityState = _activityState;
            value.Scene = world == null ? string.Empty : world.Scene;
            value.EligiblePartyCount = active == null ? 0 : active.Count;
            for (int i = 0; active != null && i < active.Count; i++)
                if (active[i] != null && !string.IsNullOrWhiteSpace(active[i].Name)) value.EligiblePartyMembers.Add(active[i].Name);
            value.SecondsStationary = CurrentStationarySeconds();
            value.SecondsOutOfCombat = CurrentOutOfCombatSeconds();
            value.SecondsSinceMeaningfulGameplay = _campmasterSocialContext == null ? 0 : _campmasterSocialContext.SecondsSinceMeaningfulGameplay;
            value.SecondsSincePlayerSpeech = SecondsSince(_lastPlayerSpeechUtc, now);
            value.SecondsSincePartySpeech = SecondsSince(_lastPartySpeechUtc, now);
            value.SecondsSinceAnyObservedChat = SecondsSince(_lastObservedChatUtc, now);
            value.SecondsSinceAutonomousSpeech = SecondsSince(_lastAutonomousSpeechUtc, now);
            value.CurrentThread = _plugin == null ? string.Empty : _plugin.CurrentSocialThreadDescription();
            value.RecentChat = _recentSocialChat.Snapshot(now, 900.0);
            if (value.RecentChat.Count > 0) value.LastVisibleSpeaker = value.RecentChat[value.RecentChat.Count - 1].Speaker;
            value.CurrentCampmasterState = _campmasterSocialContext == null ? "unavailable" :
                (_campmasterSocialContext.Mode ?? "None") + "/" + (_campmasterSocialContext.Recognition ?? "unknown");
            value.SocialBudget = _plugin == null ? string.Empty : _plugin.DescribeSocialBudget();
            value.PacingPreset = CurrentSocialPreset();
            value.AutoRelax = _campmasterSocialContext != null && _campmasterSocialContext.AutoRelaxActive;
            value.AutoCamp = false;
            value.EphemeralSummary = _plugin == null ? string.Empty : _plugin.CurrentEphemeralSocialSummary();
            value.Unanswered = _unansweredTurns.Current(now);
            return value;
        }

        private static double SecondsSince(DateTime value, DateTime now)
        {
            return value == DateTime.MinValue ? 9999.0 : Math.Max(0.0, (now - value).TotalSeconds);
        }

        private void UpdateSoftDowntime(DateTime now, IList<SimSnapshot> active, bool inCombat)
        {
            bool sitting = false;
            UnityEngine.Component player = null;
            try
            {
                sitting = GameData.PlayerControl != null && GameData.PlayerControl.Sitting;
                player = GameData.PlayerControl as UnityEngine.Component;
            }
            catch { }

            bool blocked = _plugin == null || !_plugin.CharacterScopeReady || inCombat || active == null || active.Count == 0;
            if (player == null || blocked)
            {
                if (_softDowntimeActive) VerboseDebug("soft downtime exited reason=" + (inCombat ? "combat" : "camp-or-party-state"));
                _softDowntimeActive = false;
                _softDowntimeSinceUtc = DateTime.MinValue;
                _softDowntimeHasAnchor = false;
                _standaloneStationarySeconds = 0;
                _standaloneOutOfCombatSeconds = 0;
                _standaloneOutOfCombatSinceUtc = DateTime.MinValue;
                return;
            }

            if (_standaloneOutOfCombatSinceUtc == DateTime.MinValue) _standaloneOutOfCombatSinceUtc = now;
            _standaloneOutOfCombatSeconds = Math.Max(0.0, (now - _standaloneOutOfCombatSinceUtc).TotalSeconds);
            string scene = string.Empty;
            try { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? string.Empty; } catch { }
            if (!string.Equals(scene, _standaloneScene, StringComparison.OrdinalIgnoreCase))
            {
                _standaloneScene = scene;
                _softDowntimeHasAnchor = false;
                _softDowntimeActive = false;
                _standaloneStationarySeconds = 0;
            }

            UnityEngine.Vector3 position = player.transform.position;
            if (!_softDowntimeHasAnchor) { _softDowntimeAnchor = position; _softDowntimeHasAnchor = true; _softDowntimeSinceUtc = now; return; }
            if ((position - _softDowntimeAnchor).sqrMagnitude > 9.0f)
            {
                _softDowntimeActive = false;
                _softDowntimeSinceUtc = now;
                _softDowntimeAnchor = position;
                _nextIdleEvaluationUtc = now.AddSeconds(25.0);
                _standaloneStationarySeconds = 0;
                return;
            }
            _standaloneStationarySeconds = Math.Max(0.0, (now - _softDowntimeSinceUtc).TotalSeconds);
            double requiredSeconds = 60.0;
            if (!_softDowntimeActive && (now - _softDowntimeSinceUtc).TotalSeconds >= requiredSeconds)
            {
                _softDowntimeActive = true;
                _lastSocialUtc = now;
                // The party has already demonstrated a real quiet pause; the shared scheduler will
                // re-arm at its faster standalone-downtime cadence.
                _nextIdleEvaluationUtc = now.AddSeconds(15.0);
                VerboseDebug("soft downtime active source=" + (sitting ? "sitting" : "same_area") +
                    " delay=" + requiredSeconds + "s");
            }
        }

        private double CurrentStationarySeconds()
        {
            return _campmasterSocialContext != null && _activitySource == "campmaster"
                ? _campmasterSocialContext.SecondsStationary : _standaloneStationarySeconds;
        }

        private double CurrentOutOfCombatSeconds()
        {
            return _campmasterSocialContext != null && _activitySource == "campmaster"
                ? _campmasterSocialContext.SecondsOutOfCombat : _standaloneOutOfCombatSeconds;
        }

        private double CurrentMeaningfulGameplayAge()
        {
            return _campmasterSocialContext == null ? 0.0 : _campmasterSocialContext.SecondsSinceMeaningfulGameplay;
        }

        private static string SafeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";
            StringBuilder sb = new StringBuilder();
            string lower = value.Trim().ToLowerInvariant();
            for (int i = 0; i < lower.Length && sb.Length < 48; i++)
            {
                char c = lower[i];
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            }
            return sb.ToString().Trim('_');
        }

        private void ResetStandaloneDowntime(DateTime now)
        {
            _softDowntimeActive = false;
            _softDowntimeSinceUtc = now;
            _softDowntimeHasAnchor = false;
            _standaloneStationarySeconds = 0;
            _socialScheduler.RequestRefresh();
        }

        private void LogSchedulerHeartbeat(DateTime now, int partyCount, bool authority, string authorityReason)
        {
            if (_log == null || now < _nextSchedulerHeartbeatUtc || _plugin == null || !_plugin.CharacterScopeReady) return;
            _nextSchedulerHeartbeatUtc = now.AddSeconds(30.0);
            _log.LogInfo("[DeepSims][SocialScheduler] state=alive characterReady=True party=" + partyCount +
                " authority=" + authority + " preset=" + CurrentSocialPreset() + " activity=" + _activityState +
                " source=" + _activitySource + " activityReason=" + _activityReason +
                " contextConsistency=" + _contextConsistency + " " + _socialScheduler.DescribeStatus(now) +
                (authority || string.IsNullOrWhiteSpace(authorityReason) ? string.Empty : " authorityReason=" + authorityReason.Replace(' ', '_')));
        }

        private static string FindAddressedSim(string message, IList<SimSnapshot> active)
        {
            if (string.IsNullOrWhiteSpace(message) || active == null) return null;
            string lower = message.ToLowerInvariant();
            for (int i = 0; i < active.Count; i++)
            {
                SimSnapshot sim = active[i];
                if (sim == null || string.IsNullOrWhiteSpace(sim.Name)) continue;
                string name = sim.Name.ToLowerInvariant();
                int index = lower.IndexOf(name, StringComparison.Ordinal);
                if (index < 0) continue;
                bool leftOk = index == 0 || !char.IsLetterOrDigit(lower[index - 1]);
                int right = index + name.Length;
                bool rightOk = right >= lower.Length || !char.IsLetterOrDigit(lower[right]);
                if (leftOk && rightOk) return sim.Name;
            }
            return null;
        }

        private static string FindClassRelevantSim(string message, IList<SimSnapshot> active)
        {
            if (string.IsNullOrWhiteSpace(message) || active == null) return null;
            string lower = " " + message.ToLowerInvariant() + " ";
            for (int i = 0; i < active.Count; i++)
            {
                SimSnapshot sim = active[i];
                if (sim == null || string.IsNullOrWhiteSpace(sim.ClassName)) continue;
                string cls = sim.ClassName.ToLowerInvariant();
                if (lower.Contains(" " + cls + " ")) return sim.Name;
            }
            return null;
        }

        private static bool LooksLikeQuestion(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            string m = message.Trim().ToLowerInvariant();
            if (m.IndexOf('?') >= 0) return true;
            string[] starts = new string[] { "who ", "what ", "where ", "when ", "why ", "how ", "can ", "could ", "should ", "would ", "do ", "does ", "did ", "is ", "are ", "anyone ", "anybody " };
            for (int i = 0; i < starts.Length; i++) if (m.StartsWith(starts[i], StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool LooksTrivial(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return true;
            string m = message.Trim().ToLowerInvariant().Trim('.', '!', '?', ' ');
            string[] trivial = new string[] { "lol", "lmao", "ok", "okay", "k", "nice", "gg", "yep", "yeah", "yea", "sure", "cool", "thanks", "ty", "haha", "heh" };
            for (int i = 0; i < trivial.Length; i++) if (m == trivial[i]) return true;
            return m.Length <= 2;
        }

        private static bool LooksLikeGreeting(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            string m = message.Trim().ToLowerInvariant().Trim('!', '.', '?', ' ');
            return m == "hi" || m == "hey" || m == "hello" || m == "yo" || m == "sup" || m == "whats up" || m == "what's up" ||
                m.StartsWith("thanks for the invite", StringComparison.Ordinal) || m.StartsWith("reporting for duty", StringComparison.Ordinal);
        }

        private static bool LooksLikeVanillaTacticalChatter(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return true;
            string m = message.Trim().ToLowerInvariant();
            string[] starts = new string[]
            {
                "casting ", "assisting ", "attacking ", "killing ", "following ", "pulling ", "target is ",
                "roger", "aye aye", "consider it done", "on it", "lead on", "lets do this", "let's do this"
            };
            for (int i = 0; i < starts.Length; i++) if (m.StartsWith(starts[i], StringComparison.Ordinal)) return true;
            if (m.Contains("'s target is ") || m.Contains(" is on a ") || m.Contains(" and so am i")) return true;
            return false;
        }

        private bool IsDuplicate(string type, double seconds)
        {
            DateTime now = DateTime.UtcNow;
            DateTime last;
            if (_lastEventUtc.TryGetValue(type, out last) && (now - last).TotalSeconds < seconds) return true;
            _lastEventUtc[type] = now;
            return false;
        }

        private void VerboseDebug(string message)
        {
            if (_verboseLogging != null && _verboseLogging.Value && _log != null) _log.LogDebug(message);
        }

        private double NormalIdleMinimum()
        {
            if (CurrentSocialPreset() == SocialActivityPreset.Lively)
            {
                double min, max;
                AmbientCadence.LivelyPartyRange(_plugin == null ? 0 : _plugin.GetActiveDeepSims().Count, out min, out max);
                return min;
            }
            double configured = _plugin.IdleMinSecondsConfig == null ? 90.0 : _plugin.IdleMinSecondsConfig.Value;
            return Math.Max(30.0, SocialPolicy.ScaleAmbientSeconds(CurrentSocialPreset(), configured));
        }

        private double NormalIdleMaximum()
        {
            if (CurrentSocialPreset() == SocialActivityPreset.Lively)
            {
                double min, max;
                AmbientCadence.LivelyPartyRange(_plugin == null ? 0 : _plugin.GetActiveDeepSims().Count, out min, out max);
                return max;
            }
            double configured = _plugin.IdleMaxSecondsConfig == null ? 300.0 : _plugin.IdleMaxSecondsConfig.Value;
            return Math.Max(NormalIdleMinimum() + 1.0, SocialPolicy.ScaleAmbientSeconds(CurrentSocialPreset(), configured));
        }

        private string DescribeQuietTime()
        {
            if (_lastSocialUtc == DateTime.MinValue) return "not started";
            double seconds = (DateTime.UtcNow - _lastSocialUtc).TotalSeconds;
            if (seconds < 0) seconds = 0;
            return Math.Round(seconds) + "s";
        }

        private static string OnOff(bool value) { return value ? "on" : "off"; }
    }

    internal class DirectorEvent
    {
        internal string Type;
        internal string Description;
        internal int Importance;

        // Set only for ambient opportunities that a selected seed owns. TopicKey is the semantic
        // subject whose fatigue is recorded after a line actually emits; VerifiedFact, when present,
        // came from telemetry/memory and is never generated here.
        internal long OpportunityId;
        internal string TopicKey = string.Empty;
        internal string CooldownGroup = string.Empty;
        internal string PromptHint = string.Empty;
        internal string VerifiedFact = string.Empty;

        internal DirectorEvent(string type, string description, int importance)
        {
            Type = type;
            Description = description;
            Importance = importance;
        }

        internal bool HasSeed { get { return !string.IsNullOrEmpty(TopicKey); } }
    }
}
