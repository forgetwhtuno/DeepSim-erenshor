# Deep Sims 0.8.2 — Control Sentinel + Correlation Repair

## Scope

Only Deep Sims dialogue control normalization and diagnostic correlation changed. Social cadence, SilenceFatigue, speaker selection, seeds, memory, prompts, grounding policy, reflection, curation, and Party/Whisper channel behavior were not changed.

## Behavior

`NO_MESSAGE` remains the only prompt contract. The output boundary now recognizes these whole-response aliases after bounded normalization: `NO MESSAGE`, `NO MESSAGE NEEDED`, `NO REPLY`, `NO REPLY NEEDED`, `NO RESPONSE`, `NO RESPONSE NEEDED`, and `SILENCE`.

Normalization trims whitespace, one balanced outer quote/apostrophe/backtick/bracket/angle-bracket wrapper, terminal `. ! ? : ;`, converts underscores to spaces, and collapses whitespace. It does not use substring matching.

Diagnostics now emit a content-free `requestId`, `attempt`, `threadId`, `requestType`, `speaker`, `candidateHash`, and `disposition`. Grounding retries retain the root request ID and increment `attempt`; RoleplayDiag separately reports `groundingRetryCount`.

Prompt capture enriches packets with correlation fields. It remains opt-in and content capture is not enabled by this change.

## Validation

- `tests/RUN_DETERMINISTIC_TESTS.ps1` — PASS.
- `BUILD_AND_INSTALL.ps1 -BuildOnly` — PASS; no installation performed.

## Live QA after installation

Use `/dspromptcapture on` (and `/dspromptcapture off` when finished) for a controlled local capture. Play normally and inspect `[DeepSims][DialogueCorrelation]` lines for initial generation, grounding rejection/retry, queue, and visible disposition. Control-only replies such as `no message needed`, `no reply needed`, and `silence` must not display.

Do not manipulate cadence or delete/change existing memories while testing.
