#!/usr/bin/env python3
from pathlib import Path
import re, sys

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src"
failures=[]
def need(cond,msg):
    if not cond: failures.append(msg)
def text(name): return (SRC/name).read_text(encoding='utf-8-sig')

ui=text('IdentityEditorUi.cs')
defaults=text('IdentityDefaults.cs')
ctx=text('IdentityContext.cs')
prompt=text('PromptBuilder.cs')
store=text('MemoryStore.cs')
plugin=text('DeepSimsPlugin.cs')
transfer=text('IdentityProfileTransfer.cs')
models=text('Models.cs')
reader=text('SimContextReader.cs')

need('OnGUI' not in ui and 'GUILayout' not in ui, 'production identity editor must be retained uGUI only')
for token in ('Canvas','ScrollRect','InputField','Dropdown','CanForget','Copy → Pinned','Reset All','Shared participants'):
    need(token in ui, f'identity editor missing {token}')
for token in ('overrideSorting = true', 'sortingOrder = 800', 'r.anchorMin = new Vector2(0f, 1f)', 'r.anchorMax = new Vector2(1f, 1f)', 'r.pivot = new Vector2(.5f, 1f)', 'r.sizeDelta = new Vector2(0f, height)', '_content.anchoredPosition = Vector2.zero'):
    need(token in ui, f'identity editor first-row/viewport containment guard missing: {token}')
need('viewport.gameObject.AddComponent<RectMask2D>()' in ui,
     'identity editor viewport does not own rectangular clipping for every retained field descendant')
for token in ('_simDropdown.ClearOptions()', '_simDropdown.AddOptions(names)', 'GetIdentityEditorSims()',
              'template.anchorMin = new Vector2(0f, 0f)', 'template.anchorMax = new Vector2(1f, 0f)',
              'content.sizeDelta = new Vector2(0f, 28f)', 'item.anchorMin = new Vector2(0f, .5f)',
              'item.anchorMax = new Vector2(1f, .5f)'):
    need(token in ui or token in plugin, f'identity selector population/popup geometry contract missing: {token}')
need('MakeDropdown(selectorRow' in ui and 'MakeDropdown(_content' not in ui,
     'identity dropdown popup is owned by scrolling content and can be clipped')
need('IdentityEditorUi.Initialize(this)' in plugin and 'new FallbackAction("Identity Editor", OpenIdentityEditor' in plugin, 'editor is not reachable from existing retained Deep Sims UI')
need('IdentityEditorUi.Open(target)' in plugin and 'Open(SimSnapshot preferred)' in ui, '/dsidentity <Sim> editor does not preserve the requested selection')
need('IdentityEditorUi.OnCharacterScopeChanged()' in plugin, 'character-scope switch does not invalidate identity editor')

classes=['Arcanist','Druid','Paladin','Reaver','Stormcaller','Windblade']
for c in classes: need(f'"{c}"' in defaults, f'missing current class default: {c}')
for forbidden in ('born in','dead siblings','member of the order','academy graduate','served in the war'):
    # Forbidden phrases are allowed only inside the explicit safety detector, never in generated template assignments.
    assignments='\n'.join(line for line in defaults.splitlines() if 'result.' in line or 'return "' in line)
    need(forbidden not in assignments.lower(), f'default template appears to claim unsupported specific history: {forbidden}')
need('StableHash(stableKey)' in defaults and '2166136261u' in defaults and '16777619u' in defaults, 'stable deterministic identity hash is missing')

need('IdentityValueSource.DefaultTemplate' in ctx and 'IdentityValueSource.AuthoredOverride' in ctx, 'prompt context does not preserve default/authored sources')
need('DEFAULT IDENTITY TEMPLATE' in prompt and 'AUTHOR-DEFINED IDENTITY' in prompt, 'prompt does not label default/authored identity separately')
need('VERIFIED LEARNED HISTORY > DEFAULT IDENTITY TEMPLATE' in prompt, 'prompt trust order does not place verified learned history above defaults')
need('Your fictional life outside Erenshor:' in ctx and '!roleplay' in ctx,
     'prompt-facing simulated-player background is not explicitly fictional and MMO-only')
need('OOC PLAYER-IDENTITY QUESTION' in prompt and "Keep the answer about the fictional player's life outside Erenshor" in prompt,
     'MMO out-of-character identity questions can still be answered from character state')
need('latestIdentityTopic = newestMessage' in prompt, 'compact reply builder does not route the actual latest player turn')
need('Friend to current player character:' in prompt and 'verifiedCurrentCharacterFriend=' in prompt, 'verified Friend state is not available as live prompt context')

for token in ('TryResetAllAuthoredIdentity','TryForgetLearnedMemory','TryCopyLearnedToPinned','TryUpdateAuthoredMemory','TryRemoveAuthoredMemory'):
    need(token in store, f'memory ownership operation missing: {token}')
need('Learned memory cannot be edited in place' in store, 'learned memory can be silently rewritten as learned')
need('First observed by Deep Sims around ' in store and 'First adventured with the player around ' not in store, 'first-seen bootstrap still fabricates an adventure')
need('authored_history' in store and 'authored_shared_history' in store, 'local history and shared history are not semantically distinguishable')
need('AuthoredHistory' in text('IdentityEditorModel.cs') and 'IdentityMemorySection.AuthoredHistory' in text('IdentityEditorModel.cs'), 'editor model does not keep local authored history separate from shared history')
need('IsSharedHistoryRecord' in text('IdentityEditorModel.cs') and 'player_authored_shared' in text('IdentityEditorModel.cs'), 'legacy single-Sim history is not distinguished from true multi-Sim shared history')

