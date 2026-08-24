using System;
using System.Collections.Generic;

namespace ErenshorDeepSims
{
    internal enum IdentityMemorySection
    {
        PinnedAuthored = 0,
        AuthoredHistory = 1,
        SharedHistory = 2,
        Learned = 3
    }

    internal sealed class IdentityFieldView
    {
        internal string Key = string.Empty;
        internal string Label = string.Empty;
        internal string DefaultText = string.Empty;
        internal string AuthoredText = string.Empty;
        internal string EffectiveText = string.Empty;
        internal IdentityValueSource Source = IdentityValueSource.None;
        internal string SourceLabel
        {
            get
            {
                if (Source == IdentityValueSource.AuthoredOverride) return "Authored";
                if (Source == IdentityValueSource.PersistedGeneratedDefault) return "Generated";
                if (Source == IdentityValueSource.DefaultTemplate) return "Generated template";
                return "Unknown";
            }
        }
    }

    internal sealed class IdentityMemoryView
    {
        internal string Id = string.Empty;
        internal IdentityMemorySection Section;
        internal string Text = string.Empty;
        internal string Type = string.Empty;
        internal string Source = string.Empty;
        internal string KnownBy = string.Empty;
        internal string Utc = string.Empty;
        internal bool Pinned;
        internal bool CanEdit;
        internal bool CanRemove;
        internal bool CanForget;
        internal bool CanCopyToAuthored;
    }

    internal sealed class IdentityEditorModel
    {
        internal string SimKey = string.Empty;
        internal string SimName = string.Empty;
        internal string VerifiedContext = string.Empty;
        internal string DefaultSummary = string.Empty;
        internal IdentityFieldView Personality;
        internal IdentityFieldView Background;
        internal IdentityFieldView Persona;
        internal IdentityFieldView Relationship;
        internal IdentityFieldView Wants;
        internal IdentityFieldView Cares;
        internal readonly List<IdentityMemoryView> Pinned = new List<IdentityMemoryView>();
        internal readonly List<IdentityMemoryView> AuthoredHistory = new List<IdentityMemoryView>();
        internal readonly List<IdentityMemoryView> SharedHistory = new List<IdentityMemoryView>();
        internal readonly List<IdentityMemoryView> Learned = new List<IdentityMemoryView>();
    }

    internal static class IdentityEditorModelBuilder
    {
        internal static IdentityEditorModel Build(SimSnapshot sim, SimMemory memory)
        {
            IdentityEditorModel model = new IdentityEditorModel();
            if (sim == null) return model;
            model.SimKey = sim.Key ?? string.Empty;
            model.SimName = sim.Name ?? string.Empty;
            DefaultIdentityProfile defaults = DefaultIdentityPolicy.Build(sim);
            model.DefaultSummary = defaults.Summary;

            AuthoredIdentityProfile authored = memory == null ? null : memory.AuthoredIdentity;
            if (authored == null) authored = new AuthoredIdentityProfile();
            authored.Normalize();
            model.Personality = Field("personality", "Personality", defaults.CorePersonality, authored.CorePersonality);
            string generatedBackground = memory == null ? string.Empty : (memory.GeneratedSimulatedPlayerBackground ?? string.Empty);
            model.Background = Field("background", "Simulated Player Background", generatedBackground, authored.PersonalBackground,
                string.IsNullOrWhiteSpace(authored.PersonalBackground) && !string.IsNullOrWhiteSpace(generatedBackground)
                    ? IdentityValueSource.PersistedGeneratedDefault : IdentityValueSource.DefaultTemplate);
            model.Persona = Field("persona", "Erenshor Persona", defaults.ErenshorPersona, authored.ErenshorPersona);
            model.Relationship = Field("relationship", "Relationship to Player", defaults.RelationshipToPlayer, authored.RelationshipToPlayer);
            string generatedWants = memory == null ? string.Empty : (memory.GeneratedLongTermWants ?? string.Empty);
            string generatedCares = memory == null ? string.Empty : (memory.GeneratedCaresAbout ?? string.Empty);
            model.Wants = Field("wants", "Long-term Wants / Concerns", generatedWants, authored.LongTermWants,
                string.IsNullOrWhiteSpace(authored.LongTermWants) && !string.IsNullOrWhiteSpace(generatedWants)
                    ? IdentityValueSource.PersistedGeneratedDefault : IdentityValueSource.DefaultTemplate);
            model.Cares = Field("cares", "Things This Character Cares About", generatedCares, authored.CaresAbout,
                string.IsNullOrWhiteSpace(authored.CaresAbout) && !string.IsNullOrWhiteSpace(generatedCares)
                    ? IdentityValueSource.PersistedGeneratedDefault : IdentityValueSource.DefaultTemplate);
            model.VerifiedContext = VerifiedContext(sim);

            AddRecords(model.Pinned, authored.PinnedMemories, IdentityMemorySection.PinnedAuthored);
            if (authored.SharedHistory != null)
            {
                for (int i = 0; i < authored.SharedHistory.Count; i++)
                {
                    StructuredMemoryRecord record = authored.SharedHistory[i];
                    if (record == null) continue;
                    record.Normalize();
                    IdentityMemorySection section = IsSharedHistoryRecord(record)
                        ? IdentityMemorySection.SharedHistory : IdentityMemorySection.AuthoredHistory;
                    AddRecord(section == IdentityMemorySection.SharedHistory ? model.SharedHistory : model.AuthoredHistory, record, section);
                }
            }
            if (memory != null) AddRecords(model.Learned, memory.StructuredMemories, IdentityMemorySection.Learned);
            return model;
        }

