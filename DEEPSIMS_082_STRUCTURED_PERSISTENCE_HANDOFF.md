# Deep Sims 0.8.2 structured persistence repair handoff

## Root serialization limitation

The current Unity runtime persisted SimMemory primitives, strings, and `List<string>` fields but omitted every custom reference and `List<custom-type>` field. The affected set included `AuthoredIdentity`, `StructuredMemories`, `RecentEvents`, `Conversation`, `SimRelationships`, `SocialRelationships`, and `Preferences`. Candidate metadata confirms these fields and their element types are public, non-readonly, non-`[NonSerialized]`, and `[Serializable]`; they contain no dictionaries or polymorphic members. Live diagnostics localized loss to `JsonUtility.ToJson` after the live object, writer clone, and pre-JSON object retained nonempty data.

## Repair architecture

SimMemory persistence now uses an explicit `DataContractJsonSerializer` contract. Runtime SimMemory remains unchanged. MemoryStore still owns dirty snapshots, its background writer, temporary files, atomic replacement/fallback, and bounded retry behavior. Only SimMemory read/write conversion changed.

## Compatibility

The DTO preserves all current logical field names. Missing fields in legacy-only JSON deserialize to defaults and are normalized through the existing `SimMemory.Normalize()` migration path. Old data is not bulk-migrated at startup; full-schema JSON is emitted on the next normal dirty write.

## Validation

- Deterministic suite: PASS, exit 0.
- New persistence tests: 11 PASS, covering DTO/runtime field coverage, every structured record field, RecentEvents, two preferences, multiple SimRelationships, SocialRelationships, nested AuthoredIdentity, combined legacy/new state, legacy-only fixture, empty collections, and repeated cycles.
- Contextual social source validator: 21 PASS.
- Identity/source validator: PASS.
- Live social quality source validator: 59 PASS.
- Current native Assembly-CSharp identity validator: PASS.
- Production `BUILD_AND_INSTALL.ps1 -BuildOnly`: PASS.

## Candidate

- `build-output/ErenshorDeepSims.dll`
- SHA-256: `b1dbe80b06398d7713ef156bc712c059822d4865d2f4c4efa79a85b0fb3f919a`
- Not installed.

## Required LIVE validation

Do not call the repair fixed until a current Unity run proves:

1. Install the candidate only while Erenshor is closed and verify its hash.
2. Launch and confirm the `structured-persistence-r1` load marker.
3. Observe nonempty `live`, `clone`, `dto`, `pre_json`, serialized, and disk checkpoints.
4. Verify the six structured field names and `Conversation` exist on disk.
5. Exit normally, restart, and confirm `phase=reload` restores the same structured counts.
6. Confirm legacy fields remain present and no records multiply.

Unrelated cognition work was intentionally excluded: pre-visibility state ordering, generic capture fields, ambient seed diagnostic naming, curation policy, reflection structure, summaries, seed expansion, cancellation, and scheduling.