need('SchemaVersion' in transfer and 'CurrentSchemaVersion = 2' in transfer, 'identity import/export schema version missing')
for prohibited in ('Conversation','PromptCapturePacket','PromptCaptureModel','MachinePath'):
    # Ignore explanatory comments; the serialized document itself must not contain these fields.
    doc=re.search(r'class IdentityProfileTransferDocument\s*\{([\s\S]*?)\n\s*\}', transfer)
    need(doc is not None and prohibited not in doc.group(1), f'identity transfer document exposes prohibited field {prohibited}')
need('DeepSimsPaths.ExportDirectory' in transfer and 'C:\\Users' not in transfer, 'identity transfer uses an unsafe/machine-specific path')
need('FriendStateKnown' in models and 'IsFriend' in models, 'snapshot friend provenance fields missing')
need('tracking.FriendedBy == currentSlot' in reader and '!tracking.IsGMCharacter' in reader, 'current-character native friend classification missing')

# Stable FNV-1a behavior mirror: same identity stable, distinct sample identities vary.
def fnv(s):
    h=2166136261
    for ch in s:
        h ^= ord(ch); h=(h*16777619)&0xffffffff
    return h
def bits(k): return fnv(k)&63
need(bits('dancer-key') == bits('dancer-key'), 'stable identity hash mirror failed')
need(bits('dancer-key') != bits('cyndara-key'), 'sample stable identities unexpectedly collapse to the same bounded style variation')


tests=text('IdentityContextDeterministicTests.cs')
live_tests=text('LiveSocialQualityDeterministicTests.cs')
for phrase in (
    'layered identity serializes and reloads',
    'authored shared history outranks conflicting learned memory',
    'authored shared-history known-by ownership blocks an uninformed Sim',
    'pinned authored memory receives deterministic retrieval priority',
    'unrelated modern background is excluded from roleplay',
    'modern personal background remains hidden even when directly asked in roleplay',
    'relevant personal background remains available in MMO perspective',
    'personal taste/background questions never trigger lookup',
    'character switching does not leak authored identity between scopes',
    'malformed saved data fails closed to a fresh bounded record'):
    need(phrase in tests, f'deterministic identity coverage missing: {phrase}')
for phrase in (
    'MMO generated background created once',
    'MMO generated background stable',
    'IRL question uses simulated-player layer',
    'authored player background wins',
    'Roleplay mode hides MMO background',
    'identity UI resolution math stays onscreen',
    'fresh Sim prompt establishes generated background without editor interaction',
    'editor observes the same prompt-established generated background',
    'Reset restores the original persisted generated background',
    'prompt-established generated background persists unchanged'):
    need(phrase in live_tests, f'0.8.2 live-social identity coverage missing: {phrase}')
for phrase in (
    'generated wants and cares are complete',
    'generated wants and cares are stable',
    'generated motivations reach prompt without editor',
    'roleplay retains character motivations but hides MMO background',
    'same-class identities retain stable variation',
    'generated relationship excludes mutable Friend state',
    'fresh prompt persists generated motivations before editor',
    'one authored motivation leaves unrelated generated fields unchanged',
    'Reset restores original generated wants and cares',
    'prompt-established generated motivations persist unchanged'):
    need(phrase in live_tests, f'0.8.2 generated identity motivation coverage missing: {phrase}')

# Basic delimiters for edited C# sources (not a compiler, just catches accidental truncation).
for name in ('IdentityDefaults.cs','IdentityEditorModel.cs','IdentityProfileTransfer.cs','IdentityEditorUi.cs','IdentityContext.cs','MemoryStore.cs','PromptBuilder.cs','DeepSimsPlugin.cs','SimContextReader.cs','Models.cs','SimulatedPlayerIdentity.cs','LiveSocialQualityPolicy.cs'):
    s=text(name)
    need(s.count('{')==s.count('}'), f'unbalanced braces in {name}')

need('NativePersonalityAnchor' in defaults, 'native personality authority does not feed default identity')
need('generated defaults only provide light conversational style' in defaults, 'generated defaults are not explicitly subordinate to native personality')
need('DeepSimsIdentityEditorCameraUsingUiPatch' in ui, 'identity editor camera input ownership patch missing')
need('GameData.DraggingUIElement' in ui, 'legacy pointer ownership during identity UI gestures missing')
need('ConfirmDestructive' in ui, 'destructive identity/memory actions do not require explicit confirmation')
need('DestroyImmediate' not in ui, 'runtime identity UI must not use DestroyImmediate')
need('Version = "0.8.2"' in plugin, 'expected Deep Sims 0.8.2 source version missing')

if failures:
    for f in failures: print('FAIL:',f)
    sys.exit(1)
print('PASS: Deep Sims identity editor/default/memory source contracts')
print('PASS: retained uGUI; six current class defaults; stable native-aware deterministic variation')
print('PASS: authored/default/learned authority remains separated')
print('PASS: character-scoped Friend provenance, memory ownership, and local transfer guards')
