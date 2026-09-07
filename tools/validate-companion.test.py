"""Mutation checks for missing/stale/private companion package artifacts."""
import contextlib
import copy
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import zipfile

spec = importlib.util.spec_from_file_location('validator', Path(__file__).with_name('validate-companion.py'))
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)
checks = 0
with tempfile.TemporaryDirectory(prefix='ashenspire-companion-check-') as folder:
    root = Path(folder)
    published = root / 'Published'
    published.mkdir()
    groups = {
        'nativeSources': ['Unity/Assets/AshenSpire/Runtime/Domain/Original/Game.cs'],
        'contentSources': ['GameContent/Unity/Original/content.json'],
        'transportSources': ['tools/NativeLan/Transport/Host.cs', 'tools/NativeLan/Companion/Program.cs', 'tools/NativeLan/Domain/Domain.csproj', 'tools/NativeLan/Packaging/Start-Companion.cmd'],
        'unitySources': ['Unity/Assets/AshenSpire/Runtime/Presentation/Networking/NativeLanClient.cs', 'Unity/Assets/AshenSpire/Plugins/WebGL/NativeLan.jslib', 'tools/NativeLan/Build-Companion.ps1'],
    }
    receipt = dict(version='0.0.10.0', buildNumber=10, sourceHashNormalization='utf8-lf', runtime='win-x64', selfContained=True)
    for key, paths in groups.items():
        receipt[key] = []
        for name in paths:
            path = root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b'{}\r\n')
            receipt[key].append(dict(path=name.removeprefix('tools/NativeLan/') if key == 'transportSources' else name, sha256=validator.source_digest(path)))
    (root / 'GameContent/Unity/version.json').write_text(json.dumps(dict(Version='0.0.10.0', BuildNumber=10)))
    binaries = {name: b'fixture runtime' for name in ['AshenSpire.Companion.exe', 'AshenSpire.Companion.dll', 'AshenSpire.Transport.dll', 'AshenSpire.NativeDomain.dll', 'AshenSpire.Companion.runtimeconfig.json', 'coreclr.dll', 'System.Private.CoreLib.dll', 'Start-Companion.cmd', 'Start-Companion.ps1', 'README.txt']}
    binaries['Content/content.json'] = b'{}\r\n'
    receipt['files'] = [dict(path=name, bytes=len(data), sha256=validator.digest(data)) for name, data in binaries.items()]

    def check(changed=None, payload=None, valid=False, embedded=None):
        global checks
        stamp = json.dumps(changed or receipt).encode()
        (published / 'Companion.build.json').write_bytes(stamp)
        with zipfile.ZipFile(published / 'Companion.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
            for name, data in (binaries if payload is None else payload).items():
                archive.writestr(name, data)
            archive.writestr('BuildStamp.json', stamp if embedded is None else embedded)
        try:
            with contextlib.redirect_stdout(io.StringIO()):
                validator.validate(root)
        except (ValueError, KeyError, FileNotFoundError):
            if valid:
                raise
        else:
            if not valid:
                raise AssertionError('Bad companion package was accepted')
        checks += 1

    check(valid=True)
    # A Linux checkout must validate the same Windows-built sources.
    for paths in groups.values():
        for name in paths:
            (root / name).write_bytes(b'{}\n')
    check(valid=True)
    for key, value in [('version', '0.0.11.0'), ('buildNumber', 11), ('selfContained', False), ('runtime', 'linux-x64')]:
        changed = copy.deepcopy(receipt)
        changed[key] = value
        check(changed)
    for group in groups:
        changed = copy.deepcopy(receipt)
        changed[group] = changed[group][:-1]
        check(changed)
    changed = copy.deepcopy(receipt)
    changed['nativeSources'][0]['sha256'] = '0' * 64
    check(changed)
    check(payload={**binaries, 'State/host-state.json': b'private'})
    check(payload={**binaries, '../outside.txt': b'escape'})
    check(payload={**binaries, 'AshenSpire.Companion.exe': b'changed'})
    check(payload={key: value for key, value in binaries.items() if key != 'coreclr.dll'})
    check(embedded=b'{}')
    for name in ['credentials.json', 'host-state.json.backup', 'HOST-STATE.json', 'server.log']:
        changed = copy.deepcopy(receipt)
        data = b'private'
        changed['files'].append(dict(path=name, bytes=len(data), sha256=validator.digest(data)))
        check(changed, {**binaries, name: data})
    changed = copy.deepcopy(receipt)
    changed['files'] = [row for row in changed['files'] if row['path'] != 'coreclr.dll']
    check(changed, {key: value for key, value in binaries.items() if key != 'coreclr.dll'})
    (root / groups['nativeSources'][0]).write_text('changed\n')
    check()
print(f'Companion package mutation checks: {checks} passed')
