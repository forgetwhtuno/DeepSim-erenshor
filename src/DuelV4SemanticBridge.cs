using System;
using System.Collections.Generic;
using System.Reflection;

namespace ErenshorDeepSims
{
    // Optional read-only adapter for Practice Duel contract v4.  The older DeepSims compatibility
    // callback intentionally stays supported, but it cannot carry DuelId/participant A/B on current
    // public builds.  Binding the public SemanticEvent here lets Living Social use exact correlation
    // without making Deep Sims compile-time dependent on Practice Duel.
    internal static class DuelV4SemanticBridge
    {
        private const string EventsTypeName = "ErenshorDuel.PracticeDuelEvents";
        private static readonly object Gate = new object();
        private static readonly HashSet<string> Seen = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Queue<string> SeenOrder = new Queue<string>();
        private static EventInfo _eventInfo;
        private static Delegate _handler;
        private static bool _bound;
        private static DateTime _nextResolveUtc = DateTime.MinValue;

        internal static bool IsBound
        {
            get { lock (Gate) return _bound; }
        }

        internal static void Refresh()
        {
            lock (Gate)
            {
                if (_bound) return;
                DateTime now = DateTime.UtcNow;
                if (now < _nextResolveUtc) return;
                _nextResolveUtc = now.AddSeconds(2.0);

                Type eventsType = FindType(EventsTypeName);
                if (eventsType == null) return;
                FieldInfo version = eventsType.GetField("ContractVersion", BindingFlags.Public | BindingFlags.Static);
                int contract;
                try { contract = version == null ? 0 : Convert.ToInt32(version.GetValue(null)); }
                catch { return; }
                if (!DuelV4ContractPolicy.AcceptsContract(contract)) return;

                EventInfo semanticEvent = eventsType.GetEvent("SemanticEvent", BindingFlags.Public | BindingFlags.Static);
                if (semanticEvent == null || semanticEvent.EventHandlerType == null || !semanticEvent.EventHandlerType.IsGenericType) return;
                Type genericDefinition = semanticEvent.EventHandlerType.GetGenericTypeDefinition();
                if (genericDefinition != typeof(Action<>)) return;
                Type[] args = semanticEvent.EventHandlerType.GetGenericArguments();
                if (args == null || args.Length != 1 || args[0] == null) return;

                MethodInfo binder = typeof(DuelV4SemanticBridge).GetMethod("BindTyped", BindingFlags.NonPublic | BindingFlags.Static);
                if (binder == null) return;
                try { binder.MakeGenericMethod(args[0]).Invoke(null, new object[] { semanticEvent }); }
                catch { _bound = false; _eventInfo = null; _handler = null; }
            }
        }

        private static void BindTyped<T>(EventInfo semanticEvent)
        {
            Action<T> handler = delegate(T value) { OnSemanticEvent(value); };
            semanticEvent.AddEventHandler(null, handler);
            _eventInfo = semanticEvent;
            _handler = handler;
            _bound = true;
        }

        internal static void Shutdown()
        {
            lock (Gate)
            {
                if (_eventInfo != null && _handler != null)
                    try { _eventInfo.RemoveEventHandler(null, _handler); } catch { }
                _eventInfo = null;
                _handler = null;
                _bound = false;
                _nextResolveUtc = DateTime.MinValue;
                Seen.Clear();
                SeenOrder.Clear();
            }
        }

        private static void OnSemanticEvent<T>(T value)
        {
            if (object.ReferenceEquals(value, null)) return;
            try { HandleObject((object)value); } catch { }
        }

