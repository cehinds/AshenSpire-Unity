"""Import verified reference assets into a separate Unity namespace.
  python3 tools/import-published-ui-art.py <published-898-tree>
  python3 tools/import-published-ui-art.py <ashenspire-checkout> --build 1815
Raster conversion preserves source RGBA pixels; authored trims crop exactly.
No original asset or existing native art is changed. 898 SVG inputs require
render-published-svg.cjs first; 1815 SVG/HTML inputs are already rendered by
tools/unity-illustrated-reference.mjs --build 1815 (Playwright Chromium).
"""
from pathlib import Path
from PIL import Image, ImageDraw
import hashlib, json, sys

root = Path(__file__).resolve().parent.parent
args = sys.argv[1:]
build = int(args[args.index('--build') + 1]) if '--build' in args else 898
reference = Path(next(a for i, a in enumerate(args) if not a.startswith('--') and (i == 0 or args[i - 1] != '--build'))).resolve()
meta_template = (root / 'Unity/Assets/AshenSpire/Resources/Art/background1.png.meta').read_text('utf-8')
old_guid = next(line.split(': ')[1] for line in meta_template.splitlines() if line.startswith('guid:'))


def import898():
    manifest = json.loads((root / 'TestResults/HtmlParity/test898/illustrated-reference.json').read_text('utf-8'))
    assert manifest['referenceBuild'] == 898
    target = root / 'Unity/Assets/AshenSpire/Resources/Art/html898'
    target.mkdir(parents=True, exist_ok=True)
    rows = []
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


def guid(salt):
    return hashlib.sha256(salt.encode()).hexdigest()[:32]


def ui_texture_meta(resource):
    # Uncompressed UI art: no mipmaps, NPOT kept, alpha treated as transparency.
    text = meta_template.replace(old_guid, guid('js1815:' + resource))
    text = text.replace('enableMipMap: 1', 'enableMipMap: 0').replace('nPOTScale: 1', 'nPOTScale: 0')
    text = text.replace('alphaIsTransparency: 0', 'alphaIsTransparency: 1').replace('textureCompression: 1', 'textureCompression: 0')
    return text


def text_meta(salt):
    return ('fileFormatVersion: 2\nguid: ' + guid(salt) + '\nTextScriptImporter:\n  externalObjects: {}\n'
            '  userData:\n  assetBundleName:\n  assetBundleVariant:\n')


def folder_meta(salt):
    return ('fileFormatVersion: 2\nguid: ' + guid(salt) + '\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n'
            '  userData:\n  assetBundleName:\n  assetBundleVariant:\n')


def import1815():
    work = root / 'TestResults/HtmlParity/js1815'
    manifest = json.loads((work / 'illustrated-reference.json').read_text('utf-8'))
    assert manifest['referenceBuild'] == 1815
    resources = root / 'Unity/Assets/AshenSpire/Resources'
    target = resources / 'Art/js1815/cards'
    target.mkdir(parents=True, exist_ok=True)
    for folder in (resources / 'Art/js1815', target):
        meta = folder.with_name(folder.name + '.meta')
        if not meta.exists():
            meta.write_text(folder_meta('js1815-folder:' + folder.relative_to(resources).as_posix()), 'utf-8')
    # Only assets the reference actually names are kept; stale renders are removed.
    wanted = {asset['resource'].split('/')[-1] + '.png' for asset in manifest['assets'].values()}
    for stale in target.glob('*.png'):
        if stale.name not in wanted:
            stale.unlink(); stale.with_suffix('.png.meta').unlink(missing_ok=True)
    rows = []
    for key, asset in sorted(manifest['assets'].items()):
        if 'rendered' in asset:
            original = work / asset['rendered']
            image = Image.open(original).convert('RGBA')
            conversion = 'chromium-rendered'
        else:
            original = reference / asset['file']
            image = Image.open(original).convert('RGBA')
            conversion = 'rgba-preserved'
            if asset['trim']:
                x, y, w, h = asset['trim']
                image = image.crop((x, y, x+w, y+h))
            if asset.get('cut'):
                # The JS clip-path keeps only the frame outside this polygon (the trim plane).
                mask = image.getchannel('A')
                draw = ImageDraw.Draw(mask)
                draw.polygon([(px * image.width, py * image.height) for px, py in asset['cut']], fill=0)
                image.putalpha(mask)
                conversion = 'interior-cut'
            limit = asset.get('maxSide') or 1024
            if max(image.size) > limit:
                scale = limit / max(image.size)
                image = image.resize((round(image.width * scale), round(image.height * scale)), Image.LANCZOS)
                conversion += '+downscaled'
        output = target / (asset['resource'].split('/')[-1] + '.png')
        image.save(output, optimize=True)
        decoded = Image.open(output).convert('RGBA')
        assert image.size == decoded.size and image.tobytes() == decoded.tobytes(), output
        output.with_suffix('.png.meta').write_text(ui_texture_meta(asset['resource']), 'utf-8')
        rows.append({'key':key,'source':asset.get('href') or asset.get('provenance'),'sourcePath':str(Path(asset.get('file') or asset['rendered'])),
                     'sourceSha256':hashlib.sha256(original.read_bytes()).hexdigest(),
                     'renderedHtmlSha256':asset.get('htmlSha256'),'resource':asset['resource'],'trim':asset.get('trim'),
                     'size':list(image.size),'bytes':output.stat().st_size,
                     'outputSha256':hashlib.sha256(output.read_bytes()).hexdigest(),'conversion':conversion})
    resource = resources / 'Original/illustrated-reference-1815.json'
    resource.write_text(json.dumps(manifest, indent=1)+'\n','utf-8')
    meta = resource.with_name(resource.name + '.meta')
    if not meta.exists():
        meta.write_text(text_meta('js1815:Original/illustrated-reference-1815'), 'utf-8')
    provenance = {'referenceBuild':1815,'sourceRepository':'cehinds/AshenSpire','sourceCommit':manifest['sourceCommit'],
                  'sourceDigest':manifest['sourceDigest'],'artCache':'.art-cache/hd-assets-v23',
                  'sourceLayoutSha256':manifest['sourceSha256'],'assets':rows}
    provenance_file = target / 'provenance.json'
    provenance_file.write_text(json.dumps(provenance, indent=1)+'\n', 'utf-8')
    meta = provenance_file.with_name('provenance.json.meta')
    if not meta.exists():
        meta.write_text(text_meta('js1815:Art/js1815/cards/provenance'), 'utf-8')
    (work / 'illustrated-art-import.json').write_text(json.dumps(provenance, indent=1)+'\n', 'utf-8')
    print(json.dumps({'referenceBuild':1815,'imported':len(rows),'bytes':sum(r['bytes'] for r in rows)}))


if build == 898:
    import898()
elif build == 1815:
    import1815()
else:
    raise SystemExit('Unknown reference build %d' % build)
