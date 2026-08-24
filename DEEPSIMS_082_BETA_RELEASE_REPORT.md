# Deep Sims 0.8.2 Beta identity/history hardening report

Date: 2026-08-22

## Decision

`BLOCKED` only on the bounded A-I live QA pass below. Source, deterministic,
native-reference, build, and install gates pass. This exact candidate has not yet
received LIVE-PROVEN UI/identity/PvP behavior, so later certification and public
packaging remain out of scope.

## Candidate identity

- Plugin/file version: `0.8.2.0`
- Built DLL SHA-256: `7ce978a47bd81278bfaecafa51e69dae99f1f635b57610f24fec9bdf64c202f8`
- Installed DLL SHA-256: `7ce978a47bd81278bfaecafa51e69dae99f1f635b57610f24fec9bdf64c202f8`
- Hash match: `true`
- Active installed Deep Sims DLL count: `1`
- Prompt capture: `OFF` (binary setting bytes `03-00`)
- Install state: exact fresh candidate, installed with Erenshor closed

## Root causes and repairs

- Scroll leak: all field parts were descendants of Content, but the viewport used a
  stencil `Mask`; the retained viewport now owns `RectMask2D` clipping for row
  background, label/default text, editor, and Reset descendants.
- Generated identity incompleteness: Wants and Cares had empty defaults and no
  persistence. The stable class/personality-weighted identity bundle now generates
  and persists both fields, with generated provenance and authored precedence.
- Menu zone provenance: SessionTelemetry could start in any scene and always assigned
  the full outing duration to `_currentZone`. Observation now requires native character
  readiness plus a durable gameplay scene; multi-zone duration uses location-neutral
  wording.
- PvP/generic correlation: native low-health and kill/death proxies could enter generic
  telemetry during semantic competitive combat. PvP and Practice Duel lifecycles now
  temporarily own and suppress those generic proxies; hostile interruption returns
  ownership to ordinary combat.
- Autonomous scheduling: the existing direct-player admission/preemption repair remains
  unchanged and passing.

## Migration

- Existing generated simulated-player backgrounds are preserved byte-for-byte and are
  never regenerated.
- Missing generated Wants/Cares are deterministically backfilled from stable Sim identity;
  non-empty generated values are preserved.
- Authored identity fields remain authoritative and are never overwritten.
- Reset clears only authored overrides and reveals the same persisted generated defaults.
- Legacy pseudo-location outing strings are left stored as-is but sanitized at every
  prompt/retrieval/grounding surface; only the invalid location clause is removed and no
  replacement zone is guessed.

## Validation completed

- `tests/RUN_DETERMINISTIC_TESTS.ps1`: PASS, exit `0`.
- Core regression summary: `244 PASS, 0 FAIL`.
- `tests/verify_identity_editor_source.py`: PASS.
- `tests/verify_live_social_quality_source.py`: PASS, `59` checks.
- `tests/verify_current_identity_assembly.py`: PASS against current installed native surfaces.
- Fresh native compile against current installed references: PASS.

## Compile reference SHA-256

| Reference | SHA-256 |
|---|---|
| `Lunaris.dll` | `5a70f3d1fd9441ceae6d8e1f80cafce86ff2a47245fbcfa36bfcf8e88fd20b29` |
| `0Harmony.dll` | `c349e1a3fd13fa5a9facc9805a5e160161b14489f46f6bdd38202b8e124f78df` |
| `Assembly-CSharp.dll` | `b840cb8076ed0553f7dc3beb4042aba653917882f763181ec0d2c13c26c17847` |
| `UnityEngine.JSONSerializeModule.dll` | `a5f2e3b3bdc5899db172e964690bc18459eb3ca9dcd989df41c662ceec4834a9` |
| `UnityEngine.dll` | `9a12de40d47d4c054025d79c2243b8abc781a662336af2068440b2cb67d14c1e` |
| `UnityEngine.CoreModule.dll` | `6305c82c17ffe111a016e8d8ca3e8b2d9203980b4f58cb6e90171e5629994290` |
| `UnityEngine.UIModule.dll` | `6935840627678481761e168e19a51e8be837812fc458ae8ebc37d02760e725de` |
| `UnityEngine.TextRenderingModule.dll` | `08af4550bccab28a7d1b59a4ec9690c32e2ac75e19ff4ebbd61e0ef65d9f7778` |
| `UnityEngine.UI.dll` | `8324e202ce45c1fca88e3903c646dd5b70f78d75edc3724efb104b29c97c6897` |
| `netstandard.dll` | `6ae62e082dc494a2433984177f60ca4db5fae69b1f360a8b33754172b310b8c5` |

## Bounded next live QA

A. Open Identity Editor; switch Dancer -> Phanty -> Cyndara; scroll top -> bottom;
   require no stray bar/row/Reset above the viewport.
B. Inspect untouched Phanty and Cyndara; Wants and Cares must say Generated and be
   plausible rather than identical boilerplate.
C. Check Background + Persona + Relationship + Wants + Cares coherence and require no
   invented Erenshor historical/canon fact.
D. Confirm Dancer's authored biology-master background still wins.
E. In MMO perspective ask an untouched generated Sim `what do you do irl?`; require a
   stable fictional-player answer, not quest/grind state.
F. Switch Roleplay; require no MMO work/school/background leak.
G. Run 3-5 direct exchanges; require no stale unrelated autonomous LLM interruption.
H. Run one arranged PvP match in a known playable zone, then ask `how did that fight go?`
   or allow one legitimate reaction; require correct outcome, never Menu, and no duplicate
   generic close-call story.
I. If an ordinary generic close-call line naturally occurs, it may name only a verified
   playable zone.

Stop after A-I. Do not begin character isolation, restart persistence, unload/reload,
standalone, full-suite certification, or public packaging yet.
