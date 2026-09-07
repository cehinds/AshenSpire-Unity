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
print(f'Original CSV authoring: {checks} checks passed')
