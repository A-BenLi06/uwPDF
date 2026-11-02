"""Exercise the actual PowerShell release gate with valid and adversarial fixtures."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parent.parent
VERSION = '1.2.3.0'


def digest(data):
    return hashlib.sha256(data).hexdigest()


def fixture(directory, mutation=None):
    signed = {}
    (directory / 'uwPDF.cer').write_bytes(b'public certificate fixture')
    for arch in ('x86', 'x64', 'ARM', 'ARM64'):
        extension = '.msix' if arch == 'ARM64' else '.appx'
        name = f'uwPDF_{VERSION}_{arch}{extension}'
        package = b'package fixture ' + arch.encode()
        (directory / name).write_bytes(package)
        signed[name] = digest(package)
        with zipfile.ZipFile(directory / f'uwPDF_{VERSION}_{arch}_Install.zip', 'w') as archive:
            archive.writestr(name, b'tampered' if mutation == 'inner-package' else package)
            archive.writestr('uwPDF.cer', (directory / 'uwPDF.cer').read_bytes())
            archive.writestr('Add-AppDevPackage.ps1', b'# installer fixture')
            if mutation != 'missing-dependency':
                archive.writestr('Dependencies/framework.appx', b'framework fixture')
            if mutation == 'private-key':
                archive.writestr('Dependencies/private.pfx', b'forbidden fixture')
    (directory / 'build-info.json').write_text(json.dumps({
        'version': VERSION, 'signed_package_sha256': signed,
    }), encoding='utf-8')
    if mutation == 'store-upload':
        (directory / 'private.appxupload').write_bytes(b'forbidden fixture')
    lines = [f'{digest(path.read_bytes())}  {path.name}'
             for path in sorted(directory.iterdir())]
    if mutation == 'missing-checksum':
        lines.pop()
    if mutation == 'duplicate-checksum':
        lines[-1] = lines[0]
    (directory / 'SHA256SUMS.txt').write_text('\n'.join(lines) + '\n', encoding='utf-8')
    if mutation == 'outer-package':
        (directory / f'uwPDF_{VERSION}_x64.appx').write_bytes(b'tampered')


def main():
    for mutation in (None, 'inner-package', 'missing-dependency', 'private-key',
                     'store-upload', 'missing-checksum', 'duplicate-checksum', 'outer-package'):
        with tempfile.TemporaryDirectory(prefix='uwpdf-release-gate-') as temp:
            directory = Path(temp)
            fixture(directory, mutation)
            result = subprocess.run([
                'powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
                str(ROOT / 'scripts/Test-ReleaseAssets.ps1'), '-Directory', str(directory),
            ], capture_output=True, text=True,
                env={key: value for key, value in os.environ.items() if key.lower() != 'psmodulepath'})
            if (result.returncode == 0) != (mutation is None):
                raise AssertionError(f'{mutation}: unexpected gate result\n{result.stdout}\n{result.stderr}')
    print('PASS: valid four-architecture release accepted; tampering, missing dependencies, '
          'private keys, Store uploads and invalid checksum coverage rejected.')


if __name__ == '__main__':
    main()
