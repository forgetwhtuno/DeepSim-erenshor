# Deep Sims for Erenshor 0.8.2 Beta

Deep Sims makes Erenshor's existing SimPlayers feel more like persistent MMO companions. It observes verified game state, keeps bounded sidecar memory, and produces short social dialogue through deterministic templates or an optional local Ollama model.

With Campmaster 0.4.0 present, Campmaster owns a deterministic activity classification (`Combat`, `Travel`, `ActiveGameplay`, `SocialDowntime`, `ExtendedDowntime`) sampled from current readiness, party, combat, optional competitive lifecycle, native pull activity, scene, and player displacement. Sustained safe stationary party downtime can become automatic Relax social context after about 60 seconds. Deep Sims then runs bounded low-priority context assessments—30–60 seconds for a 4–5 Sim Lively party and much less often for one companion—which may either open one normal bounded thread or deliberately keep quiet.

Recent supported visible party/Say/Shout/Guild/whisper context is held only in a 20-line in-memory buffer. Public chat remains attributed `HEARD` context, never verified world fact, never a public reply target, and never durable memory. The existing compact session summary is likewise ephemeral and cannot promote itself into factual history.

**Deep Sims does not replace Erenshor's Sim AI and does not control gameplay.** Erenshor remains authoritative for movement, combat, pulls, healing, targeting, loot, grouping, roles, equipment, quests, faction, progression, and saves.

> This is a public beta candidate. Deep Sims never controls Erenshor gameplay, and its LLM-backed dialogue requires a local Ollama-compatible runtime and model.

Part of the **Forgotten Roads for Erenshor** mod collection.

## What's new in 0.8.2 Beta

- On return after a bounded offline interval, native Friends can receive modest simulated Recent Life (availability, brief exploration/gathering/crafting/solo play, and occasional participant-only overlap). It is clearly provenance-tagged sidecar roleplay, retained for seven days, and never changes native levels, loot, gear, gold, quests, or the Friend roster. Party Tools v2 is optional; without it catch-up fails closed.
- During MMO-perspective social downtime, a two-or-more-Sim party may very occasionally discuss one actually retrieved outside-world headline. Direct conversation and current threads win, Roleplay disables the lane, all replies reuse the same evidence, and headline chat remains ephemeral rather than permanent memory.
- Practice Duel contract-v4 completion facts carry RequestId/DuelId correlation into one character-scoped social episode; only positively proven loaded same-scene Deep Sims witness local Duel events, while verified participants are explicitly KnownBy.
- MMO perspective now has a persistent fictional simulated-player background separate from verified Erenshor facts and the in-world roleplay persona. Authored background overrides the generated default; Roleplay mode keeps this layer hidden.
- The Identity Editor is a retained-uGUI scroll form with resolution-independent anchors, field-local labels/reset controls, and Generated/Authored/Unknown provenance.
- Campmaster schema 3 / SocialContract v1 living events can become factual episodes and seeds; living-event zone attribution requires matching Camp session identity.
- Repeated same-speaker chat evidence is canonicalized for prompt/memory use while retaining recurrence metadata.
- Player replies receive scheduling priority over low-value curation/reflection/autonomous work, and low-value autonomous grounding gets no expensive semantic retry. `/dsperf` exposes bounded queue/retry/social metrics.

## What's new in 0.7.4

- Explicit opinion and preference questions stay with the Sim instead of triggering factual Wiki/news retrieval.
- Direct replies that fail grounding use a bounded grounded/template fallback; rejected generated claims remain hidden.
- `/dsbanter` now creates a bounded connected A+B conversation thread through the existing Social Director.
- Autonomous semantic seeds temporarily remember verifier rejection, reducing repeated unsupported prompts without turning rejected text into world knowledge.

## Identity and retained Identity Editor

