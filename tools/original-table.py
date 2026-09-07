"""Lossless original-content CSV export/import. Every non-empty cell is JSON;
blank means absent. A hash receipt prevents stale imports. Candidates validate
in C# before an atomic replacement; exact previous bytes remain in Backups/.
Usage: python tools/original-table.py export cards Builds/Cards.csv
       python tools/original-table.py import cards Builds/Cards.csv
"""
import argparse
import csv
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import uuid

ROOT = Path(__file__).resolve().parent.parent


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f'Duplicate JSON property: {key}')
        result[key] = value
    return result


def decode(value):
    return json.loads(value, object_pairs_hook=unique_object)


def execute(mode, table, csv_path, source):
    source, csv_path = source.resolve(), csv_path.resolve()
    original = source.read_bytes()
    content = decode(original.decode('utf-8-sig'))
    parent = content
    parts = table.split('.')
    for part in parts[:-1]:
        parent = parent[part]
    rows = parent[parts[-1]]
    if not isinstance(rows, list) or not all(isinstance(row, dict) for row in rows):
        raise ValueError(f'{table} is not a record table')
    receipt_path = csv_path.with_suffix(csv_path.suffix + '.receipt.json')
    receipt = {'schemaVersion': 1, 'table': table, 'sourceSha256': hashlib.sha256(original).hexdigest()}
    if mode == 'export':
        if csv_path.exists() or receipt_path.exists():
            raise ValueError('Export path exists; choose a new filename to preserve prior edits.')
        fields = list(dict.fromkeys(key for row in rows for key in row))
        csv_path.parent.mkdir(parents=True, exist_ok=True)
        with csv_path.open('x', encoding='utf-8', newline='') as stream:
            writer = csv.DictWriter(stream, fieldnames=fields)
            writer.writeheader()
            for row in rows:
                writer.writerow({key: json.dumps(value, ensure_ascii=False, allow_nan=False, separators=(',', ':')) for key, value in row.items()})
        receipt_path.write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
        return len(rows)
    if decode(receipt_path.read_text(encoding='utf-8')) != receipt:
        raise ValueError('CSV source changed since export. Export a fresh table and reapply the edits.')
    with csv_path.open(encoding='utf-8-sig', newline='') as stream:
        reader = csv.DictReader(stream)
        if not reader.fieldnames or len(reader.fieldnames) != len(set(reader.fieldnames)):
            raise ValueError('CSV has missing or duplicate column headers.')
        updated = []
        for line, row in enumerate(reader, 2):
            if None in row or any(value is None for value in row.values()):
                raise ValueError(f'CSV row {line} has the wrong number of cells.')
            updated.append({key: decode(value) for key, value in row.items() if value != ''})
    parent[parts[-1]] = updated
    candidate_bytes = (json.dumps(content, indent=2, ensure_ascii=False, allow_nan=False) + '\n').encode('utf-8')
    handle, candidate_path = tempfile.mkstemp(prefix='original-candidate-', suffix='.json', dir=source.parent)
    candidate = Path(candidate_path)
    lock_path = source.with_suffix('.import.lock')
    lock = None
    try:
        with os.fdopen(handle, 'wb') as stream:
            stream.write(candidate_bytes)
        subprocess.run(['dotnet', 'run', '--project', str(ROOT / 'UnityTests/Parity'), '--', '--validate', str(candidate)], cwd=ROOT, check=True)
        lock = os.open(lock_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
        if source.read_bytes() != original:
            raise ValueError('Source changed during validation; original retained.')
        backups = ROOT / 'Builds/ContentBackups/Original' if source.is_relative_to(ROOT / 'GameContent/Unity') else source.parent / 'Backups'
        backups.mkdir(parents=True, exist_ok=True)
        with (backups / (source.stem + '-' + uuid.uuid4().hex + '.json')).open('xb') as stream:
            stream.write(original)
        os.replace(candidate, source)
    finally:
        if lock is not None:
            os.close(lock)
            lock_path.unlink()
        if candidate.exists():
            candidate.unlink()
    return len(updated)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['export', 'import'])
    parser.add_argument('table')
    parser.add_argument('csv', type=Path)
    parser.add_argument('--source', type=Path, default=ROOT / 'GameContent/Unity/Original/content.json')
    options = parser.parse_args()
    count = execute(options.mode, options.table, options.csv, options.source)
    print(f'Original table {options.mode}: {count} records processed')
