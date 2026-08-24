# Deep Sims 0.8.2 cognition observability handoff

## Scope

This diagnostic beta continuation adds passive, privacy-safe observations to the existing cognition and persistence paths. It does not change social cadence, prompts, seed ranking, reflection cadence, curation policy, or serializer choice.

## Persistence forensics

Each dirty Sim write now records collection counts at live cache, writer clone, and pre-`JsonUtility.ToJson`, then exact serialized JSON field-presence/size and optional post-replacement disk field-presence/size. Sim identity is a stable diagnostic hash. No memory, identity, prompt, or conversation text is logged.

This does **not** fix or claim to fix the structured-memory serializer defect. The next installed live run must determine the first boundary where counts or field presence disappear.

## Cognition observability

The existing opt-in PromptCapture infrastructure now supports `context_pulse`, `autonomous_opener`, `session_reflection`, and `social_curation`. Packets retain lane, character/session generation, conversation generation, source/correlation IDs, seed metadata, attempts, model configuration, grounding/retry result, and final visibility where applicable.

Autonomous opener packets remain pending after queue acceptance and complete only when the existing final output boundary reports displayed or a bounded rejection/clear disposition. Diagnostics separately report whether topic fatigue, conversation moment, preference, or callback state advanced before visibility.

Content-free normal diagnostics cover verified event/seed correlation, Recent Life generated/stored/already-stored/knowledge-denied/expired/selected counters, reflection eligibility/result lengths, and curation returned/accepted/rejected/store counts with bounded rejection categories.

## Validation

- `tests/RUN_DETERMINISTIC_TESTS.ps1`: PASS (exit 0).
- `tests/verify_contextual_social_source.py`: PASS (21 checks).
- `tests/verify_identity_editor_source.py`: PASS.
- `tests/verify_live_social_quality_source.py`: PASS (59 checks).
- `tests/verify_current_identity_assembly.py` against the current installed `Assembly-CSharp.dll`: PASS.
- `BUILD_AND_INSTALL.ps1 -BuildOnly`: PASS.
- Candidate SHA-256: `3a0b1c149b10c4f9f964e86b1a7c0043cca863d9fa4b9582aedbbd2e537db3d5`.
- Installation was not performed. The installed DLL remains the prior nonmatching build.

## Required live test

Install only through the normal workflow when explicitly authorized, enable PromptCapture only through its existing opt-in control, and observe one naturally occurring cognition cycle. Correlate:

`live structured count -> clone count -> pre-JSON count -> JSON field presence -> disk field presence`

Then collect any naturally produced `context_pulse`, `autonomous_opener`, `session_reflection`, and `social_curation` packets plus the bounded event/seed and queued/visible diagnostics. Stop before redesigning cognition or changing the serializer.
