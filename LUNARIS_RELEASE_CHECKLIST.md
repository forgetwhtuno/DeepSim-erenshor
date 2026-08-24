# Deep Sims 0.8.2 Beta Release Checklist

Do not mark the migration release-ready until every applicable item below has been observed on the current Erenshor + Lunaris build.

## A. Build gate

- [ ] `BUILD_AND_INSTALL.ps1` compiles against the current installed `Assembly-CSharp.dll` and Unity assemblies.
- [ ] Build uses the intended local `Lunaris.dll` and prints its hash/version.
- [ ] Build records SHA-256 for every compile reference and the final candidate DLL.
- [ ] No `BepInEx.dll` compile reference is required.
- [ ] Only `ErenshorDeepSims.dll` is installed to `<Erenshor>/plugins`.
- [ ] Lunaris/0Harmony/other runtime libraries are not bundled into the plugin output.
- [ ] `git diff --check` remains clean.
- [ ] `tests/RUN_DETERMINISTIC_TESTS.ps1` passes completely.

## B. Native Lunaris startup

- [ ] Lunaris recognizes Deep Sims as a native Lunaris plugin.
- [ ] Deep Sims loads without BepInEx.
- [ ] No startup exception appears in Lunaris logging.
- [ ] Lunaris config UI exposes the expected Deep Sims settings.
- [ ] ConfigVersion/internal migration state is not presented as a normal player setting.
- [ ] Changing a normal config option in the Lunaris UI is reflected by Deep Sims without restart where expected.
- [ ] Restarting the game preserves changed Lunaris settings.
- [ ] Clean release smoke does not print `Deep Sims local prompt capture is ENABLED`.
- [ ] Public package contains no `.lpcfg`, memory, export, diagnostic, capture, or log files.

## C. Persistence

- [ ] New memory lives under `<Erenshor>/plugins/config/DeepSims/Memory/Characters/<character-key>`.
- [ ] New memory survives game restart.
- [ ] With an empty new memory directory and legacy direct-install memory present, the conservative legacy memory copy works.
- [ ] Legacy files are not deleted.
- [ ] Existing new-format memory is never overwritten by the legacy-copy path.
- [ ] Erenshor save files are untouched.

## D. Core commands

Verify all existing command grammar still behaves as before:

- [ ] `/aistatus`
- [ ] `/dsims`
- [ ] `/dssession`
- [ ] `/dsperf`
- [ ] `/dsmemory <Sim>`
- [ ] `/dsnews ...`
- [ ] `/dsxnews ...`
- [ ] `/dssocial ...`
- [ ] `/dsroleplay on|off|status`
- [ ] `/dsinference ...`
- [ ] `/dsreasoning ...`
- [ ] `/dsevents ...`
- [ ] `/dsseeds ...`
- [ ] `/dscamp ...`
- [ ] `/dsinspect`
- [ ] `/dsguardtest`
- [ ] `/dw ...`
- [ ] `/dstalk ...`
- [ ] `/dsbanter`

## E. Vanilla command safety

Deep Sims must not steal Erenshor gameplay commands:

- [ ] normal `/group` chat still reaches Erenshor;
- [ ] `/group follow` still works;
- [ ] `/group attack` still works;
- [ ] other native command-bearing group phrases still work;
- [ ] normal whispers work;
- [ ] reply/whisper behavior remains correct;
- [ ] Deep Sims-generated lines do not recurse back into command handling.

## F. Social expression modes

- [ ] `Auto` uses LLM when healthy where appropriate.
- [ ] `Auto` degrades safely when Ollama is unavailable.
- [ ] `Templates` works with Ollama completely stopped.
- [ ] `LLM` works with Ollama running.
- [ ] `Off` suppresses autonomous social expression as designed.
- [ ] SocialBudget/cooldowns still prevent spam.
- [ ] `NO_MESSAGE` remains a valid quiet outcome.

## G. Roleplay perspective

### MMO default

- [ ] fresh/native config starts in MMO perspective.
- [ ] MMO perspective retains the existing typed-MMO-player voice.
- [ ] MMO template behavior is unchanged.

### Roleplay

