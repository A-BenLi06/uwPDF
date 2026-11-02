"""Validate package versions across NuGet 3.x generated lock-file schemas."""
import json
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parent.parent
relative = 'LitePdfViewer/project.lock.json'
expected = json.loads(subprocess.check_output(['git', 'show', f'HEAD:{relative}'], cwd=root))
actual = json.loads((root / relative).read_text(encoding='utf-8-sig'))
assert expected['libraries'].keys() == actual['libraries'].keys(), 'NuGet changed the committed dependency versions.'
assert expected['targets'].keys() == actual['targets'].keys(), 'NuGet changed the committed target set.'
for target, libraries in expected['targets'].items():
    assert libraries.keys() == actual['targets'][target].keys(), f'NuGet changed packages for {target}.'
print(json.dumps(sorted(actual['libraries'])))
