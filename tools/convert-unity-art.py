"""Convert selected inherited WebP files to Unity-readable PNG without changing pixels.
Run with Python and Pillow installed. Originals remain under assets/.
"""
from pathlib import Path
from PIL import Image

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
for name, source in sources.items():
    original = Image.open(root / source).convert("RGBA")
    destination = target / f"{name}.png"
    original.save(destination)
    assert Image.open(destination).convert("RGBA").tobytes() == original.tobytes()
print(f"Unity art: {len(sources)} pixel-identical PNG conversions passed")
