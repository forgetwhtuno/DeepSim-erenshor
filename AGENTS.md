# AGENTS.md â€” Deep Sims for Erenshor

## Purpose

Save this file as UTF-8. Do not commit credentials, API keys, private endpoints,
or generated personal-memory files.

This repository contains **Deep Sims for Erenshor**, a native-Lunaris migration candidate that adds a local-LLM social layer to Erenshor's existing SimPlayers. Harmony remains an intentional narrow game-hook layer; BepInEx is no longer the native plugin/config/logging dependency.

The project goal is to make Sims feel like persistent MMO friends: aware of the current party, recent fights, shared history, loot, zones, and conversations, while still behaving like Erenshor Sims rather than AI assistants.

**Current development line:** 0.8.x
**Current development baseline:** 0.8.2 public-beta release candidate

Post-beta future work only: named/switchable in-world role profiles such as priest,
adventurer, or mercenary. Do not add that persistence/UI axis during 0.8.2 release
hardening. The beta identity contract remains native verified identity + one in-world
Erenshor persona + one fictional MMO-player background + bounded character-scoped memory.

---

# 1. Non-Negotiable Architecture Rules

These rules take priority over convenience.

## 1.1 Erenshor controls gameplay

The LLM must **never directly control gameplay**.

Do not allow the model to decide or invoke:

- movement
- attacks
- pulls
- spell casts
- healing targets
- loot decisions
- pathfinding
- grouping
- targeting
- equipment changes
- quest actions
- combat commands

The correct architecture is:

```text
Erenshor gameplay + Sim AI
        â†“
Deep Sims world awareness
        â†“
Social Director
        â†“
Local LLM
        â†“
Short social dialogue only
```

The LLM chooses **social intent and speech only**.

---

## 1.2 Facts and flavor must remain separate

Never treat generated dialogue as authoritative game state.

Use these trust categories:

1. **OBSERVED_NOW**
   - current combat
   - current target
   - current zone
   - current HP/mana when available
   - current Sim actions
   - current party composition

2. **EXPERIENCED**
   - verified kills
   - verified loot
   - verified quest completions
   - verified deaths
   - verified zones visited
   - completed encounter records

3. **REMEMBERED**
   - compact summaries derived from verified events
   - prior outings
   - verified shared history

4. **WIKI**
   - external Erenshor wiki knowledge

5. **NEWS**
   - official Erenshor / Steam update information

6. **EXTERNAL_NEWS**
   - recent real-world news retrieved only on clear player-initiated current-events intent
   - never Erenshor game fact, never treated as personal in-game experience
   - conversation-scoped only (short TTL); never becomes permanent Sim memory
   - see [ExternalNewsClient.cs](src/ExternalNewsClient.cs) and the README "External real-world news" section

7. **HEARD**
   - player claims
   - vanilla Sim dialogue
   - AI dialogue

8. **UNKNOWN**

Priority for answering:

```text
OBSERVED_NOW
> EXPERIENCED
> REMEMBERED
> WIKI / NEWS
> EXTERNAL_NEWS
> HEARD
> UNKNOWN
```

EXTERNAL_NEWS sits below WIKI/NEWS because it is real-world information, not Erenshor game
knowledge, and must never be used to answer questions about the game itself.

Player chat and AI chat may provide conversational context, but **must not create factual memory by themselves**.

---

## 1.3 Never fabricate personal history

Phrases implying prior events need a real supporting memory.

Examples requiring verification:

- "again"
- "last time"
- "remember when"
- "we used to"
- "you always"
- "I did this before"
- "that happened to me"
- "my pet bit me again"

If no verified memory supports the statement, rewrite or reject it.

Safe alternative:

```text
"I haven't named it yet. Got any ideas?"
```

Unsafe alternative:

```text
"Hope it doesn't bite me again."
```

unless a real prior biting event exists.

---

## 1.4 Prefer actual experience over wiki knowledge

If the party just looted an item from an enemy, that observed fact should beat a generic wiki answer.

Example:

```text
Observed:
Aetheria was looted immediately after killing a Lost Sea Giant.

Preferred answer:
"We just got one after a Lost Sea Giant."

Not preferred:
"The wiki says Ancient Educators drop it."
```

