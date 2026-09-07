"""Mutation tests for exported desktop/mobile file receipts, not game execution.

All executable/APK payloads are deliberately labeled fixture bytes in a private
temporary directory. Passing these checks proves validator behavior only; actual
Unity compilation, launching, browser input and device testing remain separate.
"""
import contextlib
import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import warnings
import zipfile


spec = importlib.util.spec_from_file_location(
    'target_validator', Path(__file__).with_name('validate-unity-targets.py'))
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)


def digest(data):
    return hashlib.sha256(data).hexdigest()


windows_files = {
    'AshenSpire.exe': b'fixture Windows executable, not runnable',
    'UnityPlayer.dll': b'fixture Unity runtime, not runnable',
    'AshenSpire_Data/Managed/Assembly-CSharp.dll': b'fixture managed assembly',
}
apk = b'fixture Android APK, not installable'
windows_receipt = dict(target='Windows', sourceDigest='fixture-source',
                       version='0.0.10.0', buildNumber=10,
                       files={name: digest(data) for name, data in windows_files.items()})
android_receipt = dict(target='Android', sourceDigest='fixture-source',
                       version='0.0.10.0', buildNumber=10,
                       files={'AshenSpire.apk': digest(apk)})
checks = 0

with tempfile.TemporaryDirectory(prefix='ashenspire-target-validator-') as folder:
    def check(label, *, windows=None, android=None, files=None, android_bytes=apk,
              embedded=None, omit=(), duplicate=None, valid=False, error_contains=None):
        global checks
        root = Path(folder) / str(checks)
        published = root / 'Published'
        published.mkdir(parents=True)
        stamp = json.dumps(windows_receipt if windows is None else windows).encode('utf-8')
        if 'Windows.build-source.json' not in omit:
            (published / 'Windows.build-source.json').write_bytes(stamp)
        if 'Android.build-source.json' not in omit:
            (published / 'Android.build-source.json').write_text(
                json.dumps(android_receipt if android is None else android), encoding='utf-8')
        if 'Android.apk' not in omit:
            (published / 'Android.apk').write_bytes(android_bytes)
        if 'Windows.zip' not in omit:
            with zipfile.ZipFile(published / 'Windows.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
                for name, data in (windows_files if files is None else files).items():
                    # ZipInfo normalizes OS separators on Windows. Preserve the
                    # exact adversarial entry name so path guards are exercised.
                    info = zipfile.ZipInfo(name)
                    info.filename = name
                    archive.writestr(info, data)
                if 'embedded receipt' not in omit:
                    archive.writestr('build-source.json', stamp if embedded is None else embedded)
                if duplicate is not None:
                    with warnings.catch_warnings():
                        warnings.simplefilter('ignore', UserWarning)
                        archive.writestr(duplicate, b'duplicate fixture')
        try:
            with contextlib.redirect_stdout(io.StringIO()):
                validator.validate(root)
        except (ValueError, KeyError, FileNotFoundError, zipfile.BadZipFile) as error:
            if valid:
                raise AssertionError(f'Valid fixture refused: {label}') from error
            if error_contains is not None and error_contains not in str(error):
                raise AssertionError(f'{label}: wrong refusal: {error}') from error
        else:
            if not valid:
                raise AssertionError(f'Invalid fixture accepted: {label}')
        checks += 1

    check('matching exported bytes with exact actual file capitalization', valid=True)
    check('harmless ZIP directory entry', files={**windows_files, 'AshenSpire_Data/': b''}, valid=True)

    for name in ('Windows.zip', 'Windows.build-source.json', 'Android.apk',
                 'Android.build-source.json', 'embedded receipt'):
        check('missing ' + name, omit=(name,))

    for target, receipt in (('Windows', windows_receipt), ('Android', android_receipt)):
        changed = copy.deepcopy(receipt)
        changed['target'] = 'Web'
        check('wrong target ' + target, **{target.lower(): changed},
              error_contains='Incorrect platform receipt target')

    for name in windows_files:
        check('missing Windows export ' + name,
              files={key: data for key, data in windows_files.items() if key != name})
        check('altered Windows export ' + name,
              files={**windows_files, name: b'altered fixture'},
              error_contains='Windows export bytes changed')
    check('altered Android bytes', android_bytes=b'altered APK fixture',
          error_contains='Android APK differs')

    changed = copy.deepcopy(windows_receipt)
    changed['files']['AshenSpire.exe'] = digest(b'previous build fixture')
    check('stale Windows receipt hash', windows=changed,
          error_contains='Windows export bytes changed')
    changed = copy.deepcopy(android_receipt)
    changed['files']['AshenSpire.apk'] = digest(b'previous APK fixture')
    check('stale Android receipt hash', android=changed,
          error_contains='Android APK differs')
    check('embedded and external receipt disagree', embedded=b'{}',
          error_contains='external export receipt disagree')
    check('unexpected unreceipted file', files={**windows_files, 'extra.txt': b'extra fixture'},
          error_contains='unreceipted files')
    check('duplicate executable entry', duplicate='AshenSpire.exe',
          error_contains='unreceipted files')
    check('duplicate embedded receipt', duplicate='build-source.json',
          error_contains='unreceipted files')

    # These paths are fully receipted. Absolute/parent paths reach the path guard.
    # Python normalizes backslashes while reading on Windows, so that variant
    # may instead fail the exact membership comparison; both must fail closed.
    for unsafe in ('../outside.txt', '/absolute.txt', 'C:/outside.txt', 'dir\\outside.txt'):
        changed = copy.deepcopy(windows_receipt)
        changed['files'][unsafe] = digest(b'unsafe-path fixture')
        check('unsafe receipted path ' + unsafe, windows=changed,
              files={**windows_files, unsafe: b'unsafe-path fixture'},
              error_contains=None if '\\' in unsafe else 'Unsafe Windows ZIP path')

    # ZIP entry paths are case-sensitive even on Windows; do not accidentally
    # validate a differently named executable or managed assembly.
    check('wrong ZIP executable case', files={
        ('ashenspire.exe' if key == 'AshenSpire.exe' else key): data
        for key, data in windows_files.items()}, error_contains='unreceipted files')
    changed = copy.deepcopy(windows_receipt)
    changed['files']['ashenspire.exe'] = changed['files'].pop('AshenSpire.exe')
    check('wrong receipted executable case', windows=changed,
          error_contains='Missing exported platform executable')
    changed = copy.deepcopy(android_receipt)
    changed['files']['ashenspire.apk'] = changed['files'].pop('AshenSpire.apk')
    check('wrong receipted APK case', android=changed,
          error_contains='Missing exported platform executable')

print(f'Unity target package mutation checks: {checks} passed (fixture bytes, not runtime proof)')