        private static void HandleObject(object value)
        {
            DeepSimsPlugin plugin = DeepSimsPlugin.Instance;
            if (plugin == null || plugin.EnabledConfig == null || !plugin.EnabledConfig.Value || value == null) return;
            Type typeInfo = value.GetType();
            string eventType = Token(ReadString(typeInfo, value, "Type"), 48);
            if (!IsAllowed(eventType)) return;

            string opponent = Clean(ReadString(typeInfo, value, "OpponentName"), 80);
            string scope = Token(ReadString(typeInfo, value, "OpponentScope"), 32);
            string decision = Token(ReadString(typeInfo, value, "Decision"), 48);
            string outcome = Token(ReadString(typeInfo, value, "Outcome"), 48);
            string winner = Clean(ReadString(typeInfo, value, "Winner"), 80);
            string yielded = Clean(ReadString(typeInfo, value, "Yielded"), 80);
            string reasonToken = Token(ReadString(typeInfo, value, "ReasonToken"), 48);
            string reason = Clean(ReadString(typeInfo, value, "Reason"), 160);
            string requestId = Clean(ReadString(typeInfo, value, "RequestId"), 96);
            string duelId = Clean(ReadString(typeInfo, value, "DuelId"), 96);
            string origin = Token(ReadString(typeInfo, value, "Origin"), 32);
            string source = Token(ReadString(typeInfo, value, "Source"), 48);
            string sourceSystem = Token(ReadString(typeInfo, value, "SourceSystem"), 48);
            if (sourceSystem.Length > 0 && sourceSystem != "practice_duel") return;
            if (origin.Length > 0 && origin != "autonomous" && origin != "explicit_player") return;
            string participantA = Clean(ReadString(typeInfo, value, "ParticipantA"), 80);
            string participantB = Clean(ReadString(typeInfo, value, "ParticipantB"), 80);

            string fingerprint = DuelV4ContractPolicy.Fingerprint(requestId, duelId, eventType, decision, outcome, winner, yielded, reasonToken);
            if (!Remember(fingerprint))
            {
                plugin.NoteDuelSocialDiagnostic(requestId, duelId, eventType, participantA + "/" + participantB, winner, 0, false, "duplicate_v4_event");
                return;
            }

            List<string> participants = new List<string>();
            AddUnique(participants, participantA);
            AddUnique(participants, participantB);
            if (participants.Count == 0 && !string.Equals(scope, "sim_vs_sim", StringComparison.OrdinalIgnoreCase))
            {
                AddUnique(participants, "player");
                AddUnique(participants, opponent);
            }
            else if (participants.Count == 1 && !string.Equals(scope, "sim_vs_sim", StringComparison.OrdinalIgnoreCase) &&
                     !Contains(participants, "player")) AddUnique(participants, "player");

            string correlation = duelId.Length > 0 ? duelId : requestId;
            string eventId = correlation.Length > 0 ? "duel-" + correlation :
                SocialEpisodeLedger.StableId("duel-v4|" + eventType + "|" + participantA + "|" + participantB + "|" + DateTime.UtcNow.Ticks.ToString());
            string playerName = plugin.GetPlayerDisplayName();
            string summary = BuildSummary(eventType, participantA, participantB, opponent, decision, outcome, winner, yielded, reasonToken, reason, playerName);
            int importance = Importance(eventType);
            bool terminal = eventType == "duel_completed" || eventType == "duel_cancelled" || eventType == "duel_declined" || eventType == "duel_request_rejected";
            List<string> tags = new List<string> { "duel", "competitiveness" };
            if (scope == "sim_vs_sim") tags.Add("sim_spar");
            if (source == "nemesis") tags.Add("nemesis");
            if (eventType == "duel_completed")
            {
                if (string.Equals(winner, "player", StringComparison.OrdinalIgnoreCase)) tags.Add("duel_win");
                else if (string.Equals(yielded, "player", StringComparison.OrdinalIgnoreCase)) tags.Add("duel_loss");
            }
            float conflict = reasonToken == "hostile_interruption" ? .45f : eventType == "duel_declined" || eventType == "duel_request_rejected" ? .20f : .15f;
            List<SimSnapshot> witnesses = plugin.GetLocalSocialWitnesses();
            SocialEpisodeRecord episode = plugin.RecordLivingSocialEpisode("duel", correlation, eventId, summary, participants, string.Empty,
                importance, tags, 0f, conflict, terminal && importance >= 45, witnesses);
            int knownByCount = episode == null || episode.KnownBy == null ? 0 : episode.KnownBy.Count;
            int stored = 0;
            if (episode != null && eventType == "duel_completed")
                stored = plugin.PersistVerifiedEpisodeMemory(episode, witnesses, "practice_duel", "practice_duel_result");
            plugin.NoteDuelSocialDiagnostic(requestId, duelId, eventType, participantA + "/" + participantB, winner,
                knownByCount, episode != null, episode == null ? "episode_rejected" : (stored > 0 ? "none" : "memory_not_stored"));

            VerifiedDuelEvent routed;
            if (VerifiedDuelEvent.TryCreate(eventType, opponent, scope, decision, outcome, winner, yielded, reasonToken, reason, out routed))
                DuelSocialIntegration.Handle(plugin, routed, "v4-semantic");
        }