- [ ] `/dsroleplay on` switches perspective without changing social frequency.
- [ ] `/dsroleplay off` restores MMO perspective.
- [ ] setting persists in Lunaris config.
- [ ] Roleplay autonomous lines do not use XP/DPS/reroll/NPC/wiki/server/game framing.
- [ ] Roleplay output contains spoken words only, not `*actions*` or self-narration.
- [ ] deterministic Roleplay templates work with Ollama off.
- [ ] generated lines that violate Roleplay voice are salvaged once or become `NO_MESSAGE`, not retried in a loop.
- [ ] class affinity lines never assert faction membership/religion/upbringing/history.
- [ ] faction topics appear only after verified standing exposure exists.
- [ ] native typing personalization does not reintroduce `lol`, text faces, or similar typed-chat texture into accepted Roleplay speech.

## H. Grounding and memory

- [ ] generated dialogue never becomes verified game history.
- [ ] player claims remain heard/conversational context, not automatically verified history.
- [ ] current encounter, last completed encounter, and outing totals remain separated.
- [ ] temporal phrases such as `again`, `last time`, and `remember when` still require support.
- [ ] verified duel/event memories still persist correctly.
- [ ] external real-world news remains conversation-scoped and does not become Erenshor lore/personal history.

## I. Zone / lifecycle

- [ ] load character normally.
- [ ] zone once.
- [ ] zone repeatedly.
- [ ] return to Port Azure.
- [ ] party membership reacquires correctly after zoning.
- [ ] no stale Sim/GameObject references throw after zoning.
- [ ] disconnect/reconnect works.
- [ ] normal Erenshor save behavior is unaffected.

## J. COOP / actor authority

Only claim compatibility after live validation:

- [ ] no COOP installed: normal behavior.
- [ ] COOP installed, configured host: Deep Sims owns social generation only where intended.
- [ ] COOP client/non-authority case fails closed as designed.
- [ ] remote humans are not treated as local Sims.
- [ ] no invented network replication path was introduced.

## K. Optional integrations

Test absence first:

- [ ] no Campmaster: startup succeeds.
- [ ] no Practice Duels: startup succeeds.
- [ ] no PvP: startup succeeds.
- [ ] no Nemesis: startup succeeds.
- [ ] no Follow: startup succeeds.

Then test each integration when installed:

- [ ] Campmaster events/context remain read-only and grounded.
- [ ] Practice Duel verified events still reach Deep Sims where supported.
- [ ] PvP integration remains optional/version-safe.
- [ ] Nemesis integration remains optional/version-safe.
- [ ] Follow-related integration remains optional/version-safe.

## L. Critical Lunaris hot-unload test

Run this exact sequence:

1. [ ] Start Erenshor with Deep Sims enabled.
2. [ ] Join a party with at least two Deep Sims.
3. [ ] Produce normal Deep Sims chat.
4. [ ] Trigger an Ollama request that will still be running for a moment.
5. [ ] While the request is pending, disable/unload Deep Sims through Lunaris.
6. [ ] Verify **no late Deep Sim response appears** after unload.
7. [ ] Verify there is no exception/error spam.
8. [ ] Verify native gameplay/chat continues.
9. [ ] Reload/enable Deep Sims.
10. [ ] Verify exactly one active plugin instance appears behaviorally.
11. [ ] Run `/aistatus` and `/dsims`.
12. [ ] Trigger another social event and verify only one reaction path exists.
13. [ ] Repeat unload/reload at least 3–5 times.
14. [ ] Verify no duplicated chat.
15. [ ] Verify no duplicated event reactions.
16. [ ] Verify no growing notification count.
17. [ ] Verify no stale worker output from prior plugin instances.
18. [ ] Verify optional COOP/Campmaster detection still works after reload (the new instance must safely rebind its assembly-load hooks).
19. [ ] Verify memory continues to write normally after reload.

Do not publish the native migration as fully hot-reload-safe until this sequence passes.

## M. Uninstall

- [ ] default `UNINSTALL.ps1` removes only `ErenshorDeepSims.dll`.
- [ ] config/memory are preserved by default.
- [ ] `-RemoveData` removes only Deep Sims-owned Lunaris config/data.
- [ ] Erenshor saves are never removed.

## Final release gate

- [ ] Full compile passes.
- [ ] Deterministic test suite passes.
- [ ] Core command matrix passes.
- [ ] Vanilla gameplay-command safety passes.
- [ ] Zone lifecycle passes.
- [ ] Pending-request hot unload passes.
- [ ] 3–5 repeated reloads show no duplication/leak symptoms.
- [ ] README/install instructions match the actually tested Lunaris version/path.
- [ ] Version/changelog are finalized only after the above validation.
