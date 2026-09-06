"""Convert selected inherited WebP files to Unity-readable PNG without changing pixels.
Run with Python and Pillow installed. Originals remain under assets/.
"""
from pathlib import Path
from PIL import Image
import json

root = Path(__file__).resolve().parent.parent
target = root / "Unity/Assets/AshenSpire/Resources/Art"
target.mkdir(parents=True, exist_ok=True)
sources = {
    "reaver_idle": "assets/poses/reaver_idle_ember.webp",
    "reaver_attack": "assets/poses/reaver_attack1_ember.webp",
    "enemy_wanderingSoldier": "assets/sprites/enemy_wanderingSoldier.webp",
    "enemy_blightHound": "assets/sprites/enemy_blightHound.webp",
    "background": "assets/bg/bg_act1.webp",
}
campaign = json.loads((root / 'GameContent/Unity/campaign.json').read_text(encoding='utf-8'))
for hero in campaign['Heroes']:
    for pose in ['idle', 'attack1', 'attack2', 'guard', 'hit']:
        sources[f"{hero['Art']}_{pose}"] = f"assets/poses/{hero['Art']}_{pose}_ember.webp"
for foe in campaign['Foes']:
    if not foe['Art'].startswith('painted_'):
        sources[foe['Art']] = f"assets/sprites/{foe['Art']}.webp"
for act in [1, 2, 3]:
    sources[f'background{act}'] = f'assets/bg/bg_act{act}.webp'
for name, source in sources.items():
    original = Image.open(root / source).convert("RGBA")
    destination = target / f"{name}.png"
    original.save(destination)
    assert Image.open(destination).convert("RGBA").tobytes() == original.tobytes()
print(f"Unity art: {len(sources)} pixel-identical PNG conversions passed")
