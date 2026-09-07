"""Exercise real CSV file transactions on isolated copies, not the game source."""
import csv
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('original_table', ROOT / 'tools/original-table.py')
tool = importlib.util.module_from_spec(spec)
spec.loader.exec_module(tool)
checks = 0


def check(value):
    global checks
    assert value
    checks += 1


with tempfile.TemporaryDirectory(prefix='AshenSpire-Original-') as folder:
    root = Path(folder)
    source = root / 'content.json'
    original = (ROOT / 'GameContent/Unity/Original/content.json').read_bytes()
    source.write_bytes(original)
    sheet = root / 'Cards.csv'
    tool.execute('export', 'cards', sheet, source)
    check(len(list(csv.DictReader(sheet.open(encoding='utf-8', newline='')))) == 182)
    tool.execute('import', 'cards', sheet, source)
    check(json.loads(source.read_text(encoding='utf-8')) == json.loads(original))
    check(any(path.read_bytes() == original for path in (root / 'Backups').glob('*.json')))
    before = source.read_bytes()
    try:
        tool.execute('import', 'cards', sheet, source)
        raise AssertionError('Stale import accepted')
    except ValueError:
        check(source.read_bytes() == before)
    bad = root / 'Duplicate.csv'
    tool.execute('export', 'cards', bad, source)
    with bad.open(encoding='utf-8', newline='') as stream:
        rows = list(csv.reader(stream))
    rows.append(rows[1])
    with bad.open('w', encoding='utf-8', newline='') as stream:
        csv.writer(stream).writerows(rows)
    try:
        tool.execute('import', 'cards', bad, source)
        raise AssertionError('Duplicate record import accepted')
    except subprocess.CalledProcessError:
        check(source.read_bytes() == before)
    check(not list(root.glob('original-candidate-*')))
    check(not source.with_suffix('.import.lock').exists())
    try:
        tool.execute('export', 'cards', bad, source)
        raise AssertionError('Existing CSV overwritten')
    except ValueError:
        check(source.read_bytes() == before)
    # Add interdependent rows through five actual CSV exports/imports. Never edit
    # the game source. The final C# check equips and plays these authored additions.
    additions = [
        ('cards', 'strike', {'id': 'authoringStrike', 'name': 'Authoring Strike'}),
        ('enemies', 'wanderingSoldier', {'id': 'authoringSoldier', 'name': 'Authoring Soldier'}),
        ('encounters', 'loneSoldier', {'id': 'authoringEncounter', 'enemies': ['authoringSoldier']}),
        ('equipment.armaments', 'straightSword', {'id': 'authoringSword', 'name': 'Authoring Sword', 'attackRating': 9}),
        ('equipment.startingKits', 'reaverBaseline', {'id': 'authoringKit', 'rightHand': 'authoringSword'})]
    for table, template, changed in additions:
        sheet = root / ('Demo-' + table + '.csv')
        tool.execute('export', table, sheet, source)
        with sheet.open(encoding='utf-8', newline='') as stream:
            reader = csv.DictReader(stream); headers = reader.fieldnames; rows = list(reader)
        row = next(dict(row) for row in rows if json.loads(row['id']) == template)
        row.update({key: json.dumps(value) for key, value in changed.items()}); rows.append(row)
        with sheet.open('w', encoding='utf-8', newline='') as stream:
            writer = csv.DictWriter(stream, fieldnames=headers); writer.writeheader(); writer.writerows(rows)
        tool.execute('import', table, sheet, source)
        check(True)
    sheet = root / 'Classes.csv'
    tool.execute('export', 'classes', sheet, source)
    with sheet.open(encoding='utf-8', newline='') as stream:
        reader = csv.DictReader(stream); headers = reader.fieldnames; rows = list(reader)
    reaver = next(row for row in rows if json.loads(row['id']) == 'reaver')
    reaver['eligibleStartingKitIds'] = json.dumps(json.loads(reaver['eligibleStartingKitIds']) + ['authoringKit'])
    with sheet.open('w', encoding='utf-8', newline='') as stream:
        writer = csv.DictWriter(stream, fieldnames=headers); writer.writeheader(); writer.writerows(rows)
    tool.execute('import', 'classes', sheet, source)
    subprocess.run(['dotnet', 'run', '--project', str(ROOT / 'UnityTests/OriginalAuthoring'), '--', str(source)], cwd=ROOT, check=True)
    check(True)

print(f'Original CSV authoring: {checks} checks passed')