Do not turn one observed drop into an exclusive drop-table claim.

---

## 1.5 Silence is valid

The Social Director should not force constant conversation.

The AI may return:

```text
NO_MESSAGE
```

Autonomous chatter should fill social silence, not create spam.

Keep normal output short, usually one sentence.

---

## 1.6 Sims are MMO players, not assistants

Reject or retry chatbot-like responses such as:

- "How can I assist you?"
- "What would you like to discuss?"
- "I'm here if you need anything."
- "What is on your mind?"
- explanations of retrieval, prompting, AI, or system instructions

Dialogue should resemble casual MMO party chat.

---

## 1.7 Do not alter Erenshor save files

Deep Sims memory must remain sidecar data owned by the mod.

Do not modify core Erenshor saves.

---

# 2. Development Workflow for Codex

When asked to continue development:

1. Read this file.
2. Inspect the current implementation before changing anything.
   Read only the relevant source files first; do not inspect unrelated systems
   unless the requested change requires it.
3. Identify the highest-priority unfinished task relevant to the user's request.
4. Make the smallest coherent implementation.
5. Preserve existing working behavior.
6. Avoid speculative reflection against unknown Erenshor fields when a safer existing signal exists.
7. Do not invent APIs, fields, enum values, or game behavior.
8. If a game API cannot be established from the repository or available assemblies, leave a clearly documented TODO rather than guessing.
9. Keep performance-sensitive Unity work off hot paths where possible.
10. Keep network, wiki, and news access optional and narrow.
11. Compile/test when the repository provides a usable build path.
12. For native Lunaris work, treat `OnDestroy()` cleanup and repeated hot unload/reload as correctness requirements: remove Harmony patches, stop/neutralize background work, clear callbacks/statics, and never allow a late inference result to write chat.
13. Do not introduce a Lunaris command/config/event callback that cannot be proven unload-safe; preserve the existing Harmony command parser when the native API cannot express its grammar or unregister semantics safely.
14. Update documentation and this task list when behavior changes.

For roadmap items, define a concrete acceptance check before marking the item
complete. The check should state what can be observed in code, diagnostics, or
in-game testing.

Prefer focused commits.

---

# 3. Immediate Priority Queue

Work from the top unless the current user request clearly targets a lower item.

The first active priorities are:

1. P0.1 - Validate encounter separation.
2. P0.2 - Maintain a short encounter history.
3. P0.3 - Strengthen temporal-history grounding.
4. P0.4 - Strong party identity enforcement.
5. P0.5 - Exact Manage Roles awareness.

Do not begin lower-priority roadmap work while one of these remains unfinished,
unless the user explicitly requests it.

---

## P0 â€” Correctness and Stability

### [x] P0.1 Validate encounter separation

Current implementation has separate current, last-completed, and bounded recent
encounter snapshots. `/dssession` now reports the right-now and last-completed
values independently. Leave this item unchecked until those states are verified
in game across two encounters separated by the configured quiet period.

Ensure these are genuinely distinct:

```text
RIGHT NOW
LAST COMPLETED ENCOUNTER
WHOLE OUTING
```

Requirements:

- "How is this fight going?" uses only current encounter state.
- "How was the last fight?" uses only the frozen previous completed encounter.
- "How has the session been?" uses outing/session aggregate state.
- Do not mix current combat into previous-fight answers.
- Do not use session totals as though they describe one fight.
- Do not invent exact encounter duration language unless exact timing is intentionally exposed.
- Encounter completion should require a meaningful quiet period after combat.
- Prevent a single fight from being split into multiple fake encounters.

Add diagnostics that make current and previous encounter state easy to inspect.

---

### [x] P0.2 Maintain a short encounter history

Current implementation keeps a bounded, structured three-encounter history with
an ID, UTC start/end times, zone, enemies, kills, deaths, close calls, result,
and up to three observed combat actions. Verify the records in game before
marking this complete.

Move from only `current + last` toward a bounded history, preferably last 3 completed encounters.

Suggested record:

```text
Encounter
- id
- zone
- start/end timestamps
- enemy types/counts
- verified kills
- deaths
- close calls
- notable Sim actions
- result
- optional approximate duration
```

Do not retain unbounded encounter history in active prompt context.

