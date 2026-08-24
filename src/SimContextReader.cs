using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ErenshorDeepSims
{
    internal static class SimContextReader
    {
        private static readonly Dictionary<string, int> GuildIdBySim = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> GuildNameById = new Dictionary<int, string>();
        private static DateTime _guildCacheUntilUtc = DateTime.MinValue;
        private static readonly Dictionary<string, bool> FriendBySim = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static DateTime _friendCacheUntilUtc = DateTime.MinValue;
        private static int _friendCacheCharacterSlot = int.MinValue;

        private static readonly string[] NameMembers = new string[] { "NPCName", "Name", "SimName" };

        // A Sim's identity, personality and dialogue pools do not change while the object lives, but
        // reading them costs ~25 reflection lookups plus an enumeration of twelve dialogue lists. That
        // used to run for every Sim in the zone on every party poll. Cache the static half per object
        // and rebuild only the volatile fields (level, HP, scene) per snapshot.
        private sealed class SimProfile
        {
            public SimPlayer Owner;
            public string Name;
            public string Key;
            public string ClassName;
            public string CombatRole;
            public string Personality;
            public string PersonalityRaw;
            public int PersonalityCode;
            public string Bio;
            public int SkillLevel;
            public bool TypesInAllCaps;
            public bool TypesInAllLowers;
            public bool TypesInThirdPerson;
            public string RefersToSelfAs;
            public bool LovesEmojis;
            public bool Abbreviates;
            public int TypoRate;
            public string SignOff;
            public int Greed;
            public int Patience;
            public int GearChase;
            public bool Rival;
            public string TiedToSlot;
            public List<string> DialogueExamples;
        }

        private static readonly Dictionary<int, SimProfile> ProfileCache = new Dictionary<int, SimProfile>();
        private static FieldInfo[] _playerCandidateFields;
        private static readonly Dictionary<Type, FieldInfo[]> LanguageFieldsByType = new Dictionary<Type, FieldInfo[]>();
        private static readonly Dictionary<Type, PropertyInfo[]> LanguagePropertiesByType = new Dictionary<Type, PropertyInfo[]>();

        private static string _sceneName = string.Empty;
        private static int _sceneFrame = -1;

        // SceneManager.GetActiveScene().name allocates a string on every call; once per frame is plenty.
        private static string CurrentSceneName()
        {
            int frame = Time.frameCount;
            if (frame != _sceneFrame)
            {
                _sceneFrame = frame;
                try { _sceneName = SceneManager.GetActiveScene().name; }
                catch { }
            }
            return _sceneName;
        }

        // Cheap name read for callers that only need to identify a Sim, without paying for a full snapshot.
        internal static string ReadSimName(SimPlayer sim)
        {
            if (sim == null) return string.Empty;
            SimProfile cached;
            if (ProfileCache.TryGetValue(sim.GetInstanceID(), out cached) && cached != null && ReferenceEquals(cached.Owner, sim))
                return cached.Name;
            NPC npc = null;
            try { npc = sim.GetComponent<NPC>(); }
            catch { }
            string fallback = sim.gameObject == null ? string.Empty : sim.gameObject.name;
            return ReadString(npc, NameMembers, fallback);
        }

        private static SimProfile GetProfile(SimPlayer sim)
        {
            int id = sim.GetInstanceID();
            SimProfile cached;
            if (ProfileCache.TryGetValue(id, out cached) && cached != null && ReferenceEquals(cached.Owner, sim)) return cached;

            NPC npc = null;
            Stats stats = null;
            try { npc = sim.GetComponent<NPC>(); }
            catch { }
            try { stats = sim.GetComponent<Stats>(); }
            catch { }

            SimProfile profile = new SimProfile();
            profile.Owner = sim;
            profile.Name = ReadString(npc, NameMembers, sim.gameObject == null ? string.Empty : sim.gameObject.name);
            profile.ClassName = NormalizeClassName(ReadNestedString(stats, "CharacterClass", new string[] { "ClassName", "name" }, "unknown class"));
            profile.CombatRole = DescribeClassRole(profile.ClassName);

            object personalityValue = ReadMember(sim, "PersonalityType");
            if (personalityValue == null) personalityValue = ReadMember(sim, "Personality");
            profile.PersonalityRaw = personalityValue == null ? string.Empty : Convert.ToString(personalityValue);
            profile.PersonalityCode = ToInt(personalityValue, -1);

            profile.Bio = ReadString(sim, new string[] { "Bio" }, string.Empty);
            profile.SkillLevel = ReadInt(sim, new string[] { "SkillLevel" }, 0);
            profile.TypesInAllCaps = ReadBool(sim, new string[] { "TypesInAllCaps" }, false);
            profile.TypesInAllLowers = ReadBool(sim, new string[] { "TypesInAllLowers" }, false);
            profile.TypesInThirdPerson = ReadBool(sim, new string[] { "TypesInThirdPerson" }, false);
            profile.RefersToSelfAs = ReadString(sim, new string[] { "RefersToSelfAs" }, string.Empty);
            profile.LovesEmojis = ReadBool(sim, new string[] { "LovesEmojis" }, false);
            profile.Abbreviates = ReadBool(sim, new string[] { "Abbreviates" }, false);
            profile.TypoRate = ReadInt(sim, new string[] { "TypoRate" }, 0);
            profile.SignOff = ReadListString(sim, new string[] { "SignOffLine" });
            profile.Greed = ReadInt(sim, new string[] { "Greed" }, 0);
            profile.Patience = ReadInt(sim, new string[] { "Patience" }, 0);
            profile.GearChase = ReadInt(sim, new string[] { "GearChase" }, 0);
            profile.Rival = ReadBool(sim, new string[] { "Rival" }, false);
            profile.TiedToSlot = ReadString(sim, new string[] { "TiedToSlot" }, string.Empty);
            profile.DialogueExamples = ReadDialogueExamples(sim);
            profile.Key = Sanitize(profile.Name);
            profile.Personality = DescribeProfilePersonality(profile);

            // Zoning destroys and respawns Sims, so stale ids accumulate. Bound the cache rather than
            // tracking destruction; a rebuild is cheap relative to holding dead references forever.
            if (ProfileCache.Count > 512) ProfileCache.Clear();
            ProfileCache[id] = profile;
            return profile;
        }

        internal static List<SimSnapshot> GetActiveSims()
        {
            List<SimSnapshot> list = new List<SimSnapshot>();
            IEnumerable active = null;
            try
            {
                // Erenshor already maintains the live scene roster. Using it avoids a full
                // FindObjectsOfType scan through every spawned Sim, template, and scene object on
                // each party poll. The manager list can briefly be null during startup/zoning, so
                // retain the old scan only as a compatibility fallback.
                if (GameData.SimMngr != null)
                    active = ReadMember(GameData.SimMngr, "ActiveSimInstances") as IEnumerable;
            }
            catch { }

            SimPlayer[] fallback = null;
            if (active == null)
            {
                try { fallback = UnityEngine.Object.FindObjectsOfType<SimPlayer>(); }
                catch { return list; }
            }

            if (active != null)
            {
                foreach (object item in active)
                {
                    SimPlayer sim = item as SimPlayer;
                    if (sim == null || CoopCompatibility.IsRemoteCoopHuman(sim) || CoopCompatibility.IsRemoteCoopSim(sim)) continue;
                    SimSnapshot snapshot = BuildSnapshot(sim);
                    if (snapshot != null && !string.IsNullOrWhiteSpace(snapshot.Name)) list.Add(snapshot);
                }
                NativeRoleReader.ApplyTo(list);
                return list;
            }

            for (int i = 0; i < fallback.Length; i++)
            {
                SimPlayer sim = fallback[i];
                if (sim == null || CoopCompatibility.IsRemoteCoopHuman(sim) || CoopCompatibility.IsRemoteCoopSim(sim)) continue;
                SimSnapshot snapshot = BuildSnapshot(sim);
                if (snapshot != null && !string.IsNullOrWhiteSpace(snapshot.Name)) list.Add(snapshot);
            }
            NativeRoleReader.ApplyTo(list);
            return list;
        }

        internal static SimSnapshot FindActiveSim(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            List<SimSnapshot> sims = GetActiveSims();
            for (int i = 0; i < sims.Count; i++)
                if (string.Equals(sims[i].Name, name, StringComparison.OrdinalIgnoreCase)) return sims[i];
            return null;
        }

        internal static SimSnapshot BuildSnapshot(SimPlayer sim)
        {
            if (sim == null) return null;
            try { if (CoopCompatibility.IsRemoteCoopHuman(sim) || CoopCompatibility.IsRemoteCoopSim(sim)) return null; } catch { }
            SimProfile profile = GetProfile(sim);

            SimSnapshot s = new SimSnapshot();
            s.RuntimeSim = sim;
            s.PartyActorId = PartyActorIdentity.ForSim(sim);
            try
            {
                SimPlayerTracking tracking = sim.MySimTracking;
                if (tracking != null) s.NativeStableSimId = tracking.simIndex;
            }
            catch { s.NativeStableSimId = -1; }
            s.Scene = CurrentSceneName();

            // Static half, resolved once per Sim object.
            s.Name = profile.Name;
            s.Key = profile.Key;
            s.ClassName = profile.ClassName;
            s.CombatRole = profile.CombatRole;
            s.Personality = profile.Personality;
            s.PersonalityRaw = profile.PersonalityRaw;
            s.PersonalityCode = profile.PersonalityCode;
            s.Bio = profile.Bio;
            s.SkillLevel = profile.SkillLevel;
            s.TypesInAllCaps = profile.TypesInAllCaps;
            s.TypesInAllLowers = profile.TypesInAllLowers;
            s.TypesInThirdPerson = profile.TypesInThirdPerson;
            s.RefersToSelfAs = profile.RefersToSelfAs;
            s.LovesEmojis = profile.LovesEmojis;
            s.Abbreviates = profile.Abbreviates;
            s.TypoRate = profile.TypoRate;
            s.SignOff = profile.SignOff;
            s.Greed = profile.Greed;
            s.Patience = profile.Patience;
            s.GearChase = profile.GearChase;
            s.Rival = profile.Rival;
            s.TiedToSlot = profile.TiedToSlot;
            // Shared reference on purpose: consumers only read these, and reallocating the list per
            // snapshot was a measurable share of the party-poll cost.
            s.DialogueExamples = profile.DialogueExamples;
            s.AssignedRoles = new List<string>();

            // Volatile half, re-read every snapshot.
            Stats stats = null;
            try { stats = sim.GetComponent<Stats>(); }
            catch { }
            s.Level = ReadInt(stats, new string[] { "Level" }, ReadInt(sim, new string[] { "Level" }, 0));
            if (stats != null)
            {
                s.CurrentHp = ReadFloat(stats, new string[] { "CurrentHP", "HP", "CurHP" }, 0f);
                s.MaxHp = ReadFloat(stats, new string[] { "MaxHP", "MaximumHP", "HPMax", "BaseMaxHP" }, 0f);
                s.IsDead = s.CurrentHp <= 0f && s.MaxHp > 0f;
                s.HpPercent = s.MaxHp > 0f ? Math.Max(0f, Math.Min(100f, (s.CurrentHp / s.MaxHp) * 100f)) : -1f;
            }
            else s.HpPercent = -1f;

            PopulateGuild(s);
            PopulateFriendState(s);
            return s;
        }

        internal static WorldSnapshot BuildWorldSnapshot()
        {
            WorldSnapshot world = new WorldSnapshot();
            world.Scene = SceneManager.GetActiveScene().name;
            world.Player = GetPlayerSnapshot();
            world.Party = new List<SimSnapshot>();

            try
            {
                List<string> partyNames = PartyResolver.ResolvePartyMemberNames();
                for (int i = 0; i < partyNames.Count; i++)
                {
                    SimSnapshot sim = FindActiveSim(partyNames[i]);
                    if (sim != null) world.Party.Add(sim);
                }
            }
            catch { }
            return world;
        }

        internal static PlayerSnapshot GetPlayerSnapshot()
        {
            PlayerSnapshot p = new PlayerSnapshot();
            p.Name = GetPlayerName();
            p.ClassName = "unknown class";
            p.Level = 0;

            try
            {
                object control = GameData.PlayerControl;
                Component component = control as Component;
                Stats stats = component == null ? null : component.GetComponent<Stats>();
                if (stats != null)
                {
                    p.Level = ReadInt(stats, new string[] { "Level" }, 0);
                    p.ClassName = NormalizeClassName(ReadNestedString(stats, "CharacterClass", new string[] { "ClassName", "name" }, "unknown class"));
                    p.CurrentHp = ReadFloat(stats, new string[] { "CurrentHP", "HP", "CurHP" }, 0f);
                    p.MaxHp = ReadFloat(stats, new string[] { "MaxHP", "MaximumHP", "HPMax", "BaseMaxHP" }, 0f);
                    p.HpPercent = p.MaxHp > 0f ? Math.Max(0f, Math.Min(100f, (p.CurrentHp / p.MaxHp) * 100f)) : -1f;
                    p.IsDead = p.MaxHp > 0f && p.CurrentHp <= 0f;
                }
                else
                {
                    p.Level = ReadInt(control, new string[] { "Level", "PlayerLevel" }, 0);
                    p.ClassName = NormalizeClassName(ReadNestedString(control, "CharacterClass", new string[] { "ClassName", "name" }, "unknown class"));
                }
            }
            catch { }
            return p;
        }



        private static void PopulateFriendState(SimSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.Name)) return;
            RefreshFriendCacheIfNeeded();
            bool friend;
            if (FriendBySim.TryGetValue(snapshot.Name, out friend))
            {
                snapshot.FriendStateKnown = true;
                snapshot.IsFriend = friend;
            }
        }

        private static void RefreshFriendCacheIfNeeded()
        {
            int currentSlot = int.MinValue;
            try { currentSlot = GameData.CurrentCharacterSlot.index; }
            catch { currentSlot = int.MinValue; }

            DateTime now = DateTime.UtcNow;
            if (currentSlot == _friendCacheCharacterSlot && now < _friendCacheUntilUtc) return;
            _friendCacheUntilUtc = now.AddSeconds(5.0);
            _friendCacheCharacterSlot = currentSlot;
            FriendBySim.Clear();
            if (currentSlot < 0) return;

            try
            {
                if (GameData.SimMngr == null || GameData.SimMngr.Sims == null) return;
                List<SimPlayerTracking> sims = GameData.SimMngr.Sims;
                for (int i = 0; i < sims.Count; i++)
                {
                    SimPlayerTracking tracking = sims[i];
                    if (tracking == null) continue;
                    string name = ReadString(tracking, new string[] { "SimName", "Name" }, string.Empty);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    // Native /friend writes the acting character slot here and clears it to -1
                    // on unfriend; this is the same predicate used by current Party Tools/Nemesis.
                    FriendBySim[name] = tracking.FriendedBy == currentSlot && !tracking.IsGMCharacter;
                }
            }
            catch
            {
                FriendBySim.Clear();
            }
        }

        private static void PopulateGuild(SimSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.Name)) return;
            RefreshGuildCacheIfNeeded();
            int guildId;
            if (GuildIdBySim.TryGetValue(snapshot.Name, out guildId)) snapshot.GuildId = guildId;
            string guildName;
            if (snapshot.GuildId != 0 && GuildNameById.TryGetValue(snapshot.GuildId, out guildName)) snapshot.GuildName = guildName;
        }

        private static void RefreshGuildCacheIfNeeded()
        {
            DateTime now = DateTime.UtcNow;
            if (now < _guildCacheUntilUtc) return;
            _guildCacheUntilUtc = now.AddSeconds(10.0);
            GuildIdBySim.Clear();
            GuildNameById.Clear();

            try
            {
                object manager = null;
                try { manager = UnityEngine.Object.FindObjectOfType(typeof(SimPlayerMngr)); } catch { }
                object roster = ReadMember(manager, "Sims");
                IEnumerable enumerable = roster as IEnumerable;
                if (enumerable != null)
                {
                    foreach (object tracking in enumerable)
                    {
                        string simName = ReadString(tracking, new string[] { "SimName", "Name" }, string.Empty);
                        if (string.IsNullOrWhiteSpace(simName)) continue;
                        int id = ReadInt(tracking, new string[] { "GuildID", "GuildId", "Guild" }, 0);
                        GuildIdBySim[simName] = id;
                    }
                }
            }
            catch { }

            try
            {
                object guildManager = ReadStaticMember(typeof(GameData), new string[] { "GuildManager", "GuildMngr" });
                if (guildManager == null)
                {
                    try { guildManager = UnityEngine.Object.FindObjectOfType(typeof(GuildManager)); } catch { }
                }
                object guildList = ReadMember(guildManager, "Guilds");
                IEnumerable guilds = guildList as IEnumerable;
                if (guilds != null)
                {
                    foreach (object guild in guilds)
                    {
                        int id = ReadInt(guild, new string[] { "Id", "ID", "GuildID", "GuildId" }, -1);
                        string name = ReadString(guild, new string[] { "GuildName", "Name" }, string.Empty);
                        if (id >= 0 && !string.IsNullOrWhiteSpace(name)) GuildNameById[id] = name;
                        object membersObj = ReadMember(guild, "GuildMembers");
                        IEnumerable members = membersObj as IEnumerable;
                        if (members != null && id >= 0)
                        {
                            foreach (object member in members)
                            {
                                string memberName = member == null ? string.Empty : Convert.ToString(member);
                                if (!string.IsNullOrWhiteSpace(memberName) && !GuildIdBySim.ContainsKey(memberName)) GuildIdBySim[memberName] = id;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static object ReadStaticMember(Type type, string[] names)
        {
            if (type == null || names == null) return null;
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    FieldInfo field = type.GetField(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (field != null) return field.GetValue(null);
                    PropertyInfo prop = type.GetProperty(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (prop != null && prop.GetIndexParameters().Length == 0) return prop.GetValue(null, null);
                }
                catch { }
            }
            return null;
        }

        internal static string ApplyVanillaTypingStyle(SimSnapshot sim, string text)
        {
            if (sim == null || string.IsNullOrWhiteSpace(text)) return text;

            string styled = text;

            // Erenshor owns the authoritative typing-style implementation. It lives on the global
            // SimPlayerMngr and takes both the generated text and the Sim whose quirks should be
            // applied. The old reflection path looked for a one-argument method on SimPlayer or its
            // language object, so it never reached the game's lowercase/caps/third-person/typo/
            // emoticon logic in current builds.
            try
            {
                if (sim.RuntimeSim != null && GameData.SimMngr != null)
                {
                    string native = GameData.SimMngr.PersonalizeString(text, sim.RuntimeSim);
                    if (!string.IsNullOrWhiteSpace(native)) return ApplyDialoguePoolStyle(sim, native);
                }
            }
            catch { }

            object[] candidates = sim.RuntimeSim == null
                ? new object[0]
                : new object[] { sim.RuntimeSim, FindLanguageObject(sim.RuntimeSim) };
            for (int i = 0; i < candidates.Length; i++)
            {
                object target = candidates[i];
                if (target == null) continue;
                try
                {
                    MethodInfo[] methods = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    for (int m = 0; m < methods.Length; m++)
                    {
                        MethodInfo method = methods[m];
                        if (!string.Equals(method.Name, "PersonalizeString", StringComparison.Ordinal)) continue;
                        ParameterInfo[] ps = method.GetParameters();
                        if (ps.Length != 1 || ps[0].ParameterType != typeof(string) || method.ReturnType != typeof(string)) continue;
                        object result = method.Invoke(target, new object[] { styled });
                        string reflected = result as string;
                        if (!string.IsNullOrWhiteSpace(reflected)) return ApplyDialoguePoolStyle(sim, reflected);
                    }
                }
                catch { }
            }

            // Version-safe fallback for the two strongest visible quirks if Erenshor moves the
            // manager API again. This keeps Deep Sim text from visibly contradicting its profile.
            return ApplyDialoguePoolStyle(sim, styled);
        }

        internal static string DescribeHardOutputStyle(SimSnapshot sim)
        {
            if (sim == null) return "Use a short, casual MMO-chat fragment; never polished prose.";
            List<string> rules = new List<string>();
            if (sim.TypesInAllCaps) rules.Add("ALL CAPS");
            else if (sim.TypesInAllLowers || DialoguePoolPrefersLowercase(sim)) rules.Add("all lowercase");
            if (DialoguePoolAvoidsTerminalPunctuation(sim)) rules.Add("no terminal punctuation");
            int cap = DialoguePoolWordCap(sim);
            if (cap > 0) rules.Add("at most " + cap + " words");
            if (DialoguePoolUsesTextEmotes(sim)) rules.Add("old-school text emotes only when they fit");
            if (DialoguePoolUsesJoinedBangGreeting(sim)) rules.Add("native greetings join the player name after an exclamation mark");
            if (rules.Count == 0) rules.Add("short casual MMO-chat fragment, not polished prose");
            return string.Join("; ", rules.ToArray()) + ".";
        }

        internal static string DescribeNativeDialogueStyle(SimSnapshot sim)
        {
            return NativeDialogueStyle.Describe(sim);
        }

        private static string ApplyDialoguePoolStyle(SimSnapshot sim, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            string styled = text.Trim();
            if (sim.TypesInAllCaps) styled = styled.ToUpperInvariant();
            else if (sim.TypesInAllLowers || DialoguePoolPrefersLowercase(sim))
            {
                styled = styled.ToLowerInvariant();
            }
            // Preserve recognizable text-expression capitalization regardless of surrounding casing.
            styled = styled.Replace(":d", ":D").Replace(":p", ":P");

            int cap = DialoguePoolWordCap(sim);
            styled = LimitWords(styled, cap > 0 ? cap : 12);
            styled = ApplyObservedGreetingShape(sim, styled);
            if (DialoguePoolAvoidsTerminalPunctuation(sim)) styled = StripTerminalSentencePunctuation(styled);
            styled = SocialTemplates.ApplyOccasionalMmoTexture(sim, styled, DialoguePoolUsesTextEmotes(sim));
            return styled;
        }

        private static string ApplyObservedGreetingShape(SimSnapshot sim, string text)
        {
            return NativeDialogueStyle.ApplyGreetingShape(sim, text);
        }

        private static string StripTerminalSentencePunctuation(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            string trimmed = text.TrimEnd();
            while (trimmed.Length > 0 && (trimmed[trimmed.Length - 1] == '.' || trimmed[trimmed.Length - 1] == '!' || trimmed[trimmed.Length - 1] == '?' || trimmed[trimmed.Length - 1] == ',' || trimmed[trimmed.Length - 1] == ';'))
                trimmed = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();
            return trimmed;
        }

        private static bool DialoguePoolPrefersLowercase(SimSnapshot sim)
        {
            if (sim == null || sim.DialogueExamples == null || sim.DialogueExamples.Count < 2) return false;
            int samples = 0;
            int lowercase = 0;
            for (int i = 0; i < sim.DialogueExamples.Count; i++)
            {
                string line = sim.DialogueExamples[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                int letters = 0;
                int upper = 0;
                for (int c = 0; c < line.Length; c++)
                {
                    if (!char.IsLetter(line[c])) continue;
                    letters++;
                    if (char.IsUpper(line[c])) upper++;
                }
                if (letters == 0) continue;
                samples++;
                if (upper == 0) lowercase++;
            }
            return samples >= 2 && lowercase * 2 >= samples;
        }

        private static bool DialoguePoolAvoidsTerminalPunctuation(SimSnapshot sim)
        {
            if (sim == null || sim.DialogueExamples == null || sim.DialogueExamples.Count < 2) return false;
            int samples = 0;
            int bare = 0;
            for (int i = 0; i < sim.DialogueExamples.Count; i++)
            {
                string line = sim.DialogueExamples[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                line = line.Trim();
                if (line.Length == 0) continue;
                samples++;
                char last = line[line.Length - 1];
                if (last != '.' && last != '!' && last != '?') bare++;
            }
            return samples >= 2 && bare * 2 >= samples;
        }

        private static bool DialoguePoolUsesTextEmotes(SimSnapshot sim)
        {
            return NativeDialogueStyle.ObservedTextExpressions(sim).Count > 0;
        }

        private static bool DialoguePoolUsesJoinedBangGreeting(SimSnapshot sim)
        {
            return NativeDialogueStyle.UsesJoinedBangGreeting(sim);
        }

        private static int DialoguePoolWordCap(SimSnapshot sim)
        {
            if (sim == null || sim.DialogueExamples == null || sim.DialogueExamples.Count < 2) return 0;
            int samples = 0;
            int shortLines = 0;
            for (int i = 0; i < sim.DialogueExamples.Count; i++)
            {
                string line = sim.DialogueExamples[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                samples++;
                if (line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length <= 12) shortLines++;
            }
            return samples >= 2 && shortLines * 2 >= samples ? 18 : 0;
        }

        private static string LimitWords(string text, int maxWords)
        {
            if (string.IsNullOrWhiteSpace(text) || maxWords <= 0) return text;
            string[] words = text.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= maxWords) return text;
            return string.Join(" ", words, 0, maxWords);
        }

        internal static string GetPlayerName()
        {
            try
            {
                object playerControl = GameData.PlayerControl;
                string name = ReadString(playerControl, new string[] { "PlayerName", "MyName", "CharacterName", "CharName", "Name" }, string.Empty);
                if (!string.IsNullOrWhiteSpace(name) && !IsGenericPlayerName(name)) return name;

                Component playerComponent = playerControl as Component;
                if (playerComponent != null)
                {
                    NPC playerNpc = null;
                    try { playerNpc = playerComponent.GetComponent<NPC>(); } catch { }
                    name = ReadString(playerNpc, new string[] { "NPCName", "PlayerName", "CharacterName", "Name" }, string.Empty);
                    if (!string.IsNullOrWhiteSpace(name) && !IsGenericPlayerName(name)) return name;
                }
            }
            catch { }

            try
            {
                FieldInfo[] fields = _playerCandidateFields;
                if (fields == null)
                {
                    List<FieldInfo> candidates = new List<FieldInfo>();
                    FieldInfo[] discovered = typeof(GameData).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    for (int i = 0; i < discovered.Length; i++)
                        if (discovered[i].Name.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0) candidates.Add(discovered[i]);
                    fields = candidates.ToArray();
                    _playerCandidateFields = fields;
                }
                for (int i = 0; i < fields.Length; i++)
                {
                    object value = fields[i].GetValue(null);
                    string candidate = ReadString(value, new string[] { "PlayerName", "MyName", "CharacterName", "CharName" }, string.Empty);
                    if (!string.IsNullOrWhiteSpace(candidate) && !IsGenericPlayerName(candidate)) return candidate;
                }
            }
            catch { }
            return "the player";
        }

        private static bool IsGenericPlayerName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            string v = value.Trim();
            return string.Equals(v, "player", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(v, "the player", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(v, "playercontrol", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(v, "player(Clone)", StringComparison.OrdinalIgnoreCase);
        }

        internal static string DescribeTyping(SimSnapshot s)
        {
            List<string> parts = new List<string>();
            if (s.TypesInAllCaps) parts.Add("often types in all caps");
            if (s.TypesInAllLowers) parts.Add("often types in lowercase");
            if (s.TypesInThirdPerson) parts.Add("sometimes refers to self in third person");
            if (!string.IsNullOrWhiteSpace(s.RefersToSelfAs)) parts.Add("refers to self as '" + s.RefersToSelfAs + "'");
            if (s.LovesEmojis) parts.Add("likes old-school text emojis");
            if (s.TypoRate > 0) parts.Add("occasionally makes typos (game rate " + s.TypoRate + "/10)");
            if (parts.Count == 0) return "ordinary concise MMO chat";
            return string.Join(", ", parts.ToArray());
        }

        internal static string DescribeClassRole(string className)
        {
            string c = NormalizeClassName(className);
            if (string.Equals(c, "Paladin", StringComparison.OrdinalIgnoreCase)) return "tank / DPS";
            if (string.Equals(c, "Reaver", StringComparison.OrdinalIgnoreCase)) return "melee DPS / tank";
            if (string.Equals(c, "Druid", StringComparison.OrdinalIgnoreCase)) return "DPS / healer";
            if (string.Equals(c, "Arcanist", StringComparison.OrdinalIgnoreCase)) return "ranged DPS / crowd control";
            if (string.Equals(c, "Stormcaller", StringComparison.OrdinalIgnoreCase)) return "ranged DPS";
            if (string.Equals(c, "Windblade", StringComparison.OrdinalIgnoreCase)) return "melee DPS";
            return "unknown / flexible";
        }

        internal static string NormalizeClassName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown class";
            string clean = value.Trim();
            // Current Erenshor calls this class Windblade. Some runtime enums/save fields and older wiki
            // templates still expose the legacy name "Duelist". Keep the mod user-facing and prompt
            // context aligned with the current game terminology.
            if (string.Equals(clean, "Duelist", StringComparison.OrdinalIgnoreCase)) return "Windblade";
            return clean;
        }

        internal static string DescribePersonality(SimSnapshot s)
        {
            if (s == null) return "unknown";
            return DescribePersonalityCore(s.Rival, s.PersonalityCode, s.Bio, s.PersonalityRaw);
        }

        private static string DescribeProfilePersonality(SimProfile p)
        {
            if (p == null) return "unknown";
            return DescribePersonalityCore(p.Rival, p.PersonalityCode, p.Bio, p.PersonalityRaw);
        }

        private static string DescribePersonalityCore(bool rival, int personalityCode, string bio, string personalityRaw)
        {
            if (rival) return "rival / antagonistic";
            switch (personalityCode)
            {
                case 0:
                case 1: return "nice / friendly";
                case 2: return "tryhard / competitive";
                case 3: return "mean / blunt";
            }
            if (!string.IsNullOrWhiteSpace(bio)) return "use the bio and dialogue examples as the main personality guide";
            if (!string.IsNullOrWhiteSpace(personalityRaw)) return "unmapped game personality; infer tone from the bio and dialogue examples rather than guessing";
            return "unknown";
        }

        private static List<string> ReadDialogueExamples(SimPlayer sim)
        {
            List<string> result = new List<string>();
            if (sim == null) return result;
            object language = FindLanguageObject(sim);
            if (language == null) return result;

            string[] lists = new string[]
            {
                "Greetings", "ReturnGreeting", "LocalFriendHello", "InsultsFun", "RetortsFun", "Exclamations"
            };

            for (int i = 0; i < lists.Length && result.Count < 6; i++)
            {
                object value = ReadMember(language, lists[i]);
                IEnumerable enumerable = value as IEnumerable;
                if (enumerable == null || value is string) continue;
                foreach (object item in enumerable)
                {
                    string line = item == null ? string.Empty : Convert.ToString(item);
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    line = line.Trim(); // Resolve NN/II template tokens later with live player context.
                    if (!result.Contains(line)) result.Add(line);
                    break;
                }
            }
            return result;
        }

        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "sim";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            }
            return sb.Length == 0 ? "sim" : sb.ToString();
        }

        private static Component SafeGetComponent(Component source, Type type)
        {
            try { return source.GetComponent(type); }
            catch { return null; }
        }

        private static object FindLanguageObject(SimPlayer sim)
        {
            if (sim == null) return null;

            // Some Erenshor builds expose SimPlayerLanguage as a component; others may hold it
            // through a field/property. Try both so dialogue-style sampling is best-effort.
            Component component = SafeGetComponent(sim, typeof(SimPlayerLanguage));
            if (component != null) return component;

            try
            {
                Type t = sim.GetType();
                FieldInfo[] fields;
                if (!LanguageFieldsByType.TryGetValue(t, out fields))
                {
                    List<FieldInfo> matches = new List<FieldInfo>();
                    FieldInfo[] discovered = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    for (int i = 0; i < discovered.Length; i++)
                        if (discovered[i].FieldType.Name.IndexOf("SimPlayerLanguage", StringComparison.OrdinalIgnoreCase) >= 0) matches.Add(discovered[i]);
                    fields = matches.ToArray();
                    LanguageFieldsByType[t] = fields;
                }
                for (int i = 0; i < fields.Length; i++)
                {
                    object value = null;
                    try { value = fields[i].GetValue(sim); } catch { }
                    if (value != null && value.GetType().Name.IndexOf("SimPlayerLanguage", StringComparison.OrdinalIgnoreCase) >= 0) return value;
                }

                PropertyInfo[] props;
                if (!LanguagePropertiesByType.TryGetValue(t, out props))
                {
                    List<PropertyInfo> matches = new List<PropertyInfo>();
                    PropertyInfo[] discovered = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    for (int i = 0; i < discovered.Length; i++)
                        if (discovered[i].GetIndexParameters().Length == 0 && discovered[i].PropertyType.Name.IndexOf("SimPlayerLanguage", StringComparison.OrdinalIgnoreCase) >= 0) matches.Add(discovered[i]);
                    props = matches.ToArray();
                    LanguagePropertiesByType[t] = props;
                }
                for (int i = 0; i < props.Length; i++)
                {
                    if (props[i].GetIndexParameters().Length != 0) continue;
                    object value = null;
                    try { value = props[i].GetValue(sim, null); } catch { }
                    if (value != null && value.GetType().Name.IndexOf("SimPlayerLanguage", StringComparison.OrdinalIgnoreCase) >= 0) return value;
                }
            }
            catch { }
            return null;
        }

        internal static object ReadMember(object target, string name)
        {
            if (target == null || string.IsNullOrEmpty(name)) return null;
            Type t = target.GetType();
            FieldInfo field = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (field != null)
            {
                try { return field.GetValue(target); } catch { }
            }
            PropertyInfo prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (prop != null && prop.GetIndexParameters().Length == 0)
            {
                try { return prop.GetValue(target, null); } catch { }
            }
            return null;
        }

        internal static string ReadString(object target, string[] names, string fallback)
        {
            if (target == null) return fallback;
            for (int i = 0; i < names.Length; i++)
            {
                object value = ReadMember(target, names[i]);
                if (value == null) continue;
                string s = value as string;
                if (s != null && !string.IsNullOrWhiteSpace(s)) return s;
                if (!(value is IEnumerable))
                {
                    string converted = Convert.ToString(value);
                    if (!string.IsNullOrWhiteSpace(converted)) return converted;
                }
            }
            return fallback;
        }

        private static string ReadNestedString(object target, string member, string[] childNames, string fallback)
        {
            object child = ReadMember(target, member);
            return ReadString(child, childNames, fallback);
        }

        private static float ReadFloat(object target, string[] names, float fallback)
        {
            if (target == null) return fallback;
            for (int i = 0; i < names.Length; i++)
            {
                object value = ReadMember(target, names[i]);
                if (value == null) continue;
                try { return Convert.ToSingle(value); } catch { }
            }
            return fallback;
        }

        private static int ReadInt(object target, string[] names, int fallback)
        {
            if (target == null) return fallback;
            for (int i = 0; i < names.Length; i++)
            {
                object value = ReadMember(target, names[i]);
                if (value == null) continue;
                try { return Convert.ToInt32(value); } catch { }
            }
            return fallback;
        }

        private static int ToInt(object value, int fallback)
        {
            if (value == null) return fallback;
            try { return Convert.ToInt32(value); } catch { return fallback; }
        }

        private static bool ReadBool(object target, string[] names, bool fallback)
        {
            if (target == null) return fallback;
            for (int i = 0; i < names.Length; i++)
            {
                object value = ReadMember(target, names[i]);
                if (value == null) continue;
                try { return Convert.ToBoolean(value); } catch { }
            }
            return fallback;
        }

        private static string ReadListString(object target, string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                object value = ReadMember(target, names[i]);
                IEnumerable enumerable = value as IEnumerable;
                if (enumerable == null || value is string) continue;
                List<string> values = new List<string>();
                foreach (object item in enumerable)
                {
                    if (item != null) values.Add(Convert.ToString(item));
                    if (values.Count >= 4) break;
                }
                return string.Join(" | ", values.ToArray());
            }
            return string.Empty;
        }
    }
}
