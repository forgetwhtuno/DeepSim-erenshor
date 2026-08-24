using System;
using System.IO;
using System.Text;

namespace ErenshorDeepSims
{
    [Serializable]
    internal sealed class IdentityProfileTransferDocument
    {
        public int SchemaVersion;
        public string SimName;
        public string ExportedUtc;
        public string Personality;
        public string PersonalBackground;
        public string ErenshorPersona;
        public string RelationshipToPlayer;
        public string LongTermWants;
        public string CaresAbout;
    }

    // Explicit, local-only authored biography transfer. Conversation history, learned memory,
    // prompt captures, raw diagnostics, machine paths, and shared KnownBy records are deliberately
    // excluded from this small schema.
    internal static class IdentityProfileTransfer
    {
        internal const int CurrentSchemaVersion = 2;

        internal static bool TryExport(SimSnapshot sim, AuthoredIdentityProfile profile, out string relativePath, out string result)
        {
            relativePath = string.Empty;
            result = string.Empty;
            if (sim == null || string.IsNullOrWhiteSpace(sim.Name)) { result = "No selected Sim."; return false; }
            if (profile == null) { result = "No authored identity profile is available."; return false; }
            profile.Normalize();
            IdentityProfileTransferDocument doc = new IdentityProfileTransferDocument();
            doc.SchemaVersion = CurrentSchemaVersion;
            doc.SimName = IdentitySchema.Bound(sim.Name, 120);
            doc.ExportedUtc = DateTime.UtcNow.ToString("o");
            doc.Personality = profile.CorePersonality ?? string.Empty;
            doc.PersonalBackground = profile.PersonalBackground ?? string.Empty;
            doc.ErenshorPersona = profile.ErenshorPersona ?? string.Empty;
            doc.RelationshipToPlayer = profile.RelationshipToPlayer ?? string.Empty;
            doc.LongTermWants = profile.LongTermWants ?? string.Empty;
            doc.CaresAbout = profile.CaresAbout ?? string.Empty;
            AuthoredIdentityProfile ignored;
            if (!TryValidate(doc, out ignored, out result)) return false;

            try
            {
                string dir = Path.Combine(DeepSimsPaths.ExportDirectory, "IdentityProfiles");
                Directory.CreateDirectory(dir);
                string file = "Identity-" + SafeFileName(sim.Name) + ".json";
                string path = Path.Combine(dir, file);
                JsonUtil.WriteFile(path, doc);
                relativePath = Path.Combine("DeepSims", "Exports", "IdentityProfiles", file).Replace('\\', '/');
                result = "Exported authored biography only to " + relativePath + ".";
                return true;
            }
            catch (Exception ex)
            {
                result = "Identity export failed (" + ex.GetType().Name + ").";
                return false;
            }
        }

        internal static bool TryImport(SimSnapshot sim, out AuthoredIdentityProfile profile, out string result)
        {
            profile = null;
            result = string.Empty;
            if (sim == null || string.IsNullOrWhiteSpace(sim.Name)) { result = "No selected Sim."; return false; }
            try
            {
                string path = Path.Combine(DeepSimsPaths.ExportDirectory, "IdentityProfiles", "Identity-" + SafeFileName(sim.Name) + ".json");
                if (!File.Exists(path))
                {
                    result = "No local identity import file exists for " + sim.Name + " in DeepSims/Exports/IdentityProfiles/.";
                    return false;
                }
                IdentityProfileTransferDocument doc = JsonUtil.ReadFile<IdentityProfileTransferDocument>(path);
                return TryValidate(doc, out profile, out result);
            }
            catch (Exception ex)
            {
                result = "Identity import rejected (" + ex.GetType().Name + ").";
                profile = null;
                return false;
            }
        }

        internal static bool TryValidate(IdentityProfileTransferDocument doc, out AuthoredIdentityProfile profile, out string result)
        {
            profile = null;
            result = string.Empty;
            if (doc == null) { result = "Identity import is empty or malformed."; return false; }
            if (doc.SchemaVersion != CurrentSchemaVersion)
            {
                result = "Unsupported identity schema version " + doc.SchemaVersion + ".";
                return false;
            }
            if (!ValidLength(doc.Personality, 900) || !ValidLength(doc.PersonalBackground, 900) ||
                !ValidLength(doc.ErenshorPersona, 900) || !ValidLength(doc.RelationshipToPlayer, 900) ||
                !ValidLength(doc.LongTermWants, 900) || !ValidLength(doc.CaresAbout, 900))
            {
                result = "Identity import contains a field longer than 900 characters.";
                return false;
            }
            profile = new AuthoredIdentityProfile();
            profile.CorePersonality = IdentitySchema.Bound(doc.Personality, 900);
            profile.PersonalBackground = IdentitySchema.Bound(doc.PersonalBackground, 900);
            profile.ErenshorPersona = IdentitySchema.Bound(doc.ErenshorPersona, 900);
            profile.RelationshipToPlayer = IdentitySchema.Bound(doc.RelationshipToPlayer, 900);
            profile.LongTermWants = IdentitySchema.Bound(doc.LongTermWants, 900);
            profile.CaresAbout = IdentitySchema.Bound(doc.CaresAbout, 900);
            profile.Normalize();
            result = "Validated local authored identity profile. Shared history, learned memory, and conversations were not imported.";
            return true;
        }

        private static bool ValidLength(string value, int max) { return value == null || value.Length <= max; }

        private static string SafeFileName(string value)
        {
            StringBuilder sb = new StringBuilder();
            string source = string.IsNullOrWhiteSpace(value) ? "sim" : value.Trim();
            for (int i = 0; i < source.Length && sb.Length < 80; i++)
            {
                char c = source[i];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else if (char.IsWhiteSpace(c)) sb.Append('-');
            }
            return sb.Length == 0 ? "sim" : sb.ToString();
        }
    }
}