---

### [x] P0.3 Strengthen temporal-history grounding

Use `/dsguardtest` to run deterministic smoke tests for unsupported history,
wrong party identity, false no-new-kills claims, invented group plans, and a
valid identity claim. All reported cases must pass
before in-game dialogue testing; rejected live replies also report their reason
through the response status and log.

Centralize checks for unsupported historical language.

Guard concepts including:

```text
again
before
last time
remember
used to
always
another one
same as before
back here
still doing X
```

The guard should distinguish harmless grammatical use from claims of shared history.

Prefer regeneration/rewrite over simply dropping a useful response.

---

### [ ] P0.4 Strong party identity enforcement

The grounding boundary rejects named wrong-class, incompatible tank/healer-role,
and wrong-guild claims when live identity data is available. Confirm against a
real party roster before marking this complete.

Every active Deep Sim should know verified party facts:

- name
- class
- level
- guild
- known role/capabilities
- whether member is a human or Sim

Reject contradictions such as:

```text
Phanty is a plate tank
```

when Phanty is verified as an Arcanist.

Do not let generated dialogue redefine another party member's class.

---

### [x] P0.5 Exact Manage Roles awareness

Implemented with a read-only `NativeRoleReader` against the verified native
`GameData.SimPlayerGrouping` fields: `MainTank`, `DesignatedMA`, `Puller`, `CC`,
and `Heals`. `DesignatedMA` is used instead of combat-temporary `MainAssist`.
Snapshots fail closed as unknown if any role read fails; class capability remains
separate. Prompts expose exact assignments and grounding rejects named assignment
contradictions. Acceptance check: deterministic guards pass supported/contradictory
assignment cases and the full build compiles against the installed game assembly.
Use `/dsinspect` with the Manage Roles window changed between checks for live validation.

Current class capability is not enough.

Investigate how Erenshor stores the actual Manage Roles assignment.

Goal:

```text
class capability:
Druid can heal or DPS

actual assigned role:
Cyndara is currently assigned healer
```

Do not claim an exact role until it can be verified from game state.

If reflection is needed, isolate it behind a small compatibility layer and fail safely.

---

## P1 â€” Performance

### [ ] P1.1 Validate 0.6.1 performance diagnostics

Confirm `/dsperf` records:

- party refresh last/max
- AI request wall time
- Ollama total duration
- model load duration
- prompt evaluation duration
- generation/evaluation duration
- prompt token count
- generated token count
- request attempts
- queue/typing delay
- frame hitch count
- frame hitch max
- whether frame hitches overlap AI work

Avoid allocating heavily every frame merely to collect diagnostics.

---

### [ ] P1.2 Inference modes

Support and validate:

```text
/dsinference auto
/dsinference cpu
/dsinference cpu <threads>
/dsinference gpu
```

Goals:

- allow CPU inference to diagnose GPU contention
- avoid blocking Unity's main thread
- make configuration persist
- make unsupported settings fail clearly
- report active mode in `/dsperf`

The next AI request after changing runner options may behave differently; diagnostics should make this visible.

---

### [ ] P1.3 Frame-hitch correlation

Record large frame-time events with nearby AI state.

Useful state:

```text
timestamp
frame duration
AI idle / queued / requesting / parsing / typing
request id
time since request start
time since request completion
```

Do not conclude "AI caused hitch" solely from temporal overlap. Report correlation.

---

### [ ] P1.4 Reduce prompt and allocation overhead

Current implementation caps private-chat continuity at four lines, party-thread
history at two prior lines, style examples at two, outing summaries at two,
social summaries at one, relationship notes at two, important memories at three,
and recent verified events at four. `/dsperf` reports an estimated prompt size
beside Ollama's authoritative prompt-evaluation token count. Verify an ordinary
Auto request stays near or below 1,500 prompt-evaluation tokens without losing
the current/last/outing grounding behavior.

Audit:

- repeated `FindObjectsOfType`
- unnecessary LINQ on hot paths
- repeated regex construction
- full-memory serialization every request
- repeated reflection
- large string concatenation
- excessive context size
- unbounded recent-chat buffers

Prefer cached party snapshots and bounded collections.

---

## P1 â€” Dialogue Grounding

