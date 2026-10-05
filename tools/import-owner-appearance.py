"""Copy the owner's 4175 appearance reference into isolated native resources.
Atlas extraction is lossless and every output's decoded pixels are checked.
The reference checkout and existing resources remain untouched.
"""
from pathlib import Path
from PIL import Image
from fontTools.ttLib import TTFont
import hashlib, json, re
root=Path(__file__).resolve().parent.parent
source=Path('D:/repos/TheAshenedSpire')
assets=source/'public/assets'
target=root/'Unity/Assets/AshenSpire/Resources/Art/OwnerAppearance'
target.mkdir(parents=True,exist_ok=True)
template=(root/'Unity/Assets/AshenSpire/Resources/Art/background1.png.meta').read_text()
rows=[]
def export(name, original, box=None):
    image=Image.open(original).convert('RGBA')
    if box: image=image.crop(box)
    output=target/(name+'.png');image.save(output,optimize=True)
    assert Image.open(output).convert('RGBA').tobytes()==image.tobytes()
    meta=output.with_suffix('.png.meta')
    if not meta.exists():
        guid=hashlib.sha256(('owner-appearance:'+name).encode()).hexdigest()[:32]
        meta.write_text(re.sub(r'guid: [a-f0-9]+','guid: '+guid,template).replace('enableMipMap: 1','enableMipMap: 0'))
    # NPOT nearest-power resizing warps atlas cutouts (467x706 becomes 512x512).
    # Keep the reference dimensions and pixels in the imported player texture.
    settings=meta.read_text()
    settings=re.sub(r'nPOTScale: \d+', 'nPOTScale: 0', settings)
    settings=re.sub(r'maxTextureSize: \d+', 'maxTextureSize: 8192', settings)
    settings=re.sub(r'textureCompression: \d+', 'textureCompression: 0', settings)
    meta.write_text(settings)
    rows.append({'resource':'Art/OwnerAppearance/'+name,'source':str(original),'sourceSha256':hashlib.sha256(original.read_bytes()).hexdigest(),'crop':box,'outputSha256':hashlib.sha256(output.read_bytes()).hexdigest(),'pixelsVerified':True})
atlas=json.loads((source/'src/visual-atlas.json').read_text())
for key,frame in atlas['frames'].items():
    x,y,w,h=[frame[n] for n in ['x','y','w','h']]
    export(key,assets/('polish-v2/'+frame['sheet']+'.png'),[x,y,x+w,y+h])
export('courtyard',assets/'polish-v2/courtyard.png')
for kind in ['attack','guard','ember']:export('card-'+kind,assets/('illustrations/card-'+kind+'-illustration.png'))
fontTarget=root/'Unity/Assets/AshenSpire/Resources/Fonts'
font=TTFont(assets/'fonts/cormorant-garamond-500-normal.woff2');font.flavor=None
font.save(fontTarget/'OwnerCormorant.ttf')
(fontTarget/'OwnerFonts-OFL.txt').write_bytes((assets/'fonts/OFL.txt').read_bytes())
fontMeta=fontTarget/'OwnerCormorant.ttf.meta'
if not fontMeta.exists() or 'TrueTypeFontImporter:' not in fontMeta.read_text():
    guid=hashlib.sha256(str(fontMeta.relative_to(root)).encode()).hexdigest()[:32]
    nativeFontMeta=(fontTarget/'Cinzel-Regular.ttf.meta').read_text()
    fontMeta.write_text(re.sub(r'guid: [a-f0-9]+','guid: '+guid,nativeFontMeta).replace('- Cinzel','- Cormorant Garamond'))
for output in [target.with_suffix('.meta'),fontTarget/'OwnerFonts-OFL.txt.meta']:
    if not output.exists():output.write_text('fileFormatVersion: 2\nguid: '+hashlib.sha256(str(output.relative_to(root)).encode()).hexdigest()[:32]+'\n')
receipt=root/'TestResults/OwnerAppearance';receipt.mkdir(parents=True,exist_ok=True)
(receipt/'import.json').write_text(json.dumps({'reference':'http://127.0.0.1:4175/','assets':rows},indent=2)+'\n')
print(json.dumps({'imported':len(rows),'decodedPixelsVerified':True,'font':'Cormorant Garamond 500'}))
