#!/usr/bin/env python3
from pathlib import Path
import re, sys
root=Path(__file__).resolve().parents[1]
project=root.parent
fail=[]; checks=0

def need(path, pattern, desc, flags=0):
    global checks
    checks+=1
    text=(root/path).read_text(encoding='utf-8', errors='replace')
    if re.search(pattern,text,flags) is None: fail.append(desc)

def sibling(path): return (project/path).read_text(encoding='utf-8', errors='replace')

# Version and current sibling contracts.
need(Path('src/DeepSimsPlugin.cs'), r'PluginVersion\s*=\s*"0\.8\.2"', 'Deep Sims version is not 0.8.2')
need(Path('src/DeepSimsPlugin.cs'), r'AssemblyVersion\("0\.8\.2\.0"\).*?AssemblyFileVersion\("0\.8\.2\.0"\)', 'assembly version surfaces are not 0.8.2', re.S)
need(Path('BUILD_AND_INSTALL.ps1'), r'Deep Sims 0\.8\.2 Beta', 'PowerShell build surface is not 0.8.2 Beta')
need(Path('BUILD_AND_INSTALL.bat'), r'Deep Sims 0\.8\.2 Beta', 'batch build surface is not 0.8.2 Beta')
need(Path('README.md'), r'^# Deep Sims for Erenshor 0\.8\.2 Beta', 'README current heading is not 0.8.2 Beta', re.M)
need(Path('AGENTS.md'), r'Current development line:\*\* 0\.8\.x.*?Current development baseline:\*\* 0\.8\.2 public-beta release candidate', 'repository guidance current version surface is stale', re.S)
for client in ('OfficialNewsClient.cs','WikiClient.cs','ExternalNewsClient.cs'):
    need(Path('src')/client, r'ErenshorDeepSims/0\.8\.2 \(\+local Lunaris mod\)', client+' user-agent version surface is stale')
duel=sibling(Path('Erenshor-Duel/src/DuelEventContract.cs'))
for pat,desc in [(r'ContractVersion\s*=\s*4','Practice Duel contract is not v4'),(r'RequestId','Practice Duel RequestId missing'),(r'DuelId','Practice Duel DuelId missing'),(r'ParticipantA','Practice Duel ParticipantA missing'),(r'ParticipantB','Practice Duel ParticipantB missing'),(r'Winner','Practice Duel Winner missing'),(r'Yielded','Practice Duel Yielded missing')]:
    checks+=1
    if re.search(pat,duel) is None: fail.append(desc)
camp=sibling(Path('Erenshor-Campmaster/src/CampmasterApi.cs'))
for pat,desc in [(r'SchemaVersion\s*=\s*3','Campmaster schema !=3'),(r'SocialContractVersion\s*=\s*1','Campmaster social contract !=1'),(r'GetLivingEventsAfter','Campmaster living stream missing'),(r'GetCurrentSocialContext','Campmaster social context missing')]:
    checks+=1
    if re.search(pat,camp) is None: fail.append(desc)

# Duel bridge: exact v4, correlation, dedupe before episode, witness + memory + diagnostics.
need(Path('src/DuelV4SemanticBridge.cs'), r'DuelV4ContractPolicy\.AcceptsContract\(contract\)', 'Duel bridge does not exact-gate v4')
need(Path('src/DuelV4SemanticBridge.cs'), r'Fingerprint\(requestId, duelId', 'Duel bridge lacks v4 correlation fingerprint')
need(Path('src/DuelV4SemanticBridge.cs'), r'if \(!Remember\(fingerprint\)\).*?return;', 'Duel duplicate does not fail before episode', re.S)
need(Path('src/DuelV4SemanticBridge.cs'), r'GetLocalSocialWitnesses\(\).*?RecordLivingSocialEpisode', 'Duel bridge does not apply local witnesses', re.S)
need(Path('src/LiveSocialQualityPolicy.cs'), r'!runtimeLoaded \|\| string\.IsNullOrWhiteSpace\(activeScene\) \|\| string\.IsNullOrWhiteSpace\(simScene\)', 'Duel witness policy does not fail closed on missing scene evidence')
need(Path('src/LivingSocialSimulation.cs'), r'AddUnique\(episode\.Participants, participants\[i\]\);.*?AddUnique\(episode\.KnownBy, participants\[i\]\);', 'verified participants are not explicitly KnownBy', re.S)
need(Path('src/LivingSocialSimulation.cs'), r'episode\.CharacterScope = _scope', 'social episode is not bound to current character scope')
need(Path('src/DuelV4SemanticBridge.cs'), r'PersistVerifiedEpisodeMemory.*?practice_duel_result', 'Duel result not persisted as factual social memory', re.S)
need(Path('src/DeepSimsPlugin.cs'), r'duel_social_event_received requestId=.*?duelId=.*?knownByCount=.*?episodeCreated=.*?dedupeReason=', 'bounded duel diagnostic missing', re.S)
need(Path('src/DuelSocialIntegration.cs'), r'!DuelV4SemanticBridge\.IsBound && DuelSocialPolicy\.ShouldPersistMemory', 'legacy Duel persistence can duplicate v4')

