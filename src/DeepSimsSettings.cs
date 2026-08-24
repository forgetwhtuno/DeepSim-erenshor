using System;
using Lunaris.Config;

namespace ErenshorDeepSims
{
    // Loader-neutral ConfigEntry-style shim. Keeping the Value surface makes the Lunaris migration
    // mechanical and lets the existing domain code retain its battle-tested config access patterns.
    internal sealed class DeepSimsConfigEntry<T>
    {
        private readonly Func<T> _get;
        private readonly Action<T> _set;

        internal DeepSimsConfigEntry(Func<T> get, Action<T> set)
        {
            _get = get;
            _set = set;
        }

        internal T Value
        {
            get { return _get(); }
            set { _set(value); }
        }
    }

    internal sealed class DeepSimsSettings
    {
        public DeepSimsSettings() { }

        [ConfigHidden]
        [Config("ConfigVersion", "General", "Internal. Tracks which one-time default migrations have already been applied. Do not edit.")]
        public int ConfigVersion = 0;

        [Config("Enabled", "General", "Enable the Deep Sims enhancement layer.")]
        public bool Enabled = true;

        [Config("HostAuthority", "Co-op", "When Erenshor COOP is installed, enable Deep Sims only on the host PC. Leave false on every client; this mod never requires clients to install Ollama.")]
        public bool CoopHostAuthority = false;

        [Config("FailureCooldownSeconds", "Ollama", "After Ollama is unavailable, wait this many seconds before another Deep Sims request. Gameplay and vanilla chat continue normally.")]
        public int OllamaFailureCooldownSeconds = 60;

        [Config("MaxDeepSims", "General", "Maximum Deep Sims when WholePartyDeepSims is disabled. Hard-capped at 5.")]
        public int MaxDeepSims = 5;

        [Config("WholePartyDeepSims", "General", "Enhance every Sim in the current normal party, up to a hard cap of 5. This prevents a full raid from becoming Deep Sims.")]
        public bool WholePartyDeepSims = true;

        [Config("PartyPollSeconds", "General", "How often DeepSims re-checks current party membership. Deep Sims keeps the cached scene scan while building fresh per-party-member snapshots at prompt time; no extra FindObjectsOfType scan is added.")]
        public float PartyPollSeconds = 3.0f;

        [Config("ManualSlots", "General", "Diagnostic fallback: comma-separated Sim names. Leave blank for automatic party detection.")]
        public string ManualSlots = "";

        [Config("HybridWhispers", "Dialogue", "Let recognizable vanilla gameplay whisper intents pass to Erenshor; route other whispers to the LLM.")]
        public bool HybridWhispers = true;

        [Config("ApplyVanillaTypingStyle", "Dialogue", "Try to pass LLM replies through the Sim's own PersonalizeString typing quirks.")]
        public bool ApplyVanillaTyping = true;

        [Config("MaxReplyCharacters", "Dialogue", "Hard cap on a Deep Sim reply.")]
        public int MaxReplyCharacters = 280;

        [Config("MaxHistoryMessages", "Dialogue", "Recent conversation messages kept per Sim.")]
        public int MaxHistoryMessages = 14;

        [Config("Endpoint", "Ollama", "Ollama /api/chat endpoint.")]
        public string Endpoint = "http://localhost:11434/api/chat";

        [Config("Model", "Ollama", "The ONE local Ollama model used for every Deep Sims request (classification, replies, retries, and fallbacks). Recommended: qwen3.5:4b.")]
        public string Model = "qwen3.5:4b";

        [Config("TimeoutSeconds", "Ollama", "Maximum wait for one local model reply.")]
        public int TimeoutSeconds = 45;

        [Config("ContextWindow", "Ollama", "Requested local model context window. 2048 is the lightweight default for short MMO chat; increase if you use a larger custom model/prompt.")]
        public int ContextWindow = 2048;

        [Config("KeepAlive", "Ollama", "How long Ollama keeps the shared model loaded.")]
        public string KeepAlive = "30m";

        [Config("Enabled", "Wiki", "Allow DeepSims to query the Erenshor community wiki over HTTPS.")]
        public bool WikiEnabled = true;

        [Config("AutoLookup", "Wiki", "Automatically query the wiki for clear game-knowledge questions sent to a Deep Sim.")]
        public bool AutoWikiLookup = true;

        [Config("ApiUrl", "Wiki", "MediaWiki Action API endpoint used for Erenshor lookups.")]
        public string WikiApiUrl = "https://erenshor.wiki.gg/api.php";

        [Config("TimeoutSeconds", "Wiki", "Maximum wait for an Erenshor wiki request.")]
        public int WikiTimeoutSeconds = 8;