### [ ] P1.5 Item-aware loot discussion

Before a Sim says an item is useful for someone, verify available item facts.

Desired facts where available:

- item name
- slot
- class restriction
- stats
- quality
- current wearer compatibility
- whether the Sim can plausibly use it

Allowed:

```text
"That looks useful for Phanty."
```

only when facts support it.

Avoid:

```text
"Phanty needs that plate chest."
```

when class restrictions contradict it.

Opinions can remain subjective; compatibility must be factual.

---

### [ ] P1.6 Source-confidence-aware answer planning

Create an internal answer plan before generation when factual questions are involved.

Example:

```text
Question: where did we get Aetheria?

Best source:
EXPERIENCED â€” looted shortly after Lost Sea Giant kill

Backup:
WIKI â€” known drop sources

Forbidden:
AI conversation memory
```

Feed only the relevant facts to the model.

---

### [x] P1.7 Better anti-repetition

Implemented a conservative short-window idea classifier for continuation/readiness,
close-call, generic-praise, and idle-waiting concepts. It runs at both the central
autonomous budget and the final recent-AI output boundary, while ordinary agreement
remains unclassified. Acceptance check: differently worded continuation ideas are
suppressed across speakers while `yeah, i agree` remains allowed.

Current string similarity is not enough.

Add lightweight idea-level repetition detection.

Examples that should count as near-duplicates:

```text
"let's keep moving"
"we should keep going"
"yeah, onward"
```

Do not over-filter natural agreement.

Track only a short recent window.

---

### [ ] P1.8 Better post-combat reactions

Partial upgrade implemented: deterministic encounter templates now distinguish
recorded party deaths, close calls, five-or-more-kill crowds, and ordinary completed
kills. They do not use comparison language or infer pull quality. Keep this item open
until encounter-scoped notable loot/actions and intentionally exposed duration facts
exist and are covered in game.

After encounter completion, allow grounded reactions based on:

- close call
- death
- easy fight
- long fight
- many enemies
- notable loot
- specific verified actions

Examples:

```text
"that got a little close"
"nice pull"
"that one went way smoother"
```

Only use comparison language if a real comparison source exists.

---

# 4. Social History / 0.7 Direction

## [x] P2.1 Sim-to-player familiarity

Implemented in 0.7.0 with bounded Familiarity, Rapport, and Rivalry tone state.
Familiarity derives from completed outings, actual grouped time, and repeated
conversation exchanges. Raw scores are not shown to players. Retain in-game
validation across several real outings.

Familiarity should emerge from:

- outings together
- approximate grouped time
- conversations
- verified shared events

Do not reduce this to a visible dating-sim-style numeric meter.

Use coarse internal tiers such as:

```text
new
acquaintance
familiar
regular
```

Tone may change subtly with familiarity.

---

## [x] P2.2 Sim-to-Sim familiarity

Implemented in 0.7.0 with bounded shared outings, actual overlapping grouped
minutes, shared conversation threads, and conservative relationship tone.
Brief leave/rejoin cycles do not create relationship progress, and relationship
state is never evidence that a specific event happened.

Track shared social history between party Sims.

Possible compact data:

```text
shared outings
approximate shared minutes
shared conversation threads
notable verified shared events
```

Use this for tone only.

Do not invent past events merely because Sims have high familiarity.

---

## [x] P2.3 Reunion behavior

Implemented with a verified completed-outing gate, returning-Sim-only speaker scope,
silent initial roster priming, and an empty-to-grouped transition path. Reconnects,
join counts, and conversational familiarity alone cannot unlock reunion language.
Acceptance check: deterministic social tests must prove reconnect-only history is
rejected, a completed outing creates a `reunion` candidate, only the returning Sim
is eligible, and the bounded template contains no invented prior event detail.

A Sim returning after prior outings may acknowledge the history.

Examples:

```text
"hey, been a bit"
"back at it?"
```

Avoid overly emotional reunion dialogue unless supported by personality/history.

---

## [ ] P2.4 Running jokes

Allow recurring jokes only when they emerge from actual repeated events or prior verified conversational themes.

Do not manufacture a fake running joke on first contact.

Store the concept compactly rather than entire transcripts.

---

## [x] P2.5 Persistent preferences and opinions