Fresh Sims no longer present an empty authored-identity schema. Deep Sims now generates a **deterministic, non-factual default profile** from the Sim's stable identity, current native class, level tone, and verified current-character Friend state when that state is actually available. The six current class templates are Arcanist, Druid, Paladin, Reaver, Stormcaller, and Windblade; unknown/future classes fall back to the universal adventurer scaffold.

The layers remain deliberately separate:

1. **Verified native facts** — current class, level, zone, guild, current-character Friend state, party/role context.
2. **Default template** — generated roleplay tendencies only; never evidence of birthplace, family, named training, religion/order, accomplishments, or past events.
3. **My Override** — explicit player-authored personality, personal background, Erenshor persona, and relationship canon.
4. **Pinned/authored + shared history** — explicit facts the player chooses to establish, with shared KnownBy limited to selected current Sims.
5. **Learned structured memory** — gameplay/conversation-derived records owned by the existing validation pipeline.

Open the existing Deep Sims retained panel and choose **Identity Editor**. There is no new global hotkey. The editor uses retained Unity uGUI and provides:

- a Sim selector plus verified current context and the generated Default Profile;
- six multiline authored fields—personality, simulated-player background, Erenshor persona, relationship, long-term wants, and cares—each with explicit generated/authored provenance;
- Save, per-field reset, Reset All Authored Identity, and Reload/Cancel;
- separate `PINNED / AUTHORED`, `SHARED HISTORY`, and `LEARNED MEMORY` sections;
- add/edit/remove for authored memory, participant-selective shared history, and Forget / Copy → Pinned for learned memory;
- explicit local biography Export/Import using a versioned JSON schema under Deep Sims' own local export directory.

Empty authored fields mean **use the default**; generated defaults are never copied into authored fields merely because the editor was opened or saved. Editing a learned memory in place is intentionally unsupported—copy it into authored pinned memory first if you want to rewrite it.

Power-user commands remain available:

```text
/dsidentity <Sim> show
/dsidentity <Sim> editor
/dsidentity <Sim> set personality|background|persona|relationship <text>
/dsidentity <Sim> add history|pinned <text>
/dsidentity <Sim> share <OtherSim> <shared-history text>
/dsidentity <Sim> clear|reset personality|background|persona|relationship
/dsidentity <Sim> reset all
/dsidentity <Sim> export
/dsidentity <Sim> import
```

Default identity is prompt flavor below live native facts, authored canon, and verified learned history. Background is admitted only when the current question makes it relevant; a dungeon-readiness question does not randomly pull in an unrelated college/job/family-style authored background.

## Requirements

