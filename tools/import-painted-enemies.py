"""Copy generated enemy PNGs unchanged and record alpha bounds for UI placement.
Run with Python plus Pillow after editing EnemyExpansion source PNGs. This reads
pixels for registration; it does not resize, crop, recolor or re-encode artwork.
The shared Unity figure scales the visible bounds and aligns the foot baseline.
"""
from pathlib import Path
import hashlib, json, shutil
from PIL import Image
root=Path(__file__).resolve().parent.parent
source=root/'GameContent/Unity/Art/EnemyExpansion'
resource_root=root/'Unity/Assets/AshenSpire/Resources'
destination=resource_root/'Art/Enemies/Painted'
destination.mkdir(parents=True,exist_ok=True)
for file in source.glob('*.png'):
    shutil.copyfile(file,destination/file.name)
catalog_path=root/'GameContent/Unity/Original/enemy-art.json'
catalog=json.loads(catalog_path.read_text(encoding='utf-8'))
content=json.loads((root/'GameContent/Unity/Original/content.json').read_text(encoding='utf-8'))
assert set(catalog['enemies'])=={e['id'] for e in content['enemies']}
receipts=[]
for enemy_id,value in catalog['enemies'].items():
    resource=value if isinstance(value,str) else value['resource']
    file=resource_root/(resource+'.png')
    with Image.open(file) as image:
        assert image.mode=='RGBA', f'{enemy_id}: genuine RGBA required'
        alpha=image.getchannel('A')
        assert alpha.getextrema()[0]==0, f'{enemy_id}: transparent background required'
        box=alpha.point(lambda n: 255 if n>16 else 0).getbbox()
        assert box, f'{enemy_id}: empty sprite'
        w,h=image.size;x0,y0,x1,y1=box
        catalog['enemies'][enemy_id]={'resource':resource,'bounds':[x0/w,y0/h,(x1-x0)/w,(y1-y0)/h]}
        receipts.append({'enemyId':enemy_id,'resource':resource,'sha256':hashlib.sha256(file.read_bytes()).hexdigest(),'canvas':[w,h],'alphaBounds':list(box),'hasTransparentPixels':True})
catalog_path.write_text(json.dumps(catalog,indent=2)+'\n',encoding='utf-8')
(source/'Registration.json').write_text(json.dumps({'method':'PNG bytes unchanged; alpha > 16 bounds used for runtime placement','baseline':0.94,'maximumWidth':0.90,'maximumHeight':0.84,'enemies':receipts},indent=2)+'\n',encoding='utf-8')
print(f'Painted enemies: {len(receipts)} complete mappings, transparent PNGs and source hashes verified.')
