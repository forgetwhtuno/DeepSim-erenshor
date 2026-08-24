#!/usr/bin/env python3
from pathlib import Path
import argparse, sys
p=argparse.ArgumentParser(description='Verify current Erenshor assembly contains identity/friend members used by Deep Sims.')
p.add_argument('--assembly', required=True)
a=p.parse_args(); path=Path(a.assembly)
if not path.is_file(): print('FAIL: assembly not found'); sys.exit(2)
data=path.read_bytes()
required=['SimPlayerTracking','FriendedBy','IsGMCharacter','CurrentCharacterSlot','CharacterClass']
classes=['Arcanist','Druid','Paladin','Reaver','Stormcaller','Windblade']
missing=[]
for token in required+classes:
    b=token.encode('ascii'); u=token.encode('utf-16le')
    if b not in data and u not in data: missing.append(token)
if missing:
    print('FAIL: missing current assembly tokens: '+', '.join(missing)); sys.exit(1)
print('PASS: current Assembly-CSharp contains Friend/current-character identity surface: '+', '.join(required))
print('PASS: current class-name evidence present: '+', '.join(classes))
