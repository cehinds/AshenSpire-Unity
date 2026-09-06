"""Extract transparent sprite components from the approved 4x3 enemy atlas.
Connected alpha regions belong to the closest cell by their center of mass. This
preserves weapons crossing a cell boundary. All sprites share one scale and foot anchor.
The source atlas and extraction boxes remain available for an owner to inspect.
"""
from pathlib import Path
import hashlib, json
from PIL import Image
root=Path(__file__).resolve().parent.parent
source=root/'GameContent/Unity/Art/EnemyAtlas.png'
atlas=Image.open(source).convert('RGBA');width,height=atlas.size
alpha=atlas.getchannel('A');mask=bytearray(1 if value>8 else 0 for value in alpha.getdata())
groups=[[] for _ in range(12)]
for start in range(width*height):
    if not mask[start]:continue
    stack=[start];mask[start]=0;region=[];sx=sy=0
    while stack:
        pixel=stack.pop();region.append(pixel);x=pixel%width;y=pixel//width;sx+=x;sy+=y
        for other in (pixel-1 if x else -1,pixel+1 if x+1<width else -1,pixel-width if y else -1,pixel+width if y+1<height else -1):
            if other>=0 and mask[other]:mask[other]=0;stack.append(other)
    if len(region)<8:continue
    column=min(3,int((sx/len(region))*4/width));row=min(2,int((sy/len(region))*3/height));groups[row*4+column].extend(region)
ids=['wanderingSoldier','blightHound','livingArmor','fellWarden','courtDuelist','courtSurgeon','gildedKnight','stitchedKing','graveWisp','wyrmAspirant','valkyrieShade','wyrmLord']
sprites=[];boxes=[]
for pixels in groups:
    if len(pixels)<1000:raise ValueError('Missing sprite group; inspect atlas.')
    selected=Image.new('L',atlas.size);selection=selected.load()
    for pixel in pixels:selection[pixel%width,pixel//width]=alpha.getpixel((pixel%width,pixel//width))
    bbox=selected.getbbox();sprite=atlas.copy();sprite.putalpha(selected);sprites.append(sprite.crop(bbox));boxes.append(bbox)
scale=min(340/max(sprite.width for sprite in sprites),340/max(sprite.height for sprite in sprites))
destination=root/'Unity/Assets/AshenSpire/Resources/Art';destination.mkdir(parents=True,exist_ok=True)
for id,sprite in zip(ids,sprites):
    sprite=sprite.resize((round(sprite.width*scale),round(sprite.height*scale)),Image.Resampling.LANCZOS)
    canvas=Image.new('RGBA',(384,384));canvas.alpha_composite(sprite,((384-sprite.width)//2,364-sprite.height));canvas.save(destination/f'painted_{id}.png')
manifest=dict(SourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(),Canvas=[384,384],FootAnchor=[192,364],SharedScale=scale,SourceBoxes=dict(zip(ids,boxes)))
(source.parent/'EnemyAtlas.json').write_text(json.dumps(manifest,indent=2)+'\n')
print('Enemy atlas: 12 sprites extracted with one scale and foot anchor.')
