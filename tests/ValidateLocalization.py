"""Validate shipped localization coverage and Store identity without changing the app."""
from pathlib import Path
import json
import re
import string
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / 'LitePdfViewer'
LANGUAGES = {'en-US', 'ja-JP', 'zh-Hans', 'zh-Hant'}


def validate():
    catalog = {}
    for language in LANGUAGES:
        entries = ET.parse(APP / 'Strings' / language / 'Resources.resw').findall('data')
        strings = {entry.attrib['name']: entry.findtext('value') for entry in entries}
        assert len(strings) == len(entries), f'{language}: duplicate resource keys'
        assert len({key.lower() for key in strings}) == len(strings), f'{language}: case collision'
        assert all(strings.values()), f'{language}: empty translation'
        assert not any(other.startswith(key + '.') for key in strings for other in strings), f'{language}: resource/scope collision'
        catalog[language] = strings
    reference = catalog['en-US']
    for language, strings in catalog.items():
        assert strings.keys() == reference.keys(), f'{language}: missing or extra keys'
        for key, value in strings.items():
            fields = lambda text: sorted(field for _, field, _, _ in string.Formatter().parse(text) if field is not None)
            assert fields(value) == fields(reference[key]), f'{language}/{key}: broken format arguments'

    for source in APP.glob('*.cs'):
        text = source.read_text(encoding='utf-8-sig')
        for key in re.findall(r'L\.(?:Get|Format)\("([^"]+)"', text):
            assert key in reference, f'{source.name}: missing key {key}'
        # User-facing CJK strings belong in RESW, not the C# implementation.
        assert not re.search(r'"[^"\n]*[\u3400-\u9fff][^"\n]*"', text), f'{source.name}: hardcoded UI text'

    ns = {'x': 'http://schemas.microsoft.com/winfx/2006/xaml'}
    page = ET.parse(APP / 'MainPage.xaml')
    for element in page.iter():
        uid = element.attrib.get('{' + ns['x'] + '}Uid')
        if uid:
            assert any(key.startswith(uid + '.') for key in reference), f'Unbound x:Uid {uid}'
        for prop, value in element.attrib.items():
            assert not re.search('[\u3400-\u9fff]', value), f'Hardcoded XAML {prop}'
    # Catch accidentally swapped scripts, not just identical key sets.
    assert catalog['zh-Hans']['Save'] == '保存'
    assert catalog['zh-Hant']['Save'] == '儲存'
    assert catalog['ja-JP']['Cancel'] == 'キャンセル'
    assert catalog['en-US']['MatchCount'].format(4, 17) == '4 of 17'
    assert catalog['zh-Hant']['MatchCount'].format(4, 17) == '第4/17個'

    identity = json.loads((APP / 'StoreIdentity.json').read_text(encoding='utf-8'))
    assert identity['storeId'] == '9PP3QW43QJ77'
    assert identity['packageFamilyName'] == identity['name'] + '_7cnye3b758vcr'
    for key, value in identity.items():
        assert value == value.strip() and '&#' not in value, f'{key}: pasted whitespace/entity'
    for name in ['Package.appxmanifest', 'Package.ARM64.appxmanifest']:
        manifest = ET.parse(APP / name)
        package = manifest.getroot()
        find = lambda path: package.find('./{*}' + path)
        assert find('Identity').attrib['Name'] == identity['name']
        assert find('Identity').attrib['Publisher'] == identity['publisher']
        assert find('Properties').findtext('{*}PublisherDisplayName') == identity['publisherDisplayName']
        for element in package.iter():
            for value in list(element.attrib.values()) + [element.text or '']:
                if value.startswith('ms-resource:'):
                    assert value.split(':', 1)[1] in reference, f'{name}: unresolved {value}'
        assert find('Resources').find('{*}Resource').attrib['Language'] == 'x-generate'
    for name in ['LitePdfViewer.csproj', 'LitePdfViewer.ARM64.csproj']:
        project = ET.parse(APP / name)
        assert project.find('.//{*}DefaultLanguage').text == 'en-US'
    print(f'PASS: {len(reference)} complete resources in all four languages; format arguments, x:Uid/code/manifest coverage, script-specific strings and Store identity.')


if __name__ == '__main__':
    validate()
