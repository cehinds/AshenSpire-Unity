"""Package a receipted Web preview without promoting Published/ or any channel."""
from pathlib import Path
import hashlib
import json
import sys
import zipfile

web = Path(sys.argv[1]).resolve()
receipt = json.loads((web / 'build-source.json').read_text('utf-8-sig'))
if receipt['target'] != 'Web':
    raise ValueError('Expected a Web player receipt.')
files = sorted(p for p in web.rglob('*') if p.is_file())
payload = {p.relative_to(web).as_posix(): p for p in files if p.name != 'build-source.json'}
if set(payload) != set(receipt['files']):
    raise ValueError('Player payload differs from its receipt.')
for name, file in payload.items():
    with file.open('rb') as stream:
        if hashlib.file_digest(stream, 'sha256').hexdigest() != receipt['files'][name]:
            raise ValueError('Player payload hash mismatch: ' + name)
archive = web.parent / f'AshenedSpire-build{receipt["buildNumber"]}-Web.zip'
temporary = archive.with_suffix('.zip.tmp')
instructions = (
    f'AshenedSpire {receipt["version"]}, build {receipt["buildNumber"]}\n\n'
    'Extract this archive. Serve its Web folder over HTTP; opening index.html as a file is unsupported.\n'
    'With Python installed, run from the extracted folder:\n'
    '  python -m http.server 8080 --bind 127.0.0.1 --directory Web\n'
    'Then open http://127.0.0.1:8080/ in a browser.\n\n'
    'This is an intermediate migration preview, not milestone or owner acceptance.\n'
    'Browser saves belong to the browser profile and origin used to play.\n'
    f'Source digest: {receipt["sourceDigest"]}\n'
)
with zipfile.ZipFile(temporary, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=1) as bundle:
    for file in files:
        bundle.write(file, 'Web/' + file.relative_to(web).as_posix())
    bundle.writestr('README.txt', instructions)
with zipfile.ZipFile(temporary) as bundle:
    if bundle.testzip() is not None:
        raise ValueError('Archive CRC verification failed.')
    for name, expected in receipt['files'].items():
        if hashlib.sha256(bundle.read('Web/' + name)).hexdigest() != expected:
            raise ValueError('Archive payload hash mismatch: ' + name)
temporary.replace(archive)
with archive.open('rb') as stream:
    archive_hash = hashlib.file_digest(stream, 'sha256').hexdigest()
archive.with_suffix('.zip.sha256').write_text(archive_hash + '  ' + archive.name + '\n', 'utf-8')
print(json.dumps({'archive': str(archive), 'bytes': archive.stat().st_size,
                  'sha256': archive_hash, 'payloadFilesVerified': len(payload)}))