        [Config("MaxExtractCharacters", "Wiki", "Maximum wiki/news extract supplied to the local model.")]
        public int WikiMaxChars = 1200;

        [Config("OfficialSteamNews", "Knowledge", "Use Valve's public Steam-news API for current Erenshor patch/update/expansion questions.")]
        public bool OfficialNewsEnabled = true;

        [Config("SteamNewsApiUrl", "Knowledge", "Valve ISteamNews endpoint for Erenshor (AppID 2382520).")]
        public string OfficialNewsApiUrl = "https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid=2382520&count=12&maxlength=1200";

        [Config("Enabled", "ExternalNews", "Allow DeepSims to look up recent real-world news when a player explicitly asks about something current outside Erenshor. This is separate from the Erenshor wiki and official Erenshor news.")]
        public bool ExternalNewsEnabled = true;

        [Config("AutoLookup", "ExternalNews", "Automatically trigger an external news search for clear current-events questions (e.g. 'what's going on with NASA?'). /dsxnews <query> always works while ExternalNews is enabled.")]
        public bool ExternalNewsAutoLookup = true;

        [Config("Provider", "ExternalNews", "GDELT Doc 2.0 endpoint override. This mod always tries the free, keyless Google News RSS search first, then falls back to this GDELT endpoint - this setting only overrides the GDELT fallback's URL, it does not select between providers. Only a documented HTTPS API should go here; this mod never scrapes arbitrary HTML.")]
        public string ExternalNewsApiUrl = "https://api.gdeltproject.org/api/v2/doc/doc";

        [Config("ApiKey", "ExternalNews", "Optional API key for a keyed news provider. Unused by either built-in keyless provider (Google News RSS, GDELT). Never logged, never exported. Leave blank unless you have configured a provider that requires one.")]
        public string ExternalNewsApiKey = "";

        [Config("MaxResults", "ExternalNews", "Maximum recent articles retrieved per external news search (1-5).")]
        public int ExternalNewsMaxResults = 4;

        [Config("TimeoutSeconds", "ExternalNews", "Maximum wall-clock time for the WHOLE external-news lookup (primary + fallback provider combined), not per provider.")]
        public int ExternalNewsTimeoutSeconds = 6;

        [Config("MaxContextCharacters", "ExternalNews", "Maximum combined external-news extract supplied to the local model.")]
        public int ExternalNewsMaxChars = 900;

        [Config("ConversationTtlMinutes", "ExternalNews", "How long a retrieved external-news topic stays available for conversational follow-up before it expires and a fresh search is required.")]
        public int ExternalNewsTtlMinutes = 6;

        [Config("Enabled", "Social Director", "Allow Deep Sims to occasionally speak without being directly prompted.")]
        public bool DirectorEnabled = true;

        [Config("EventChatter", "Social Director", "Allow reactions to observed game events such as deaths, levels, quests, and zoning.")]
        public bool EventChatter = true;

        [Config("IdleChatter", "Social Director", "Allow rare quiet-moment group chatter.")]
        public bool IdleChatter = true;

        [Config("SimToSimReplies", "Social Director", "Allow a second Deep Sim to occasionally reply to another Deep Sim in group chat.")]
        public bool SimToSim = true;

        [Config("PartyChatResponses", "Social Director", "Let normal player /p party-chat messages naturally prompt Deep Sim replies without requiring /dw.")]
        public bool PartyChatResponses = true;

        [Config("EventReactionChance", "Social Director", "Global multiplier from 0 to 1 for event reactions. The model can still choose silence.")]
        public float EventReactionChance = 0.70f;

        [Config("DuelReactionChance", "Social Director", "Chance from 0 to 1 to ask a Deep Sim for a short reaction after a completed friendly practice duel. The duel result is always saved as verified memory.")]
        public float DuelReactionChance = 1.00f;

        [Config("EventCooldownSeconds", "Social Director", "Minimum seconds between verified event-conversation opportunities (effective minimum 30 seconds). This is separate from idle/banter cooldowns.")]
        public float EventCooldownSeconds = 30f;

        [Config("Enabled", "Camp Mode", "Automatically enter social camp mode when the player remains seated/meditating outside combat. Ignored when Erenshor Campmaster is detected; see CampmasterIntegration.")]
        public bool CampMode = true;

        [Config("CampmasterIntegration", "Camp Mode", "When the optional Erenshor Campmaster mod is present, use its verified Hunt Camp recognition instead of sitting detection, and add its verified facts to prompts.")]
        public bool CampmasterIntegration = true;

