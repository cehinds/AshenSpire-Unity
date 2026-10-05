"""Import verified test-898 assets into a separate Unity namespace.
Raster conversion preserves source RGBA pixels; authored trims crop exactly.
No original asset or existing native art is changed. SVG inputs require resvg.
"""
from pathlib import Path
from PIL import Image
import hashlib, json, sys, subprocess

root = Path(__file__).resolve().parent.parent
reference = Path(sys.argv[1]).resolve()
manifest = json.loads((root / 'TestResults/HtmlParity/test898/illustrated-reference.json').read_text('utf-8'))
assert manifest['referenceBuild'] == 898
target = root / 'Unity/Assets/AshenSpire/Resources/Art/html898'
target.mkdir(parents=True, exist_ok=True)
rows = []
meta_template = (root / 'Unity/Assets/AshenSpire/Resources/Art/background1.png.meta').read_text('utf-8')
for key, asset in manifest['assets'].items():
    original = reference / '.art-cache/hd-assets-v9/high' / asset['href']
    if not original.exists():
        original = reference / '.art-cache/hd-assets-v9/common' / asset['href']
    if not original.exists():
        raise FileNotFoundError(original)
    rasterized = root / 'TestResults/HtmlParity/test898/svg-rasterized' / (key + '.png')
    if original.suffix == '.svg' and not rasterized.exists():
        raise RuntimeError('Run render-published-svg.cjs first for ' + str(original))
    image = Image.open(rasterized if original.suffix == '.svg' else original).convert('RGBA')
    if asset['trim']:
        x, y, w, h = asset['trim']
        image = image.crop((x, y, x+w, y+h))
    output = target / (key + '.png')
    image.save(output, optimize=True)
    decoded = Image.open(output).convert('RGBA')
    assert image.size == decoded.size and image.tobytes() == decoded.tobytes(), output
    meta = output.with_suffix('.png.meta')
    if not meta.exists():
        guid = hashlib.sha256(('published898:' + asset['resource']).encode()).hexdigest()[:32]
        old_guid = next(line.split(': ')[1] for line in meta_template.splitlines() if line.startswith('guid:'))
        meta.write_text(meta_template.replace(old_guid, guid).replace('enableMipMap: 1','enableMipMap: 0'), 'utf-8')
    rows.append({'source':asset['href'],'sourceSha256':hashlib.sha256(original.read_bytes()).hexdigest(),
                 'resource':asset['resource'],'trim':asset['trim'],'size':list(image.size),
                 'outputSha256':hashlib.sha256(output.read_bytes()).hexdigest(),'pixelsVerified':True,
                 'conversion':'svg-rasterized' if original.suffix == '.svg' else 'rgba-preserved'})
resource = root / 'Unity/Assets/AshenSpire/Resources/Original/illustrated-reference.json'
resource.write_text(json.dumps(manifest, indent=2)+'\n','utf-8')
receipt = {'referenceBuild':898,'assets':rows,'sourceLayoutSha256':manifest['sourceSha256'],'allPixelsVerified':True}
(root / 'TestResults/HtmlParity/test898/illustrated-art-import.json').write_text(json.dumps(receipt,indent=2)+'\n','utf-8')
print(json.dumps({'referenceBuild':898,'imported':len(rows),'allPixelsVerified':True}))
