"""Import the owner-supplied art kit without importing its scripts or game data.

Usage: python tools/import-player-components.py KIT --resvg MODULE_DIRECTORY
Requires Pillow and @resvg/resvg-js; dependencies stay outside the Unity project.
The receipt records the source and exported bytes. All images keep their aspect.
"""
import argparse
import hashlib
import json
import subprocess
import uuid
from pathlib import Path
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument('kit', type=Path)
parser.add_argument('--resvg', required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
destination = root / 'Unity/Assets/AshenSpire/Resources/Art/PlayerComponents'
destination.mkdir(parents=True, exist_ok=True)
manifest = json.loads((args.kit / 'manifest.json').read_text(encoding='utf-8'))
scene_names = {'spire-vista', 'merchant', 'forge', 'chapel-rest', 'spoils', 'aftermath', 'ruined-courtyard'}
components = {'action-neutral', 'action-ready', 'action-selected', 'action-disabled', 'focus-ring',
              'engraved-folio-backed', 'menu-window', 'settings-section', 'stat-chip',
              'portrait-mat', 'item-mat', 'cost-hexagon', 'hud-rail', 'combat-intent',
              'mobile-action-tray', 'turn-orb', 'chamber-known', 'chamber-current',
              'chamber-visited', 'chamber-unknown', 'selection-ring'}
receipt = []
template = (root / 'Unity/Assets/AshenSpire/Resources/Art/OwnerAppearance/card-attack.png.meta').read_text()
for asset in manifest['assets']:
    source = args.kit / asset['file']
    name = source.stem
    is_scene = any(name == f'{scene}-{orientation}' for scene in scene_names for orientation in ('desktop', 'mobile'))
    is_item = '/canonical/assets/equipment/' in asset['file'] or '/canonical/assets/relics/' in asset['file']
    if not (name in components or is_scene or is_item):
        continue
    source.resolve().relative_to(args.kit.resolve())
    source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    if source_hash != asset['sha256']:
        raise ValueError(f'Source checksum mismatch: {source}')
    output = destination / (name + '.png')
    if source.suffix == '.svg':
        script = "const fs=require('fs');const {Resvg}=require(process.argv[1]);fs.writeFileSync(process.argv[3],new Resvg(fs.readFileSync(process.argv[2]),{fitTo:{mode:'zoom',value:2}}).render().asPng());"
        subprocess.run(['node', '-e', script, args.resvg, str(source), str(output)], check=True)
    else:
        # Convert WebP for Unity's native importer. Never resize either dimension independently.
        with Image.open(source) as image:
            image.convert('RGBA' if asset.get('hasTransparency') else 'RGB').save(output)
    meta = output.with_suffix('.png.meta')
    if not meta.exists():
        import re
        text = re.sub(r'guid: [a-f0-9]+', 'guid: ' + uuid.uuid5(uuid.NAMESPACE_URL, 'ashen-player-components/' + name).hex, template, count=1)
        text = text.replace('maxTextureSize: 8192', 'maxTextureSize: 2048').replace('alphaIsTransparency: 0', 'alphaIsTransparency: 1')
        text = text.replace('wrapU: 0', 'wrapU: 1').replace('wrapV: 0', 'wrapV: 1')
        meta.write_text(text, encoding='utf-8')
    with Image.open(output) as image:
        dimensions = list(image.size)
    receipt.append({'id': asset['id'], 'source': asset['file'], 'sourceSha256': source_hash,
                    'resource': 'Art/PlayerComponents/' + name, 'dimensions': dimensions,
                    'sha256': hashlib.sha256(output.read_bytes()).hexdigest(),
                    'nineSliceInsets': asset.get('nineSliceInsets')})
docs = root / 'docs/art/player-components'
docs.mkdir(parents=True, exist_ok=True)
(docs / 'import-receipt.json').write_text(json.dumps({'schema': 1, 'sourceKit': args.kit.name, 'assets': receipt}, indent=2) + '\n', encoding='utf-8')
for name in ('CREDITS.md', 'SOURCE-CREDITS.md'):
    (docs / name).write_bytes((args.kit / name).read_bytes())
print(f'Imported {len(receipt)} verified assets; receipt: {docs / "import-receipt.json"}')
