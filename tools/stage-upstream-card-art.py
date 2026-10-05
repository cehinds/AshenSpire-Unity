"""Verify and stage the three build-899 paintings without changing Unity assets."""
from pathlib import Path
from io import BytesIO
import hashlib
import json
import sys
import zipfile
from PIL import Image

stage = Path(sys.argv[1]).resolve()
pack = stage / 'hd-assets-v11.zip'
expected_pack = 'b56250b1de8b08cb2ac2d2d423964b82afc2947da1811324bf8e852900a9fd1f'
with pack.open('rb') as stream:
    actual_pack = hashlib.file_digest(stream, 'sha256').hexdigest()
if actual_pack != expected_pack:
    raise ValueError('The art pack does not match the pinned upstream release.')
manifest = json.loads((stage / 'art-manifest.json').read_text('utf-8'))
cards = {'starstonePebble': 'starstone-pebble', 'urgentHeal': 'urgent-heal', 'ambush': 'ambush'}
output = stage / 'verified-paintings'
output.mkdir(exist_ok=True)
rows = []
with zipfile.ZipFile(pack) as archive:
    for card_id, slug in cards.items():
        source = f'assets/cards/{slug}-512.webp'
        expected = manifest['assets'][source]['high']
        raw = archive.read(source)
        digest = hashlib.sha256(raw).hexdigest()
        if digest != expected['sha256'] or len(raw) != expected['bytes']:
            raise ValueError(f'Individual asset verification failed: {source}')
        decoded = Image.open(BytesIO(raw)).convert('RGBA')
        if decoded.size != (expected['width'], expected['height']):
            raise ValueError(f'Asset dimensions do not match: {source}')
        converted = output / f'{card_id}.png'
        decoded.save(converted, optimize=True)
        reopened = Image.open(converted).convert('RGBA')
        if reopened.size != decoded.size or reopened.tobytes() != decoded.tobytes():
            raise ValueError(f'RGBA conversion changed pixels: {source}')
        rows.append({'cardId': card_id, 'source': source, 'sourceSha256': digest,
                     'png': converted.name, 'pngSha256': hashlib.sha256(converted.read_bytes()).hexdigest(),
                     'size': list(decoded.size), 'rgbaPixelsVerified': True})
receipt = {'upstreamCommit': '40c8a45fe951de4f757b2add0811aeeeb7f41fe1',
           'release': 'hd-assets-v11', 'packSha256': actual_pack,
           'stagedOnly': True, 'unityImported': False, 'assets': rows}
(output / 'receipt.json').write_text(json.dumps(receipt, indent=2) + '\n', 'utf-8')
print(json.dumps(receipt, indent=2))
