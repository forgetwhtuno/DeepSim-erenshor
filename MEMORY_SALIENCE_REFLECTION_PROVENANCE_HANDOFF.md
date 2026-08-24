# Deep Sims 0.8.2 — Memory Salience + Reflection Provenance Repair

## Scope

- Preserves existing legacy `ImportantMemories` and their seed eligibility.
- Does not change scheduling, cadence, speaker selection, connected replies, Ollama settings, or player memory files.

## Repair

- Autonomous prompt memory retrieval now uses the selected seed's verified context instead of operational situation text when a seed exists. An unrelated rolling-summary name can no longer make an old legacy memory appear relevant to that opener.
- Reflection instructions preserve `PLAYER`, `SIM`, and verified-event attribution.
- A bounded deterministic guard rejects a reflection summary that makes a strong player-attribution claim without topical `PlayerSaid` evidence in its reflection delta.

## Validation

- `tests/RUN_DETERMINISTIC_TESTS.ps1` passed, including nine new memory-salience and reflection-provenance assertions.
- `BUILD_AND_INSTALL.ps1 -BuildOnly` passed.

## Candidate

- `build-output/ErenshorDeepSims.dll`
- SHA-256: `13c48803a7b66051417115fcd7dcbecd212abe4eeca978f642e77aabda4d8582`
- The candidate was not installed.

## Live QA after a normal installation

1. Keep the existing Scrubby PvP memory intact.
2. Trigger an unrelated autonomous social seed such as level/progression; verify Scrubby is absent from the captured prompt and output.
3. Trigger a Scrubby/PvP-relevant seed; verify the existing memory remains available.
4. Let a Sim ask a player-belief question without player response; verify reflection does not claim player confirmation/preference.
5. Have the player explicitly state a preference or answer; verify an attributed reflection remains eligible.
