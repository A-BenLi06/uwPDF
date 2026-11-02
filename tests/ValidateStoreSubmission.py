"""Check source submission fields that caused the October 26 rejection."""
from pathlib import Path
import json
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
listings = json.loads((ROOT / 'docs/store/listings.json').read_text(encoding='utf-8'))
assert set(listings) == {'en-us', 'ja-jp', 'zh-cn', 'zh-tw'}
for locale, listing in listings.items():
    terms = listing['keywords']
    assert 1 <= len(terms) <= 7 and len({term.casefold() for term in terms}) == len(terms), locale
    assert all(term.strip() == term and 0 < len(term) <= 40 and '\n' not in term for term in terms), locale
    assert all('提案です' not in term and 'キーワード' not in term for term in terms), locale
    description = listing['description']
    assert 100 <= len(description) <= 10000 and 'uwPDF' in description, locale
    assert not re.search(r'lorem ipsum|以下是|以下为|キーワードの提案|placeholder', description, re.I), locale
    assert not re.search(r'https?://', description), 'Links belong in the dedicated Properties fields.'
versions = set()
for name in ('Package.appxmanifest', 'Package.ARM64.appxmanifest'):
    manifest = ET.parse(ROOT / 'LitePdfViewer' / name)
    assert not list(manifest.findall('./{*}Capabilities/*')), name
    versions.add(manifest.find('./{*}Identity').attrib['Version'])
assert versions == {'1.0.14.0'}
source = (ROOT / 'LitePdfViewer/MainPage.xaml.cs').read_text(encoding='utf-8-sig')
assert 'RegisterCloseRequestedHandler' not in source and 'SystemNavigationManagerPreview' not in source
subprocess.run([sys.executable, str(ROOT / 'scripts/Sync-StorePrivacy.py'), '--check'], check=True)
print('PASS: four Store listings, seven relevant terms per locale, app descriptions, version and no restricted capabilities.')
