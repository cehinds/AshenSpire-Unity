"""Validate the shipped cooperative host against its exact source and ZIP receipt.

Run from any directory: python tools/validate-companion.py [repository root].
The host save, credentials and live session folders are never distributable files.
"""
import hashlib
import json
import re
from pathlib import Path, PurePosixPath
import sys
import zipfile


def digest(data):
    return hashlib.sha256(data).hexdigest()


def source_digest(path):
    return digest(path.read_bytes().decode('utf-8').replace('\r\n', '\n').encode('utf-8'))


def validate(root):
    root = Path(root).resolve()
    published = root / 'Published'
    receipt_bytes = (published / 'Companion.build.json').read_bytes()
    receipt = json.loads(receipt_bytes.decode('utf-8-sig'))
    version = json.loads((root / 'GameContent/Unity/version.json').read_text(encoding='utf-8-sig'))
    if (receipt['version'], receipt['buildNumber']) != (version['Version'], version['BuildNumber']):
        raise ValueError('Companion version differs from the Unity player')
    if receipt.get('sourceHashNormalization') != 'utf8-lf' or receipt.get('runtime') != 'win-x64' or receipt.get('selfContained') is not True:
        raise ValueError('Unsupported companion build receipt')
    groups = {
        'nativeSources': (root, set((root / 'Unity/Assets/AshenSpire/Runtime/Domain/Original').glob('*.cs'))),
        'contentSources': (root, set((root / 'GameContent/Unity/Original').glob('*.json'))),
        'transportSources': (root / 'tools/NativeLan', {p for folder in ['Transport', 'Companion', 'Domain', 'Packaging'] for p in (root / 'tools/NativeLan' / folder).rglob('*') if p.is_file() and not {'bin', 'obj'}.intersection(p.parts)}),
        'unitySources': (root, {root / p for p in ['Unity/Assets/AshenSpire/Runtime/Presentation/Networking/NativeLanClient.cs', 'Unity/Assets/AshenSpire/Plugins/WebGL/NativeLan.jslib', 'tools/NativeLan/Build-Companion.ps1']}),
    }
    checks = 0
    for key, (base, expected) in groups.items():
        rows = receipt[key]
        paths = [base / row['path'] for row in rows]
        if len(set(paths)) != len(paths) or set(paths) != expected:
            raise ValueError(f'Incomplete or unexpected companion source list: {key}')
        for row, path in zip(rows, paths):
            if source_digest(path) != row['sha256']:
                raise ValueError(f'Companion source changed: {path.relative_to(root)}')
            checks += 1
    with zipfile.ZipFile(published / 'Companion.zip') as archive:
        names = archive.namelist()
        if len(set(names)) != len(names):
            raise ValueError('Duplicate companion ZIP entry')
        files = [info for info in archive.infolist() if not info.is_dir()]
        for info in files:
            path = PurePosixPath(info.filename)
            if path.is_absolute() or '..' in path.parts or '\\' in info.filename or ':' in info.filename:
                raise ValueError('Unsafe companion ZIP path')
            if info.file_size > 200 * 1024 * 1024:
                raise ValueError('Oversized companion ZIP entry')
        rows = receipt['files']
        expected = {row['path']: row for row in rows}
        required = {'AshenSpire.Companion.exe', 'AshenSpire.Companion.dll', 'AshenSpire.Transport.dll', 'AshenSpire.NativeDomain.dll', 'AshenSpire.Companion.runtimeconfig.json', 'coreclr.dll', 'System.Private.CoreLib.dll', 'Start-Companion.cmd', 'Start-Companion.ps1', 'README.txt'}
        if not required.issubset(expected):
            raise ValueError('Companion ZIP is missing its portable runtime or launchers')
        if len(expected) != len(rows) or set(info.filename for info in files) != set(expected) | {'BuildStamp.json'}:
            raise ValueError('Companion ZIP contains unreceipted or missing files')
        if archive.read('BuildStamp.json') != receipt_bytes:
            raise ValueError('Companion ZIP and external build receipt disagree')
        forbidden = {'data', 'uisession', 'state', 'credentials', 'host-state.json'}
        for name, row in expected.items():
            if forbidden.intersection(part.lower() for part in PurePosixPath(name).parts) or re.match(r'^(credentials|host-state|resume-token|join-token|host-token|server-output)([.\-_]|$)', PurePosixPath(name).name, re.I) or name.lower().endswith('.log'):
                raise ValueError('Private companion state cannot be packaged')
            data = archive.read(name)
            if len(data) != row['bytes'] or digest(data) != row['sha256']:
                raise ValueError(f'Companion binary changed: {name}')
            checks += 1
        for row in receipt['contentSources']:
            name = 'Content/' + PurePosixPath(row['path']).name
            normalized = archive.read(name).decode('utf-8').replace('\r\n', '\n').encode('utf-8')
            if digest(normalized) != row['sha256']:
                raise ValueError(f'Companion content differs: {name}')
            checks += 1
    print(f'Companion package: {checks} source and binary checks passed')
    return checks


if __name__ == '__main__':
    validate(Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[1])