Implemented a bounded eight-entry sidecar flavor-preference list. Only accepted,
fact-free, non-question ambient opinions from allowlisted topics may update it.
Preferences are shown separately by `/dsmemory`, are labeled non-factual in prompts,
never enter the verified-event corpus, and are selected by current-topic relevance.
Acceptance check: questions are refused, statements are accepted, older files
normalize the new field, and a stored preference survives reload.

Sims may develop lightweight recurring preferences:

- likes/dislikes a zone
- prefers cautious/aggressive pulls socially
- jokes about specific enemy types
- likes certain loot aesthetics
- enjoys/dislikes long dungeon runs

Preferences are flavor, not authoritative game facts.

Keep them bounded and editable/forgetful.

---

## [x] P2.6 Semantic memory retrieval

Implemented bounded lexical retrieval across verified outing summaries, important
memories, and non-dialogue recent events. The current message/situation supplies the
query; at most three existing strings are selected without rewriting or changing
trust. When any lexical match exists, unrelated recency-only candidates are omitted.
Acceptance check: an older Aetheria memory outranks a newer unrelated wolf event.

Do not dump all memory into every prompt.

Select only the 2â€“3 memories most relevant to the current topic.

Desired pipeline:

```text
current message
+ current world state
+ likely topic
        â†“
retrieve relevant compact memories
        â†“
build small prompt
```

Start with lightweight lexical/topic matching before adding expensive embeddings.

---

# 5. Co-op Compatibility

Treat this as an optional compatibility layer. Do not make Erenshor COOP a hard dependency.

## [x] P2.7 Detect Erenshor COOP safely

Current implementation detects COOP's `NetworkedPlayer` component by reflection
and excludes remote humans from Deep Sim snapshots and combat classification.
The implementation remains dependency-free. A host-and-client gameplay check is
still required before release, but the detection and classification code is complete.

If present, distinguish:

```text
local human
remote NetworkedPlayer human
local/host Sim
networked Sim
```

Important known compatibility concern:

A remote co-op human may still carry a `SimPlayer` component internally.

Do **not** identify Deep Sims solely by `GetComponent<SimPlayer>() != null`.

Prefer explicit game/co-op markers indicating whether the entity is actually a Sim.

---

## [x] P2.8 Host-authoritative Deep Sims

When COOP is detected, Deep Sims is disabled unless the host explicitly enables
`Co-op/HostAuthority` in its config. Clients do not need Ollama or Deep Sims.
The local role must also be verified as the actual COOP host/server. A configured
client fails closed and does not start a second Social Director. COOP being merely
installed does not disable ordinary solo play.

Preferred co-op architecture:

```text
HOST
- runs Deep Sims Social Director
- runs Ollama
- owns social memory
- owns encounter/session memory
- generates Sim dialogue

CLIENTS
- contribute human party chat
- receive generated Sim dialogue
- do not independently run competing Social Directors
```

Avoid duplicate AI speakers and divergent memory.

---

## [x] P2.9 Co-op party chat ingestion

Human messages from all co-op participants should be eligible conversation context.

They are **HEARD**, not verified game facts.

Do not allow a remote player saying:

```text
"we killed the dragon already"
```

to create a verified kill unless game telemetry supports it.

The host recognizes visible COOP group lines only when their speaker is a verified
remote member of the current party, not merely a connected same-zone player. It
records them as dialogue context and routes response selection to the host Social
Director; it does not add them to combat telemetry.

---

## [ ] P2.10 Safe remote delivery of generated Sim speech

Determine whether Deep Sims-injected party messages automatically propagate through the co-op mod.

If not, implement a small optional bridge.

Requirements:

- one generated message
- one network broadcast
- no echo back into the generator as a new human message
- no duplicate display on host
- no hard dependency when co-op mod absent

COOP 2.3.1's public `GameHooks.SendMessageToPlayers` method broadcasts to every
same-zone peer and cannot be restricted to verified party recipients. Deep Sims
therefore displays generated party speech locally on the verified host and fails
closed for remote broadcast. Keep the diagnostic/TODO until COOP exposes a safe
party-targeted recipient API.

---

# 6. Knowledge and World Reasoning

## [ ] P2.11 Destination reasoning