        private static IdentityFieldView Field(string key, string label, string defaultText, string authoredText,
            IdentityValueSource generatedSource = IdentityValueSource.DefaultTemplate)
        {
            IdentityFieldView view = new IdentityFieldView();
            view.Key = key;
            view.Label = label;
            view.DefaultText = defaultText ?? string.Empty;
            view.AuthoredText = authoredText == null ? string.Empty : authoredText.Trim();
            if (!string.IsNullOrWhiteSpace(view.AuthoredText))
            {
                view.Source = IdentityValueSource.AuthoredOverride;
                view.EffectiveText = view.AuthoredText;
            }
            else
            {
                view.Source = string.IsNullOrWhiteSpace(view.DefaultText) ? IdentityValueSource.None : generatedSource;
                view.EffectiveText = view.DefaultText;
            }
            return view;
        }

        private static void AddRecords(List<IdentityMemoryView> target, List<StructuredMemoryRecord> source, IdentityMemorySection section)
        {
            if (target == null || source == null) return;
            for (int i = source.Count - 1; i >= 0; i--) AddRecord(target, source[i], section);
        }

        private static void AddRecord(List<IdentityMemoryView> target, StructuredMemoryRecord record, IdentityMemorySection section)
        {
            if (target == null || record == null || string.IsNullOrWhiteSpace(record.Text)) return;
            record.Normalize();
            IdentityMemoryView view = new IdentityMemoryView();
            view.Id = record.Id ?? string.Empty;
            view.Section = section;
            view.Text = record.Text.Trim();
            view.Type = record.MemoryType ?? string.Empty;
            view.Source = FriendlySource(record, section);
            view.KnownBy = CompactKnownBy(record.KnownBy);
            view.Utc = record.Utc ?? string.Empty;
            view.Pinned = record.Pinned;
            view.CanEdit = section != IdentityMemorySection.Learned && record.Authored;
            view.CanRemove = section != IdentityMemorySection.Learned && record.Authored;
            view.CanForget = section == IdentityMemorySection.Learned && !record.Authored;
            view.CanCopyToAuthored = section == IdentityMemorySection.Learned && !record.Authored;
            target.Add(view);
        }

