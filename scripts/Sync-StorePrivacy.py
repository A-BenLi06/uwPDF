"""Keep the packaged policy, public Markdown and website in agreement."""
from pathlib import Path
import argparse
import json
import re
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser()
parser.add_argument('--check', action='store_true')
args = parser.parse_args()
policies = json.loads((ROOT / 'docs/store/privacy.json').read_text(encoding='utf-8'))
parts = ['# uwPDF Privacy Policy / 隐私政策 / 隱私權政策 / プライバシーポリシー\n',
         'Publisher: BenLi06 · Product ID: 9PP3QW43QJ77\n']
outputs = {}
for locale in ['en', 'zh-cn', 'zh-tw', 'ja']:
    policy = policies[locale]
    body = [policy['updated'], policy['intro']]
    parts.extend(['## ' + policy['title'] + ' (' + locale + ')\n', policy['updated'] + '\n', policy['intro'] + '\n'])
    for _, title, paragraphs in policy['sections']:
        body.extend([title] + paragraphs)
        parts.append('### ' + title + '\n')
        parts.extend(x + '\n' for x in paragraphs)
    contact = 'https://github.com/A-BenLi06/uwPDF/issues'
    body.append(contact)
    parts.append('Contact: ' + contact + '\n')
    language = {'en': 'en-US', 'zh-cn': 'zh-Hans', 'zh-tw': 'zh-Hant', 'ja': 'ja-JP'}[locale]
    path = ROOT / 'LitePdfViewer/Strings' / language / 'Resources.resw'
    text = path.read_text(encoding='utf-8-sig')
    support = {'en': 'Contact support', 'zh-cn': '联系支持', 'zh-tw': '聯絡支援', 'ja': 'サポート'}[locale]
    discard = {'en': 'The draft could not be discarded: {0}', 'zh-cn': '无法丢弃草稿：{0}',
               'zh-tw': '無法捨棄草稿：{0}', 'ja': '下書きを破棄できませんでした：{0}'}[locale]
    changed = {'en': 'This PDF changed outside uwPDF. Earlier local annotations and drafts were retained separately.',
               'zh-cn': 'PDF 已在其他应用中修改；先前的本地批注和草稿已另外保留。',
               'zh-tw': 'PDF 已在其他應用程式中變更；先前的本機註記和草稿已另外保留。',
               'ja': 'PDF が他のアプリで変更されました。以前のローカル注釈と下書きは別に保持されています。'}[locale]
    for key, value in {'PrivacyCommand.Text': policy['title'], 'PrivacyTitle': policy['title'],
                       'PrivacyBody': '\n\n'.join(body), 'PrivacySupport': support, 'DraftDiscardFailed': discard,
                       'AnnotationSourceChanged': changed}.items():
        element = '  <data name="' + key + '" xml:space="preserve">\n    <value>' + escape(value) + '</value>\n  </data>'
        pattern = r'  <data name="' + re.escape(key) + r'"[^>]*>.*?</data>'
        if re.search(pattern, text, re.S):
            text = re.sub(pattern, lambda _: element, text, flags=re.S)
        else:
            text = text.replace('</root>', element + '\n</root>')
    outputs[path] = text
outputs[ROOT / 'PRIVACY.md'] = '\n'.join(parts)
website = ROOT / 'website/src/i18n/privacy.json'
if website.parent.is_dir():
    outputs[website] = json.dumps(policies, ensure_ascii=False, indent=2) + '\n'
for path, content in outputs.items():
    if args.check:
        assert path.read_text(encoding='utf-8-sig') == content, f'Run scripts/Sync-StorePrivacy.py to update {path}'
    else:
        path.write_text(content, encoding='utf-8')
print('PASS: all four packaged policies, public Markdown and available website policies agree.')