Improve discussion of where to go next using:

- current zone
- known neighboring/travel zones
- relevant quests
- observed party goals
- wiki knowledge when enabled

Do not let the model invent a route.

Represent uncertain routes as uncertain.

---

## [ ] P2.12 Wiki confidence and caching

Wiki lookup should:

- be optional
- be narrowly triggered
- prefer exact entity/title pages
- cache recent lookups
- distinguish old terminology such as legacy Duelist vs current Windblade
- never leak "according to my lookup" or retrieval instructions into Sim speech

---

## [ ] P2.13 Official news knowledge

Use official Erenshor/Steam news only for questions about:

- latest patch
- expansion
- update
- recent official changes

Do not spend network requests on normal social conversation.

---

## [x] P2.13a External real-world news

Implemented: `ExternalNewsClient` (GDELT Doc 2.0 API, keyless/HTTPS/JSON), `ExternalNewsQueryClassifier`
(keyword-only, no LLM), `/dsxnews <query>` and `/dsnewsources` commands, `ExternalNews` config section,
short-TTL conversational reuse of a retrieved topic, and prompt/grounding fencing that keeps real-world
news out of Erenshor game-fact answers and out of permanent Sim memory. See
[ExternalNewsClient.cs](src/ExternalNewsClient.cs), the trust hierarchy above, and the README
"External real-world news" section.

Still requires in-game testing: live `/p` phrasing coverage beyond the deterministic classifier tests,
and real GDELT network responses (only the JSON parsing/shape has been verified against the documented
schema, not a live call during this change). Not implemented: a keyed provider with article body
snippets (GDELT exposes headline/publisher/timestamp only); `ExternalNews/ApiKey` is reserved for that.

---

## [ ] P2.14 Global verified Chronicle and companion-event bridge

Current 0.7.0 memory is per-Sim plus structured outing/encounter telemetry. The
event-conversation pipeline reacts to bounded verified candidates, but there is
not yet one canonical global `VerifiedEvent` ledger or `/dschronicle` command.

Before adding one, define bounded records with stable IDs, UTC, type, zone,
participants, subject/target, importance, and trust/source. Existing per-Sim
memories should become projections of canonical verified events rather than a
second authority.

Generalize the existing optional `NotifyObservedGameEvent` reflection bridge so
Practice Duels can report verified results and Erenshor Follow can report only
verified completed travel. Companion mods must remain optional and must not gain
memory ownership.

---

# 7. Social Director Improvements

## [x] P3.1 Personality-based talk frequency

Implemented a separate bounded desire-to-volunteer probability using verified native
personality/typing cues and the effective Quiet/Normal/Lively preset. Adaptive activity
now rolls a temporary party mood from those same verified cues plus party size, a small
random term, and verified town/downtime context; manual presets remain overrides. Topic
relevance and final speaker selection remain separate, direct player-addressed replies
are unaffected, and class is never used as talkativeness. Acceptance check: deterministic
tests show distinct quiet/chatty probabilities, adaptive Quiet/Lively outcomes, exact
town matching, and positive town/downtime score changes within the configured bounds.

Not every Sim should speak equally often.

Use verified personality/typing traits where available.

Separate:

```text
desire to speak
speaker selection
response length
tone
```

Avoid hard stereotypes tied only to class.

---

## [ ] P3.2 Adaptive conversation length

Default to one sentence.

Allow two short sentences when:

- answering a direct question
- telling a compact remembered anecdote
- clarification is necessary

Avoid paragraph responses in party chat.

---

## [ ] P3.3 Better conversation handoff

Multi-Sim threads should feel like a party, not round-robin bots.

Allow:

```text
player â†’ Sim A â†’ Sim B
player â†’ Sim A only
vanilla Sim â†’ Deep Sim response
```

But not every line requires another AI response.

Maintain the autonomous reply cap.

---

## [ ] P3.4 Stale-response suppression

If the player or game state changes meaningfully while an AI request is queued:

- cancel or discard stale replies when possible
- do not answer an obsolete question after a new topic has taken over
- do not describe combat that already ended as current

Tag requests with enough context/version data to validate them before display.

---

# 8. Native Visual Integration

## [ ] P3.5 Validate native chat styling