        internal static bool IsSharedHistoryRecord(StructuredMemoryRecord record)
        {
            if (record == null || !string.Equals(record.MemoryType, "authored_shared_history", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(record.Source, "player_authored_shared", StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrWhiteSpace(record.Id) && record.Id.StartsWith("shared-", StringComparison.OrdinalIgnoreCase)) return true;
            // Older /dsidentity add history records used authored_shared_history even though only one
            // Sim knew them. A true shared record contains player + at least two stable-key/name pairs.
            return record.KnownBy != null && record.KnownBy.Count >= 5;
        }

        private static string FriendlySource(StructuredMemoryRecord record, IdentityMemorySection section)
        {
            if (section == IdentityMemorySection.PinnedAuthored) return "Player authored / pinned";
            if (section == IdentityMemorySection.AuthoredHistory) return "Player authored / history";
            if (section == IdentityMemorySection.SharedHistory) return "Player authored / shared history";
            string source = record == null ? string.Empty : (record.Source ?? string.Empty);
            if (source.StartsWith("verified_event", StringComparison.OrdinalIgnoreCase)) return "Learned from verified gameplay";
            if (source.StartsWith("player_authored", StringComparison.OrdinalIgnoreCase)) return "Player authored";
            if (source.Length == 0) return "Learned structured memory";
            return "Learned / " + source;
        }

        private static string CompactKnownBy(List<string> knownBy)
        {
            if (knownBy == null || knownBy.Count == 0) return "this Sim";
            // IdentitySchema records are written as player + stable-key/name pairs. The editor shows
            // only the human-readable names; stable keys remain an internal ownership mechanism.
            List<string> clean = new List<string>();
            int start = 0;
            if (string.Equals(knownBy[0], "player", StringComparison.OrdinalIgnoreCase))
            {
                clean.Add("player");
                start = 2; // index 1 is the first stable key, index 2 its display name.
                for (int i = start; i < knownBy.Count && clean.Count < 8; i += 2)
                {
                    string name = knownBy[i] == null ? string.Empty : knownBy[i].Trim();
                    if (name.Length > 0) AddDisplayName(clean, name);
                }
                if (clean.Count > 1) return string.Join(", ", clean.ToArray());
            }
            for (int i = 0; i < knownBy.Count && clean.Count < 8; i++)
            {
                string value = knownBy[i] == null ? string.Empty : knownBy[i].Trim();
                if (value.Length == 0) continue;
                if (value.IndexOf("key", StringComparison.OrdinalIgnoreCase) >= 0 && value.IndexOf(' ') < 0) continue;
                AddDisplayName(clean, value);
            }
            return clean.Count == 0 ? "this Sim" : string.Join(", ", clean.ToArray());
        }

        private static void AddDisplayName(List<string> clean, string value)
        {
            if (clean == null || string.IsNullOrWhiteSpace(value)) return;
            for (int j = 0; j < clean.Count; j++) if (string.Equals(clean[j], value, StringComparison.OrdinalIgnoreCase)) return;
            clean.Add(value.Trim());
        }

        private static string VerifiedContext(SimSnapshot sim)
        {
            List<string> parts = new List<string>();
            parts.Add("L" + sim.Level + " " + (string.IsNullOrWhiteSpace(sim.ClassName) ? "unknown class" : sim.ClassName));
            if (!string.IsNullOrWhiteSpace(sim.Scene)) parts.Add("zone: " + sim.Scene);
            if (!string.IsNullOrWhiteSpace(sim.GuildName)) parts.Add("guild: " + sim.GuildName);
            if (sim.FriendStateKnown) parts.Add(sim.IsFriend ? "Friend: yes (native current-character roster)" : "Friend: no (native current-character roster)");
            else parts.Add("Friend: unknown");
            if (sim.RoleAssignmentsKnown)
                parts.Add("roles: " + (sim.AssignedRoles == null || sim.AssignedRoles.Count == 0 ? "none" : string.Join("/", sim.AssignedRoles.ToArray())));
            return string.Join("  |  ", parts.ToArray());
        }
    }

    // Pure draft state used by the retained UI and deterministic tests. Empty authored text means
    // "use the generated default"; defaults themselves never get copied into persistent authored fields.
    internal sealed class IdentityEditorDraft
    {
        private readonly Dictionary<string, string> _original = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal IdentityEditorDraft(IdentityEditorModel model)
        {
            Capture(model == null ? null : model.Personality);
            Capture(model == null ? null : model.Background);
            Capture(model == null ? null : model.Persona);
            Capture(model == null ? null : model.Relationship);
            Capture(model == null ? null : model.Wants);
            Capture(model == null ? null : model.Cares);
        }

        internal string Get(string field)
        {
            string value;
            return _current.TryGetValue(field ?? string.Empty, out value) ? value : string.Empty;
        }

        internal void Set(string field, string value)
        {
            if (string.IsNullOrWhiteSpace(field)) return;
            _current[field.Trim()] = value == null ? string.Empty : value;
        }

        internal void ResetField(string field)
        {
            if (string.IsNullOrWhiteSpace(field)) return;
            _current[field.Trim()] = string.Empty;
        }

        internal void ResetAll()
        {
            List<string> keys = new List<string>(_current.Keys);
            for (int i = 0; i < keys.Count; i++) _current[keys[i]] = string.Empty;
        }

        internal void Cancel()
        {
            _current.Clear();
            foreach (KeyValuePair<string, string> pair in _original) _current[pair.Key] = pair.Value;
        }

        internal AuthoredIdentityProfile ToProfile()
        {
            AuthoredIdentityProfile result = new AuthoredIdentityProfile();
            result.CorePersonality = Get("personality");
            result.PersonalBackground = Get("background");
            result.ErenshorPersona = Get("persona");
            result.RelationshipToPlayer = Get("relationship");
            result.LongTermWants = Get("wants");
            result.CaresAbout = Get("cares");
            result.Normalize();
            return result;
        }

        private void Capture(IdentityFieldView field)
        {
            if (field == null || string.IsNullOrWhiteSpace(field.Key)) return;
            string value = field.AuthoredText ?? string.Empty;
            _original[field.Key] = value;
            _current[field.Key] = value;
        }
    }
}