        [Config("EnterAfterSeconds", "Camp Mode", "Seconds the player must remain seated outside combat before automatic camp mode begins.")]
        public float CampEnterSeconds = 8f;

        [Config("ChatterMinSeconds", "Camp Mode", "Minimum quiet time before camp chatter becomes possible.")]
        public float CampIdleMinSeconds = 12f;

        [Config("ChatterMaxSeconds", "Camp Mode", "Quiet time at which the next camp chatter check becomes strongly encouraged.")]
        public float CampIdleMaxSeconds = 40f;

        [Config("SimToSimReplyChance", "Social Director", "Chance from 0 to 1 that a generated group line may get one natural reply from another Deep Sim.")]
        public float SimToSimChance = 0.60f;

        [Config("IdleMinSeconds", "Social Director", "Seconds of party-chat silence before spontaneous chatter starts becoming possible.")]
        public float IdleMinSeconds = 90f;

        [Config("IdleMaxSeconds", "Social Director", "At this many seconds of party-chat silence, the next spontaneous chatter evaluation is strongly encouraged.")]
        public float IdleMaxSeconds = 300f;

        [Config("AutonomousCooldownSeconds", "Social Director", "Minimum time between autonomous Deep Sim conversation opportunities.")]
        public float AutonomousCooldownSeconds = 35f;

        [Config("TypingCharsPerSecond", "Social Director", "Approximate simulated typing speed used only to delay display of generated group chat.")]
        public float TypingCharsPerSecond = 22f;

        [Config("MinTypingDelaySeconds", "Social Director", "Minimum simulated typing delay for autonomous group chat.")]
        public float MinTypingDelay = 0.7f;

        [Config("MaxTypingDelaySeconds", "Social Director", "Maximum simulated typing delay for autonomous group chat.")]
        public float MaxTypingDelay = 3.5f;

        [Config("ConversationThreads", "Social Director", "Allow context-aware multi-turn party conversations. Player messages can continue a conversation; AI-only runs are safety-capped.")]
        public bool ConversationThreads = true;

        [Config("PartyReadDelaySeconds", "Social Director", "Brief delay before answering party chat so nearby lines can be read as one banter turn. Newer player messages cancel stale work.")]
        public float PartyReadDelaySeconds = 0.55f;

        [Config("ThreadReadDelaySeconds", "Social Director", "Brief delay after a Deep Sim reply becomes visible before deciding on the next line in the same thread. Lets the player, a vanilla Sim, or combat change the conversation before the next line is generated.")]
        public float ThreadReadDelaySeconds = 0.9f;

        [Config("MaxAutonomousThreadReplies", "Social Director", "Maximum Sim responses to a single party-chat line before Deep Sims waits for the player. Hard-capped at 3. This is an upper bound, not a target: threads stop earlier whenever the previous line does not invite an answer.")]
        public int MaxAutonomousThreadReplies = 4;

        [Config("PauseAutonomousAIInCombat", "Performance", "Do not start idle/event/banter LLM generations during active or very recent combat. Player-initiated /p and /dw replies still work.")]
        public bool PauseAutonomousInCombat = true;

        [Config("InferenceMode", "Performance", "Ollama runner mode: Auto lets Ollama choose, CPU forces num_gpu=0, GPU requests maximum GPU offload (num_gpu=-1). Changing this may reload the model.")]
        public string InferenceMode = "Auto";

        [Config("ReasoningMode", "Performance", "Diagnostic/routing signal only: Off never flags a turn as reasoning-shaped, Selective flags factual/history/grounding-correction requests, Always flags every LLM line. Deep Sims requests the SAME single Model for every call regardless of this setting; it no longer selects a different model.")]
        public string ReasoningMode = "Selective";

        [Config("ReasoningModel", "Performance (legacy)", "DEPRECATED. Deep Sims now requests only the single Model above for every call; a separate reasoning model is never selected at runtime, so keep_alive can no longer keep two models resident. This field is read once at startup only to migrate an existing installation's previously chosen strong model into Model, then has no further effect.")]
        public string ReasoningModel = "qwen3.5:4b";

        [Config("CpuThreads", "Performance", "CPU inference thread count sent to Ollama. 0 lets Ollama choose. Mainly useful with InferenceMode=CPU.")]
        public int CpuThreads = 0;

        [Config("FrameHitchThresholdMs", "Performance", "Frame duration counted as a hitch for /dsperf correlation. Hitches while the game window is unfocused are ignored.")]
        public float FrameHitchThresholdMs = 100f;

        [Config("KnowledgeDisagreementChance", "Social Director", "Chance from 0 to 1 that a general party wiki/news question starts with one tentative or incomplete Sim answer before another Sim corrects/clarifies it. The verified source remains authoritative; set 0 to disable.")]
        public float KnowledgeDisagreementChance = 0.12f;