Deep Sims should visually blend with Erenshor's native social chat.

Verify:

- AI party chat matches native Sim party-chat styling
- whispers match native whisper styling
- system/debug text stays visually distinct
- raw rich-text tags cannot leak from model output

Prefer learning/reusing Erenshor's actual color/style rather than guessing a similar color.

---

# 9. Public Configuration

## [ ] P3.6 Human-friendly config presets

Expose simple user-facing controls such as:

```text
Chatter:
Quiet
Normal
Talkative

Memory:
On / Off

Wiki:
Off / Manual / Auto

News:
On / Off

ImperfectKnowledgePercent:
0â€“20

Model:
qwen3.5:2b

Inference:
Auto / CPU / GPU

Debug:
On / Off
```

Keep advanced internals available in config but do not overwhelm normal users.

---

## [ ] P3.7 Controlled imperfect knowledge

General factual party questions may occasionally produce one tentative/incomplete answer that another Sim corrects.

Default concept: approximately 12% of eligible questions.

Rules:

- system always retains authoritative facts
- directly named Sim questions should not intentionally trigger the disagreement gimmick
- wrong statements never become memory
- don't use this for safety-critical or hard current-state facts
- don't turn every question into an argument

---

# 10. Memory and Export

## [ ] P3.8 Compact long-term memory

Memory should remain concise.

Prefer:

```text
Outing summary:
"Grouped in Krakengard, fought Molorai militia, had one close call, and found Aetheria."
```

over raw transcripts.

Limit stored conversational topics.

---

## [ ] P3.9 Session export

Maintain:

```text
/dsexport
```

for concise readable notes.

Optional:

```text
/dsexport full
```

for raw diagnostic/event detail.

Suggested concise sections:

```text
OUTING
RIGHT NOW
LAST COMPLETED FIGHT
RECENT FIGHTS
VERIFIED SESSION FACTS
NOTABLE LOOT
PARTY SOCIAL HISTORY
```

Do not expose hidden prompts or secrets in exports.

---

# 11. Reliability / Safety Guards

Always preserve or improve these protections:

- prompt-instruction leak guard
- assistant-style language guard
- rich-text sanitization
- emoji compatibility conversion
- unsupported boss/raid/quest/kill/death claims
- unsupported +N gear claims
- named-event grounding
- guild contradiction guard
- party class contradiction guard
- combat-state contradiction guard
- bounded recent-message history
- bounded AI queue
- graceful Ollama failure

If generation fails, the game should continue normally.

Deep Sims should never be required for Erenshor gameplay to function.

---

# 12. Testing Checklist

Before calling a feature complete, test as many as applicable.

## Party basics

- [ ] Detect 1â€“5 normal Sims.
- [ ] Do not enhance more than configured cap.
- [ ] Named `/p` question prefers named Sim.
- [ ] Tactical vanilla commands still pass through.
- [ ] Social `/p` chat is consumed by Deep Sims appropriately.

## Grounding

- [ ] Sim knows own class.
- [ ] Sim knows other party classes.
- [ ] Guild answers use verified guild.
- [ ] Current target answer matches live target.
- [ ] Current-combat answer does not say "nothing happening."
- [ ] "last fight" does not describe current fight.
- [ ] Session totals are not presented as one encounter.
- [ ] Unsupported history language is rejected or rewritten.

## Memory

- [ ] AI dialogue does not become factual memory.
- [ ] Player claims do not become factual memory.
- [ ] Verified events do become compact memory.
- [ ] `/dsforget` does not erase verified event history unless explicitly intended.
- [ ] memory files remain bounded.

## Performance

- [ ] no expensive scene scan every frame
- [ ] AI request runs asynchronously
- [ ] `/dsperf` remains cheap
- [ ] CPU mode works
- [ ] GPU/Auto mode works
- [ ] frame-hitch telemetry doesn't itself cause hitches

## Social behavior

- [ ] AI can remain silent
- [ ] no obvious assistant language
- [ ] no duplicate same-idea chatter
- [ ] no third-person self-reference
- [ ] thread cap works
- [ ] stale queued replies are suppressed
- [ ] vanilla chatter can enter thread without creating facts

## Visual