        private static bool IsAllowed(string type)
        {
            return type == "duel_requested" || type == "duel_request_rejected" || type == "duel_challenge" ||
                   type == "duel_accepted" || type == "duel_declined" || type == "duel_started" ||
                   type == "duel_completed" || type == "duel_cancelled";
        }

        private static int Importance(string type)
        {
            if (type == "duel_completed") return 70;
            if (type == "duel_started") return 38;
            if (type == "duel_cancelled") return 32;
            if (type == "duel_declined" || type == "duel_request_rejected") return 26;
            if (type == "duel_accepted") return 24;
            if (type == "duel_challenge") return 20;
            return 15;
        }

        internal static string BuildSummary(string type, string a, string b, string opponent, string decision,
            string outcome, string winner, string yielded, string reasonToken, string reason, string playerName)
        {
            string displayPlayer = string.IsNullOrWhiteSpace(playerName) ? "the player" : playerName.Trim();
            string left = a.Length > 0 ? (string.Equals(a, "player", StringComparison.OrdinalIgnoreCase) ? displayPlayer : a) : displayPlayer;
            string right = b.Length > 0 ? (string.Equals(b, "player", StringComparison.OrdinalIgnoreCase) ? displayPlayer : b) : (opponent.Length > 0 ? opponent : "a nearby Sim");
            if (type == "duel_requested") return "A Practice Duel request was recorded between " + left + " and " + right + ".";
            if (type == "duel_challenge") return left + " challenged " + right + " to a Practice Duel.";
            if (type == "duel_accepted") return right + " accepted a Practice Duel challenge from " + left + ".";
            if (type == "duel_declined") return right + " declined a Practice Duel challenge from " + left + (decision.Length == 0 ? "." : " (decision " + decision + ").");
            if (type == "duel_request_rejected") return "A Practice Duel request between " + left + " and " + right + " was rejected" + ReasonSuffix(reasonToken, reason) + ".";
            if (type == "duel_started") return left + " and " + right + " started a Practice Duel.";
            if (type == "duel_completed")
                return DuelV4ContractPolicy.BuildCompletedFact(a, b, opponent, winner, yielded, playerName);
            return "The Practice Duel between " + left + " and " + right + " was cancelled" + ReasonSuffix(reasonToken, reason) + ".";
        }

        private static string ReasonSuffix(string token, string reason)
        {
            if (token.Length > 0) return " (" + token + ")";
            if (reason.Length > 0) return " (" + reason + ")";
            return string.Empty;
        }

        private static bool Remember(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return true;
            lock (Gate)
            {
                if (Seen.Contains(key)) return false;
                Seen.Add(key); SeenOrder.Enqueue(key);
                while (SeenOrder.Count > 128) Seen.Remove(SeenOrder.Dequeue());
                return true;
            }
        }

        private static Type FindType(string fullName)
        {
            try
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    Assembly assembly = assemblies[i]; if (assembly == null) continue;
                    Type type = assembly.GetType(fullName, false); if (type != null) return type;
                }
            }
            catch { }
            return null;
        }

        private static string ReadString(Type type, object value, string name)
        {
            try
            {
                PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                return property == null ? string.Empty : Convert.ToString(property.GetValue(value, null)) ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private static void AddUnique(List<string> values, string value)
        {
            string clean = Clean(value, 80); if (clean.Length == 0 || Contains(values, clean)) return; values.Add(clean);
        }
        private static bool Contains(List<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++) if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        private static string Clean(string value, int max)
        {
            string clean = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',').Replace('=', ':').Trim();
            return clean.Length <= max ? clean : clean.Substring(0, max);
        }
        private static string Token(string value, int max)
        {
            string clean = Clean(value, max).ToLowerInvariant(); char[] chars = clean.ToCharArray(); int count = 0;
            for (int i = 0; i < chars.Length && count < max; i++) if (char.IsLetterOrDigit(chars[i]) || chars[i] == '_' || chars[i] == '-') chars[count++] = chars[i];
            return new string(chars, 0, count);
        }
    }
}