        [Config("VanillaChatterContinuity", "Social Director", "Let Deep Sims hear normal Erenshor party chatter and occasionally continue a substantive vanilla Sim line as part of the same conversation.")]
        public bool VanillaChatterContinuity = true;

        [Config("VanillaChatterReplyChance", "Social Director", "Base chance from 0 to 1 that a substantive vanilla Sim party-chat line gets a Deep Sim continuation. Greetings, acknowledgements, and combat command chatter are much less likely or ignored.")]
        public float VanillaChatterReplyChance = 0.18f;

        [Config("ExpressionMode", "Social Director", "Autonomous social expression: Auto, LLM, Templates, or Off. Auto uses templates for ritual chatter and while Ollama is unavailable.")]
        public string SocialExpressionMode = "Auto";

        [Config("Perspective", "Social Director", "Social perspective: MMO or Roleplay. MMO keeps Sims talking like players in an old-school MMO. Roleplay makes them speak as the adventurers they represent. This does not change gameplay, grounding, or how often they talk.")]
        public string SocialPerspective = "MMO";

        [Config("ActivityPreset", "Social Director", "Autonomous social activity: Adaptive chooses a temporary Quiet/Normal/Lively party mood from personality and verified context; Quiet, Normal, and Lively are manual overrides.")]
        public string SocialActivityPreset = "Lively";

        [Config("AdaptiveTownZones", "Social Director", "Comma-separated verified scene names that receive a social town boost in Adaptive activity mode.")]
        public string AdaptiveTownZones = "Port Azure";

        [Config("Enabled", "Conversation Seeding", "Choose a grounded subject before speaking during a quiet moment. When off, ambient chatter is disabled entirely rather than falling back to unseeded chatter.")]
        public bool SeedingEnabled = true;

        [Config("Diagnostics", "Conversation Seeding", "Record per-candidate score components for /dsseeds recent. Turning this off keeps the decision history but drops the score breakdown.")]
        public bool SeedDiagnostics = true;

        [Config("SilenceNormal", "Conversation Seeding", "Score an ambient subject must beat to be worth saying outside camp. Higher means quieter.")]
        public float SeedSilenceNormal = 42f;

        [Config("SilenceCamp", "Conversation Seeding", "Score an ambient subject must beat during camp downtime. Higher means quieter.")]
        public float SeedSilenceCamp = 38f;

        [Config("SilenceRelax", "Conversation Seeding", "Score a Relax subject must beat during explicit Relax downtime. Higher means quieter.")]
        public float SeedSilenceRelax = 34f;

        [Config("FatigueSeconds", "Conversation Seeding", "How long a recently discussed subject stays penalized.")]
        public float SeedFatigueSeconds = 300f;

        [Config("RecentTopicWindowMinutes", "Conversation Seeding", "Window used to count repeated party-wide use of the same subject.")]
        public float SeedRecentTopicWindowMinutes = 10f;

        [Config("VerboseLogging", "Diagnostics", "Enable high-volume Deep Sims social-routing diagnostics. Warnings, errors, grounding rejections, and explicit diagnostic commands remain visible regardless.")]
        public bool VerboseLogging = false;

        // ---- Local prompt-capture diagnostics (developer only, OFF by default) --------------------
        // When enabled, Deep Sims writes the exact LLM request/result packets for each inference to a
        // local diagnostics directory so prompt designs can be compared offline. Packets contain the
        // real conversation and prompt text by design and never leave this machine; normal logs keep
        // only privacy-safe counters. Leave this off for ordinary play.
        [Config("PromptCaptureEnabled", "Diagnostics", "DEVELOPER ONLY. Write exact local LLM request/result packets for offline prompt analysis. Packets contain real conversation text and stay on this machine. Off by default.")]
        // Shipped default is OFF. Toggle this in the runtime config (or via the in-game settings) for
        // an explicit local capture session - do not flip the compiled default for that; it must stay
        // false in source so an ordinary build/install never captures dialogue without the operator
        // deliberately turning it on.
        public bool PromptCaptureEnabled = false;

        [Config("PromptCaptureMaxFiles", "Diagnostics", "Maximum logical LLM requests captured per session. When reached, capture stops and warns once rather than deleting collected evidence.")]
        public int PromptCaptureMaxFiles = 100;

        [Config("PromptCaptureIncludeClassifier", "Diagnostics", "Also capture the semantic classifier call as its own linked packet, including the raw classification and the deterministic corrections applied to it.")]
        public bool PromptCaptureIncludeClassifier = true;
    }
}