- [ ] native group-chat color/style matches
- [ ] whisper styling matches
- [ ] debug text stays distinct
- [ ] no raw markup leakage

## Co-op, when implemented

- [ ] remote human is never mistaken for a Deep Sim
- [ ] only host runs Social Director
- [ ] client human chat reaches host context
- [ ] generated Sim message displays once per peer
- [ ] no network echo loop
- [ ] memory remains host-authoritative

---

# 13. Useful Existing Commands

Preserve commands unless deliberately deprecated.

```text
/aistatus
/aitest
/aimodel <model>

/dsims
/dw <Sim> <message>
/dstalk [Sim]
/dsbanter
/dsdirector

/dswiki ...
/dsnews ...

/dsforget <Sim>
/dssession
/dsmemory <Sim>
/dsperf
/dsinspect
/dsguardtest
/dsevents [status|recent|test|on|off|duel on|duel off|cooldown <30-120>]
/dsseeds [status|recent|test|reset]
/dscamp [status|on|off|auto on|auto off]

/dsinference auto
/dsinference cpu [threads]
/dsinference gpu
/dsreasoning off|selective|always

/dsexport
```

Older/debug commands may exist. Check source before removing them.

---

# 14. Preferred Model / Runtime Philosophy

Default target:

```text
Ollama
qwen3.5:2b (recommended default, not a hard requirement)
~2048 context unless measurement justifies more
```

The public mod should remain practical on ordinary gaming PCs.

Favor:

- small prompts
- short outputs
- bounded memory
- one shared request queue
- low idle overhead
- optional network access
- graceful degradation

Do not optimize for perfect prose at the cost of noticeable gameplay hitching.

---

# 15. Definition of Done for a Change

A task is not complete merely because code was added.

A change is done when:

1. It follows the architecture rules above.
2. It compiles in the available development environment, or the exact missing dependency is documented.
3. Existing commands and save compatibility are preserved unless intentionally changed.
4. Failure paths are safe.
5. Debug output is sufficient to validate the feature in game.
6. The user can explain how to test it.
7. README/CHANGELOG/version metadata are updated when appropriate.
8. This `AGENTS.md` checklist is updated if the task status materially changed.

---

# 16. Guiding Product Principle

The best Deep Sims feature is one the player stops noticing is a mod.

A successful Sim should feel like:

```text
a slightly imperfect MMO friend
who knows what just happened,
remembers enough of your shared history,
sometimes has an opinion,
occasionally gets something wrong,
and never takes control away from Erenshor.
```

When choosing between more AI complexity and stronger grounding/cohesion, prefer grounding/cohesion.

### 0.7.x social-expression implementation note

Autonomous social speech now follows:

```text
verified game event / quiet social opportunity
        -> deterministic candidate subjects + topic fatigue vs explicit SILENCE
        -> central social budget
        -> social intent
        -> Auto / LLM / Templates / Off expression routing
        -> grounding + duplicate output boundary
        -> native-style chat
```

The social budget is the authority for autonomous cooldowns and rolling message pressure. Do not add new feature-local autonomous cooldown/chance systems when a new event can be submitted through this boundary.

`ConversationSeeds.cs` owns the separate question "what subject is worth discussing now?" — semantic
`TopicKey` definitions, the bounded transient `TopicFatigueTracker`, the additive scorer, and the
explicit silence candidate. `SocialBudget` still owns "may autonomous speech happen now?". Do not
merge, duplicate, or bypass either responsibility, and do not add a second autonomous director that
can initiate conversations alongside `EventConversationDirector`: ambient evaluation stands down while
that director holds a pending verified candidate. Phases 3–6 of
[docs/CONVERSATION_SEEDING_DESIGN.md](docs/CONVERSATION_SEEDING_DESIGN.md) (generalized
`ConversationSeed`, unified producers, seed-bound chains, Relax/Expedition) remain unimplemented.

COOP remains host-authority only for autonomous Deep Sim speech. Do not add peer election or broad same-zone broadcast unless a future inspected COOP API exposes a safe established party-targeted mechanism.

Practice Duel completed-result reactions use only current eligible Deep Sims and are capped to one post-duel spectator line. Pre/during spectator chatter remains intentionally disabled until participant/presence identity can be verified without speculative reflection.
