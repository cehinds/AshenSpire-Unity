"""Round-trip campaign records through CSV with strict C# validation before import.
Examples: python tools/campaign-table.py export Cards work/Cards.csv
          python tools/campaign-table.py import Cards work/Cards.csv
JSON remains authoritative. Lists/effects are JSON inside CSV cells. Import retains
a timestamped backup in Builds/ContentBackups and replaces no data on validation failure.
"""
import argparse, csv, datetime, json, pathlib, shutil, subprocess, tempfile

root = pathlib.Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('action', choices=['export','import'])
parser.add_argument('table', choices=['Cards','Heroes','Foes','Encounters','Equipment','Tags'])
parser.add_argument('csv_path', type=pathlib.Path)
args = parser.parse_args()
source = root / 'GameContent/Unity/campaign.json'
content = json.loads(source.read_text(encoding='utf-8'))
rows = content[args.table]
template = rows[0]
columns = list(template)
if args.action == 'export':
    args.csv_path.parent.mkdir(parents=True, exist_ok=True)
    with args.csv_path.open('w', newline='', encoding='utf-8') as output:
        writer = csv.DictWriter(output, fieldnames=columns); writer.writeheader()
        writer.writerows({key:json.dumps(value,ensure_ascii=False) if isinstance(value,(list,dict)) else value for key,value in row.items()} for row in rows)
    print(f'Exported {len(rows)} {args.table} records to {args.csv_path}')
else:
    result=[]
    with args.csv_path.open(newline='',encoding='utf-8-sig') as source_csv:
        reader=csv.DictReader(source_csv)
        if reader.fieldnames != columns: raise ValueError(f'{args.csv_path}/header: expected columns {columns}')
        for number,row in enumerate(reader,2):
            record={}
            for key,sample in template.items():
                try:
                    value=row[key]
                    if value is None: raise ValueError('missing cell')
                    record[key]=json.loads(value) if isinstance(sample,(list,dict)) else int(value) if isinstance(sample,int) else value
                except (ValueError,TypeError) as error: raise ValueError(f'{args.csv_path}/row {number}/{key}: {error}') from error
            result.append(record)
    content[args.table]=result
    scratch=root/'Builds/ContentValidation'; scratch.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(dir=scratch) as temporary:
        candidate=pathlib.Path(temporary)/'campaign.json';candidate.write_text(json.dumps(content,indent=2)+'\n',encoding='utf-8',newline='\n')
        subprocess.run(['dotnet','run','--project',str(root/'UnityTests/Domain'),'--','--validate-campaign',str(candidate)],check=True,cwd=root)
        backup=root/'Builds/ContentBackups'/datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
        backup.mkdir(parents=True);shutil.copy2(source,backup/source.name)
        shutil.copyfile(candidate,source)
    print(f'Imported {len(result)} validated {args.table} records. Use AshenSpire > Validate and Import Content in Unity.')
