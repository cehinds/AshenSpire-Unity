"""Import the verified v11 starter paintings; keep profile-specific artwork precedence."""
from pathlib import Path
import hashlib
import json
import subprocess
import sys

root = Path(__file__).resolve().parent.parent
stage = Path(sys.argv[1]).resolve()
# Recheck the pinned pack, individual source hashes and decoded pixels before any Unity write.
subprocess.run([sys.executable, str(root / 'tools/stage-upstream-card-art.py'), str(stage)], check=True, capture_output=True)
verified = stage / 'verified-paintings'
receipt = json.loads((verified / 'receipt.json').read_text('utf-8'))
resources = root / 'Unity/Assets/AshenSpire/Resources'
target = resources / 'Art/upstream913'
target.mkdir(exist_ok=True)
guid = lambda key: hashlib.sha256(('upstream913:' + key).encode()).hexdigest()[:32]
folder_meta = target.with_suffix('.meta')
if not folder_meta.exists():
    folder_meta.write_text(f'fileFormatVersion: 2\nguid: {guid("folder")}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n', 'utf-8')
template = (resources / 'Art/background1.png.meta').read_text('utf-8')
old_guid = next(line.split(': ')[1] for line in template.splitlines() if line.startswith('guid:'))
layout_path = resources / 'Original/illustrated-reference.json'
layout = json.loads(layout_path.read_text('utf-8'))
for row in receipt['assets']:
    card_id = row['cardId']
    png = (verified / row['png']).read_bytes()
    if hashlib.sha256(png).hexdigest() != row['pngSha256']:
        raise ValueError('Staged image changed during import: ' + card_id)
    resource = 'Art/upstream913/' + card_id
    destination = target / (card_id + '.png')
    destination.write_bytes(png)
    meta = destination.with_suffix('.png.meta')
    if not meta.exists():
        meta.write_text(template.replace(old_guid, guid(card_id)).replace('enableMipMap: 1', 'enableMipMap: 0').replace('nPOTScale: 1', 'nPOTScale: 0').replace('textureCompression: 1', 'textureCompression: 0'), 'utf-8')
    layout['artwork'][card_id] = {'path': row['source'], 'kind': 'official', 'equipment': False, 'resource': resource}
    row['resource'] = resource
layout.setdefault('upstreamArtOverrides', {})['build913'] = {
    'commit': receipt['upstreamCommit'], 'release': receipt['release'],
    'packSha256': receipt['packSha256'], 'cardIds': [r['cardId'] for r in receipt['assets']]}
layout_path.write_text(json.dumps(layout, indent=2) + '\n', 'utf-8')
receipt['stagedOnly'] = False
receipt['unityImported'] = True
receipt['compiledPlayerVerified'] = False
evidence = root / 'TestResults/HtmlParity/upstream913'
evidence.mkdir(parents=True, exist_ok=True)
(evidence / 'starter-art-import.json').write_text(json.dumps(receipt, indent=2) + '\n', 'utf-8')
print(json.dumps({'imported': len(receipt['assets']), 'release': receipt['release'], 'rgbaPixelsVerified': True}))