- Erenshor
- [Lunaris](https://github.com/MizukiBelhi/Lunaris)
- A local Ollama-compatible runtime and available model for LLM-backed dialogue. The default model is `qwen3.5:4b`; deterministic `Templates` mode remains available for troubleshooting or inference-free fallback.

Deep Sims no longer requires BepInEx as its native plugin loader. Harmony is still used intentionally for verified Erenshor hooks and the existing rich command parser.

## Install

### Normal/manual install

1. Install Lunaris and launch Erenshor once.
2. Put `ErenshorDeepSims.dll` in:

```text
<Erenshor>\plugins\ErenshorDeepSims.dll
```

3. Launch Erenshor through the normal Lunaris installation.
4. Use `/aistatus` or `/dsims` after entering the game.

Do **not** copy `Lunaris.dll`, `0Harmony.dll`, Newtonsoft, ImGui, or other Lunaris runtime libraries into the Deep Sims package. Lunaris owns those dependencies.

### Developer build

`BUILD_AND_INSTALL.ps1` compiles against your current installed Erenshor assemblies plus local Lunaris developer references.

Put at least these two developer references in `LunarisLibs\` or pass `-LunarisLibDir`:

```text
Lunaris.dll
0Harmony.dll
```

Then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\BUILD_AND_INSTALL.ps1
```

The script compiles to a temporary file first and copies only a completed DLL into `<Erenshor>\plugins`, avoiding a partial DLL being observed by Lunaris' runtime watcher.

## 30-second quick start

After entering the game and grouping with SimPlayers:

```text
/aistatus                 Deep Sims / Ollama status
/dsims                    current Deep Sim party status
/dssocial status          social expression/activity status
/dsroleplay status        MMO vs Roleplay perspective
/dssession                current/last/outing encounter state
/dsperf                   performance and request diagnostics
```

Then simply talk in normal party chat. Deep Sims preserves Erenshor's ordinary command-bearing group chat path.

## Social expression modes

`/dssocial` controls **how** autonomous social lines are expressed:

```text
Auto        use LLM when appropriate/healthy; deterministic fallback when not
LLM         prefer the local model
Templates   deterministic social expression only
Off         disable autonomous Deep Sims social expression
```

Activity can independently be `Adaptive`, `Quiet`, `Normal`, or `Lively`.

## MMO vs Roleplay perspective

`/dsroleplay` controls **who the Sim is speaking as**, independently from expression mode:

```text
/dsroleplay on        Roleplay: speak as the adventurer represented by the Sim
/dsroleplay off       MMO: speak like another old-school MMO player
/dsroleplay status
```

Roleplay does not change talk frequency, grounding, gameplay authority, or memory truth. It changes voice/perspective only. Roleplay autonomous output has an additional guard against MMO/meta language, narration, invented affiliation/history, and post-generation typing texture such as `lol` or text faces.

## Lunaris settings and data

Native settings are registered through Lunaris and appear in its config system. The expected config file is:

```text
<Erenshor>\plugins\config\erenshordeepsims.lpcfg
```

Deep Sims-owned sidecar data lives under:

```text
<Erenshor>\plugins\config\DeepSims\
    Memory\
    Exports\
```

Deep Sims never writes its social memory into Erenshor save files.

### Existing BepInEx installs

The native Lunaris configuration starts as a fresh typed Lunaris config; this migration deliberately does not guess at or rewrite arbitrary old BepInEx settings.

For a simple direct game-root legacy install only, if the new Lunaris memory directory is empty, Deep Sims will copy existing files from:

```text
<Erenshor>\BepInEx\config\DeepSims\Memory
```

into the new Lunaris sidecar directory. The old files are left untouched. r2modman/Thunderstore profile directories are not searched automatically.

## Grounding and memory boundary

Deep Sims separates observed/verified state from heard or generated text. Broadly:

```text
OBSERVED_NOW
> verified EXPERIENCE / EVENTS
> bounded REMEMBERED summaries
> wiki / official game news
> external real-world news
> HEARD dialogue/player claims
> UNKNOWN
```

Generated dialogue is evidence only that a line was said. It is never proof that its content happened. Unsupported phrases such as `again`, `last time`, or shared-history claims are rejected unless verified history supports them.

### Live party grounding

0.7.2 adds a separate typed `LivePartyFacts` boundary for current party membership. `GameData.GroupMembers` is the primary native authority; manual Deep Sim slots only filter which already-grouped local Sims may be enhanced and can never create membership. Every party-facing inference captures a membership version immediately before prompting, revalidates it after inference/before grounding, and checks it again at the final display boundary. A short empty-roster hold during zoning is represented as `transition_uncertain`, never as confirmed current membership. Remote COOP humans may appear as context when the existing COOP bridge proves them, but they are never eligible generated speakers.

A deterministic final party-stance guard rejects or narrowly rewrites lines that conflict with live membership (for example an already-grouped Sim asking to be invited). See `DEEP_SIMS_PARTY_GROUNDING_IMPLEMENTATION.md` and `LIVE_TEST.md`.

## Main commands

```text
/aistatus                 model/plugin status
/aitest                   AI/integration smoke test
/aimodel <model>          change Ollama model
/dwhisper <Sim> <text>    force an AI whisper
/vwhisper <Sim> <text>    request vanilla-style handling
/dsbanter                 start a bounded connected A+B banter thread
/dssession                encounter/session state
/dsperf                   performance/request diagnostics
/dsmemory [Sim]           bounded memory inspection
/dsforget <Sim> ...       forget eligible flavor/social data
/dsexport [full]          concise/detailed session export
/dsevents ...             verified event-director controls
/dsseeds ...              autonomous seed diagnostics
/dsguardtest              grounding/contract smoke tests
/dsinference ...          auto/CPU/GPU inference mode
/dsreasoning ...          reasoning-model routing
/dsxnews <query>          explicit external-news lookup
/dsnewsources             external-news source status
/dscamp ...               Campmaster context integration
/dssocial ...             expression/activity controls
/dsroleplay ...           MMO/Roleplay perspective
```

Deep Sims deliberately keeps its existing Harmony-backed `TypeText.CheckCommands` parser instead of converting these to Lunaris command attributes. The current commands include optional and multiword/free-form grammar, and the inspected Lunaris command surface does not provide the unregister semantics this project requires for safe hot unload.

## Co-op behavior

COOP is optional. When detected, Deep Sims preserves its conservative host-authority model. Remote humans are not classified as local Sims, remote chat remains HEARD rather than verified fact, and generated Sim speech is not given invented network replication authority.

## Optional companion mods

Deep Sims remains standalone. Current optional integrations include Campmaster, Practice Duels, PvP, Nemesis, and related suite components. Existing narrow runtime/reflection contracts remain optional and absent-safe during this migration; they are not being forced to Aura until both sides have stable native contracts.

None of those companion mods—and neither Forgotten Roads Suite Hub nor Erenshor COOP—is required to use Deep Sims.

`BUILD_AND_INSTALL.ps1` does **not** build sibling mods unless `-BuildCompanionMods` is explicitly supplied.

## Optional Suite Hub integration

Suite Hub is optional. Deep Sims publishes a versioned primitive-only `DeepSimsControlApi`/Aura surface and never references Hub types. Hub may show a coarse enabled/perspective/Ollama status and edit an allowlisted subset of normal player settings. Basic controls are Perspective (MMO/Roleplay), Social expression (Auto/LLM/Templates/Off), Social activity (Adaptive/Quiet/Normal/Lively), autonomous chatter, and party-chat replies. Advanced controls cover existing social/knowledge/runtime toggles plus inference/reasoning routing; developer controls are limited to verbose/seed diagnostics.

API keys, endpoint URLs, raw memories, conversation history, prompts, filesystem paths, and arbitrary command execution are deliberately absent from the Hub setting surface. Changing perspective through Hub uses the same Roleplay state/config path as `/dsroleplay`; it does not bypass the final Roleplay output guard, grounding, quality checks, generation invalidation, or gameplay-authority boundaries.

The pending-Ollama unload/re-enable sequence remains a **live validation requirement**. Source guards alone are not treated as proof that repeated runtime unload/reload is release-ready.

## Privacy and network behavior

- Ollama defaults to a local endpoint.
- Wiki/news lookups are optional and bounded.
- External real-world news is conversation-scoped and never becomes Erenshor lore or permanent Sim memory.
- API keys are never intentionally logged or exported.
- Deep Sims memory remains local sidecar data.
- Exact prompt capture is a developer diagnostic, defaults off, and may contain real conversation text. Never enable or distribute it as a public release default.
- Do not publish personal memory exports or private logs with bug reports unless you have reviewed them.

## Hot reload / development safety

Lunaris can unload plugins while Erenshor is running, so `OnDestroy()` is part of correctness. Deep Sims now stops new request admission, invalidates conversation generations, clears queued display work, finishes/flushes sidecar state, removes its Harmony patches, clears Roleplay runtime context, and clears the plugin singleton without waiting indefinitely for an Ollama request.

Before calling a native Lunaris build release-ready, test:

1. load Deep Sims and join a party;
2. start an Ollama request;
3. unload Deep Sims through Lunaris while the request is pending;
4. confirm no late chat appears and no Harmony behavior remains;
5. reload and confirm exactly one working instance;
6. repeat unload/reload several times;
7. zone, then repeat unload/reload;
8. verify no duplicated chat, callbacks, social events, or memory writers.

See `LUNARIS_RELEASE_CHECKLIST.md` in the migration package for the complete matrix.

## Uninstall

`UNINSTALL.ps1` removes only the native Deep Sims DLL by default and preserves config/memory.

To intentionally remove Deep Sims-owned config and sidecar data too:

```powershell
.\UNINSTALL.ps1 -RemoveData
```

Erenshor save files are never deleted by this script.

## Troubleshooting

If Deep Sims does not appear:

- verify Lunaris itself loads;
- verify `ErenshorDeepSims.dll` is under `<Erenshor>\plugins`;
- check the Lunaris console/log for an assembly or Harmony error;
- rebuild against the current `Assembly-CSharp.dll` after an Erenshor update;
- use `/aistatus`, `/dsperf`, `/dsguardtest`, and `/dsinspect` when available.
- if Ollama is unavailable, start Ollama, confirm the configured model exists, or use `Templates` mode to verify the social layer independently from model inference;
- if the model is unavailable, run `ollama pull qwen3.5:4b` or select an already-installed compatible model with `/aimodel <model>`;
- if replies are slow, use `/dsperf`, try a smaller model, or compare `/dsinference cpu` and `/dsinference gpu` without treating temporal hitch overlap as proof of causation;
- reset authored identity through the Identity Editor or `/dsidentity <Sim> reset all`; use `/dsforget` only for eligible learned/flavor memory and review `/dsmemory <Sim>` first.

See `INSTALL.md` for the short installation and clean-start checklist.

## Related mods

- [Forgotten Roads: Practice Duel](https://github.com/forgetwhtuno/ForgottenRoads-Duel): friendly, non-lethal virtual-health duels with local Sims.
- [Forgotten Roads: PvP](https://github.com/forgetwhtuno/ForgottenRoads-PvP): standalone off-map Sim-profile PvP encounters.
- [Forgotten Roads: Follow](https://github.com/forgetwhtuno/ForgottenRoadsFollow): deterministic player follow, Sim-led travel, and expeditions.
- [Forgotten Roads: Party Tools](https://github.com/forgetwhtuno/ForgottenRoads-PartyTools): ready checks, cosmetic rolls, friend availability, and a compact command panel.
- [Forgotten Roads: Campmaster](https://github.com/forgetwhtuno/ForgottenRoads-Campmaster): read-only Hunt Camp and Relax social-context modes.
- [Forgotten Roads: Nemesis](https://github.com/forgetwhtuno/ForgottenRoads-Nemesis): an optional persistent rival system that can use PvP results when PvP is installed.

## Credits and inspiration

- **[CustomSimFramework](https://github.com/PuzzelPiece/CustomSimFramework) by PuzzelPiece / TeamSaltyBois** — inspiration for exploring richer Sim social behavior. Deep Sims is an independent implementation and does not use its code.
- **[Erenshor COOP](https://github.com/MizukiBelhi/ErenshorCoop) by MizukiBelhi** — important compatibility/reference work, especially for local-vs-remote actor boundaries. No COOP code is included.
- **[Lunaris](https://github.com/MizukiBelhi/Lunaris) by MizukiBelhi** — native Erenshor loader/config/plugin API used by this migration.

## Development note

Development is guided through design, testing, playtesting, audits, and iteration against Erenshor. Bug reports, code review, corrections, and contributions from experienced Erenshor modders are welcome.

This is an unofficial community-made mod for Erenshor and is not affiliated with or endorsed by the game's developer.
