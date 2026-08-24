using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace ErenshorDeepSims
{
    // Optional, read-only Campmaster bridge. It binds only the small public compatibility API and
    // never takes a compile-time dependency on Campmaster or mutates either mod's gameplay state.
    internal sealed class CampmasterCompatibility
    {
        private const string ApiTypeName = "ErenshorCampmaster.CampmasterApi";
        private const int MinimumSchemaVersion = 2;
        private readonly IDeepSimsLog _log;
        private bool _resolved;
        private DateTime _nextResolveUtc = DateTime.MinValue;
        private bool _healthy;
        private bool _warned;
        private bool _socialWarned;
        private Type _apiType;
        private PropertyInfo _activeProperty;
        private PropertyInfo _relaxActiveProperty;
        private PropertyInfo _latestSequenceProperty;
        private FieldInfo _schemaVersionField;
        private MethodInfo _eventsAfterMethod;
        private MethodInfo _socialContextMethod;
        private long _lastSequence;
        private DateTime _nextPollUtc = DateTime.MinValue;

        internal CampmasterCompatibility(IDeepSimsLog log)
        {
            _log = log;
        }

        internal bool Healthy
        {
            get
            {
                EnsureResolved();
                return _healthy;
            }
        }

        internal bool SocialContextAvailable
        {
            get { EnsureResolved(); return _healthy && _socialContextMethod != null; }
        }

        internal bool IsHuntCampActive
        {
            get
            {
                EnsureResolved();
                if (!_healthy || _activeProperty == null) return false;
                try { return (bool)_activeProperty.GetValue(null, null); }
                catch (Exception ex) { MarkUnhealthy("Campmaster active-state read failed", ex); return false; }
            }
        }

        internal bool IsRelaxActive
        {
            get
            {
                EnsureResolved();
                if (!_healthy || _relaxActiveProperty == null) return false;
                try { return (bool)_relaxActiveProperty.GetValue(null, null); }
                catch (Exception ex) { MarkUnhealthy("Campmaster Relax-state read failed", ex); return false; }
            }
        }

        internal CampmasterSocialContext ReadSocialContext()
        {
            EnsureResolved();
            if (!_healthy || _socialContextMethod == null) return null;
            try
            {
                IDictionary row = _socialContextMethod.Invoke(null, null) as IDictionary;
                if (row == null) return null;
                CampmasterSocialContext value = new CampmasterSocialContext();
                value.ActivityState = Read(row, "activityState");
                value.ActivityReason = Read(row, "activityReason");
                value.Zone = Read(row, "zone");
                value.Mode = Read(row, "mode");
                value.Recognition = Read(row, "recognition");
                value.GameplayReady = ReadBool(row, "gameplayReady");
                value.AutoRelaxActive = ReadBool(row, "autoRelaxActive") == true;
                value.SecondsStationary = ReadDouble(row, "secondsStationary");
                value.SecondsOutOfCombat = ReadDouble(row, "secondsOutOfCombat");
                value.SecondsSinceMeaningfulGameplay = ReadDouble(row, "secondsSinceMeaningfulGameplay");
                return value;
            }
            catch (Exception ex) { MarkUnhealthy("Campmaster social-context read failed", ex); return null; }
        }

        internal List<CampmasterSemanticEvent> Poll(DateTime now)
        {
            List<CampmasterSemanticEvent> result = new List<CampmasterSemanticEvent>();
            EnsureResolved();
            if (!_healthy || now < _nextPollUtc) return result;
            _nextPollUtc = now.AddSeconds(1.0);

            long latest = ReadLatestSequence();
            if (!_healthy) return result;
            if (latest <= _lastSequence) return result;

            try
            {
                object raw = _eventsAfterMethod.Invoke(null, new object[] { _lastSequence });
                IEnumerable rows = raw as IEnumerable;
                if (rows == null)
                {
                    _lastSequence = latest;
                    return result;
                }

                long highest = _lastSequence;
                foreach (object rowObject in rows)
                {
                    IDictionary row = rowObject as IDictionary;
                    if (row == null) continue;
                    CampmasterSemanticEvent evt = Parse(row);
                    if (evt == null || evt.Sequence <= _lastSequence) continue;
                    result.Add(evt);
                    if (evt.Sequence > highest) highest = evt.Sequence;
                }
                _lastSequence = Math.Max(highest, latest);
            }
            catch (Exception ex)
            {
                MarkUnhealthy("Campmaster event read failed", ex);
            }
            return result;
        }

        private void EnsureResolved()
        {
            if (_healthy || (_resolved && _apiType != null)) return;
            DateTime now = DateTime.UtcNow;
            if (now < _nextResolveUtc) return;
            _nextResolveUtc = now.AddSeconds(5.0);
            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type found = null;
                    try { found = assembly.GetType(ApiTypeName, false); }
                    catch { }
                    if (found == null) continue;
                    _apiType = found;
                    break;
                }
                if (_apiType == null)
                {
                    // Optional dependency may load after Deep Sims; retry at low frequency rather
                    // than turning plugin load order into a hard requirement.
                    _resolved = false;
                    return;
                }
                _resolved = true;

                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                _activeProperty = _apiType.GetProperty("IsHuntCampActive", flags);
                _relaxActiveProperty = _apiType.GetProperty("IsRelaxActive", flags);
                _latestSequenceProperty = _apiType.GetProperty("LatestEventSequence", flags);
                _schemaVersionField = _apiType.GetField("SchemaVersion", flags);
                _eventsAfterMethod = _apiType.GetMethod("GetEventsAfter", flags, null, new Type[] { typeof(long) }, null);
                _socialContextMethod = _apiType.GetMethod("GetCurrentSocialContext", flags, null, Type.EmptyTypes, null);

                int schema = 0;
                if (_schemaVersionField != null)
                {
                    object value = _schemaVersionField.GetValue(null);
                    if (value != null) schema = Convert.ToInt32(value);
                }
                if (schema < MinimumSchemaVersion || _activeProperty == null || _activeProperty.PropertyType != typeof(bool) ||
                    (_relaxActiveProperty != null && _relaxActiveProperty.PropertyType != typeof(bool)) ||
                    _latestSequenceProperty == null || _eventsAfterMethod == null)
                {
                    WarnOnce("Campmaster compatibility API was found but its schema/member shape is unsupported; legacy Deep Sims camp behavior remains enabled.");
                    return;
                }

                _healthy = true;
                if (_socialContextMethod == null && !_socialWarned)
                {
                    _socialWarned = true;
                    if (_log != null) _log.LogWarning("Campmaster social-context API is unavailable; Deep Sims standalone downtime detection remains enabled.");
                }
                // Never replay retained Campmaster history on bind. Current active state is queried
                // separately, and only events emitted after this boundary are consumed.
                _lastSequence = ReadLatestSequence();
            }
            catch (Exception ex)
            {
                MarkUnhealthy("Campmaster compatibility binding failed", ex);
            }
        }

        private long ReadLatestSequence()
        {
            if (!_healthy || _latestSequenceProperty == null) return 0L;
            try
            {
                object value = _latestSequenceProperty.GetValue(null, null);
                return value == null ? 0L : Convert.ToInt64(value);
            }
            catch (Exception ex)
            {
                MarkUnhealthy("Campmaster sequence read failed", ex);
                return 0L;
            }
        }

        private static CampmasterSemanticEvent Parse(IDictionary row)
        {
            long sequence;
            if (!long.TryParse(Read(row, "sequence"), out sequence) || sequence <= 0) return null;
            string type = Read(row, "type");
            if (string.IsNullOrWhiteSpace(type)) return null;
            return new CampmasterSemanticEvent
            {
                Sequence = sequence,
                Type = type.Trim(),
                EventId = Read(row, "eventId"),
                SessionId = Read(row, "sessionId"),
                Zone = Read(row, "zone"),
                Detail = Read(row, "detail")
            };
        }

        private static string Read(IDictionary row, string key)
        {
            if (row == null || string.IsNullOrWhiteSpace(key)) return string.Empty;
            foreach (DictionaryEntry pair in row)
            {
                string candidate = pair.Key == null ? string.Empty : pair.Key.ToString();
                if (!string.Equals(candidate, key, StringComparison.Ordinal)) continue;
                return pair.Value == null ? string.Empty : pair.Value.ToString();
            }
            return string.Empty;
        }

        private static bool? ReadBool(IDictionary row, string key)
        {
            bool parsed;
            return bool.TryParse(Read(row, key), out parsed) ? (bool?)parsed : null;
        }

        private static double ReadDouble(IDictionary row, string key)
        {
            double parsed;
            return double.TryParse(Read(row, key), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out parsed) ? Math.Max(0.0, parsed) : 0.0;
        }

        private void MarkUnhealthy(string prefix, Exception ex)
        {
            _healthy = false;
            _resolved = false;
            _apiType = null;
            _nextResolveUtc = DateTime.UtcNow.AddSeconds(5.0);
            string detail = ex == null ? string.Empty : ": " + DiagnosticPrivacy.ExceptionType(ex);
            WarnOnce(prefix + detail + ". Legacy Deep Sims camp behavior remains enabled.");
        }

        private void WarnOnce(string message)
        {
            if (_warned) return;
            _warned = true;
            if (_log != null) _log.LogWarning(message);
        }
    }

    internal sealed class CampmasterSemanticEvent
    {
        internal long Sequence;
        internal string Type;
        internal string EventId;
        internal string SessionId;
        internal string Zone;
        internal string Detail;
    }

    internal sealed class CampmasterSocialContext
    {
        internal string ActivityState;
        internal string ActivityReason;
        internal string Zone;
        internal string Mode;
        internal string Recognition;
        internal bool? GameplayReady;
        internal bool AutoRelaxActive;
        internal double SecondsStationary;
        internal double SecondsOutOfCombat;
        internal double SecondsSinceMeaningfulGameplay;
    }
}
