"""Bind desktop/mobile downloads to file hashes written by the Unity exporter."""
import hashlib
import json
from pathlib import Path, PurePosixPath
import sys
import zipfile


def validate(root):
    root = Path(root) / 'Published'
    windows_bytes = (root / 'Windows.build-source.json').read_bytes()
    windows = json.loads(windows_bytes.decode('utf-8-sig'))
    android = json.loads((root / 'Android.build-source.json').read_text(encoding='utf-8-sig'))
    if windows['target'] != 'Windows' or android['target'] != 'Android':
        raise ValueError('Incorrect platform receipt target')
    if 'AshenSpire.exe' not in windows['files'] or 'AshenSpire.apk' not in android['files']:
        raise ValueError('Missing exported platform executable')
    checks = 0
    with zipfile.ZipFile(root / 'Windows.zip') as archive:
        files = [info.filename for info in archive.infolist() if not info.is_dir()]
        if len(files) != len(set(files)) or set(files) != set(windows['files']) | {'build-source.json'}:
            raise ValueError('Windows ZIP is missing or adds unreceipted files')
        if archive.read('build-source.json') != windows_bytes:
            raise ValueError('Windows ZIP and external export receipt disagree')
        for name, expected in windows['files'].items():
            path = PurePosixPath(name)
            if path.is_absolute() or '..' in path.parts or '\\' in name or ':' in name:
                raise ValueError('Unsafe Windows ZIP path')
            if hashlib.sha256(archive.read(name)).hexdigest() != expected:
                raise ValueError(f'Windows export bytes changed: {name}')
            checks += 1
    if hashlib.sha256((root / 'Android.apk').read_bytes()).hexdigest() != android['files']['AshenSpire.apk']:
        raise ValueError('Android APK differs from its Unity export receipt')
    print(f'Native download receipts: {checks + 1} exported-file checks passed')


if __name__ == '__main__':
    validate(Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[1])