# Camp living facts.
need(Path('src/CampmasterBridge.cs'), r'SupportedSocialContractVersion\s*=\s*1', 'Camp bridge does not exact-gate social v1')
need(Path('src/CampmasterBridge.cs'), r'string\.Equals\(evt\.SessionId, contextSession', 'Camp living zone not session-joined safely')
need(Path('src/DeepSimsPlugin.cs'), r'type == "camp_watch_event".*?PersistVerifiedEpisodeMemory', 'Camp watch does not reach durable factual memory', re.S)
need(Path('src/DeepSimsPlugin.cs'), r'CampEventIsKnownCrossZone', 'Camp cross-zone stale guard missing')
need(Path('src/DeepSimsPlugin.cs'), r'NotifyObservedGameEvent\("camp_watch_event", detail', 'Camp watch not eligible as social seed')

# Identity layer / UI.
need(Path('src/Models.cs'), r'GeneratedSimulatedPlayerBackground', 'persisted generated simulated-player background missing')
need(Path('src/SimulatedPlayerIdentity.cs'), r'GeneratorVersion\s*=\s*"mmo-background-v1"', 'generated background version missing')
need(Path('src/IdentityContext.cs'), r'!roleplay.*?PersonalBackground', 'roleplay/MMO background separation missing', re.S)
need(Path('src/PromptBuilder.cs'), r'fictional human MMO player at a computer', 'MMO prompt does not establish player-behind-character identity')
need(Path('src/IdentityEditorUi.cs'), r'SetAnchors\(_panel, new Vector2\(\.04f, \.05f\), new Vector2\(\.96f, \.95f\)', 'identity panel is not resolution-independent')
need(Path('src/IdentityEditorUi.cs'), r'ContentSizeFitter', 'identity content does not auto-height')
need(Path('src/IdentityEditorUi.cs'), r'view\.SourceLabel', 'identity generated/authored status missing')
need(Path('src/IdentityEditorUi.cs'), r'viewport\.gameObject\.AddComponent<RectMask2D>\(\)', 'identity viewport does not own descendant clipping')
need(Path('src/SimulatedPlayerIdentity.cs'), r'class GeneratedIdentityMotivationPolicy.*?GeneratedLongTermWants.*?GeneratedCaresAbout', 'stable generated wants/cares persistence missing', re.S)
need(Path('src/IdentityDefaults.cs'), r'WantsTemplate\(cls, hash\).*?CaresTemplate\(cls, hash\)', 'generated motivation bundle is not tied to stable identity hash', re.S)
need(Path('src/SessionTelemetry.cs'), r'!gameplayReady \|\| !VerifiedOutingHistoryPolicy\.IsDurableGameplayLocation', 'outing start/update is not gated by gameplay authority')
need(Path('src/SessionTelemetry.cs'), r'across multiple verified areas', 'multi-zone outing still attributes whole duration to one zone')
need(Path('src/SessionTelemetry.cs'), r'CompetitiveCombatOwnsGenericProxy', 'PvP/Duel generic telemetry isolation missing')
need(Path('src/DeepSimsPlugin.cs'), r'ShouldSuppressGenericCombatEvent\(type\).*?return;', 'competitive proxy can still enter generic social director/session path', re.S)
need(Path('src/PvpSocialIntegration.cs'), r'NotifyCompetitiveCombatSemantic\("pvp"', 'PvP semantic owner does not isolate generic telemetry')
need(Path('src/DuelSocialIntegration.cs'), r'NotifyCompetitiveCombatSemantic\("duel"', 'Practice Duel semantic owner does not isolate generic telemetry')

# Dedupe / performance.
need(Path('src/DeepSimsPlugin.cs'), r'_conversationEvidenceDedupe\.Observe', 'chat evidence dedupe not wired')
need(Path('src/DeepSimsPlugin.cs'), r'\[repeated x" \+ line\.RecurrenceCount', 'recurrence metadata not retained in prompt context')
need(Path('src/DeepSimsPlugin.cs'), r'TryEnterLowPriorityInferenceAsync\(RequestLane\.Curation\)', 'curation does not yield to player inference')
need(Path('src/DeepSimsPlugin.cs'), r'TryEnterLowPriorityInferenceAsync\(RequestLane\.Reflection\)', 'reflection does not yield to player inference')
need(Path('src/DeepSimsPlugin.cs'), r'InferencePriorityPolicy\.AllowsGroundingRetry\(diagnosticSource, forceMessage\)', 'bounded retry policy not wired')
need(Path('src/InferencePriorityPolicy.cs'), r'if \(source == \"whisper\".*?return true;.*?return false;', 'autonomous retry policy not fail-closed', re.S)
need(Path('src/DeepSimsPlugin.cs'), r'player queue wait last=.*?grounding=.*?low-priority deferred=', '/dsperf quality metrics missing', re.S)

# No production OnGUI in identity editor.
checks+=1
if 'OnGUI(' in (root/'src/IdentityEditorUi.cs').read_text(encoding='utf-8', errors='replace'): fail.append('production Identity UI uses OnGUI')

if fail:
    print('FAIL: live social quality source verification')
    for x in fail: print(' -',x)
    sys.exit(1)
print(f'PASS: live social quality source verification ({checks} checks)')
