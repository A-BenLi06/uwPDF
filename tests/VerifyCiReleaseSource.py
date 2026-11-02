"""Check tag/source agreement using an isolated local Git repository."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parent.parent
TAG = 'v1.2.3.0'
ENV = {key: value for key, value in os.environ.items() if key.lower() != 'psmodulepath'}
ENV.update(GIT_AUTHOR_NAME='uwPDF CI tests', GIT_COMMITTER_NAME='uwPDF CI tests',
           GIT_AUTHOR_EMAIL='ci-tests@example.invalid', GIT_COMMITTER_EMAIL='ci-tests@example.invalid')


def main():
    with tempfile.TemporaryDirectory(prefix='uwpdf-source-gate-') as temp:
        repo = Path(temp)

        def git(*args):
            return subprocess.run(['git', '-C', str(repo), *args], env=ENV, check=True,
                                  capture_output=True, text=True).stdout.strip()

        def check(accepted, tag=TAG):
            result = subprocess.run([
                'powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
                str(repo / 'scripts/Test-ReleaseSource.ps1'), '-Tag', tag,
            ], env=ENV, capture_output=True, text=True)
            assert (result.returncode == 0) == accepted, result.stdout + result.stderr

        (repo / 'scripts').mkdir()
        shutil.copyfile(ROOT / 'scripts/Test-ReleaseSource.ps1', repo / 'scripts/Test-ReleaseSource.ps1')
        (repo / 'LitePdfViewer').mkdir()
        for name in ('Package.appxmanifest', 'Package.ARM64.appxmanifest'):
            (repo / 'LitePdfViewer' / name).write_text('<Package><Identity Version="1.2.3.0" /></Package>')
        (repo / 'docs/releases').mkdir(parents=True)
        notes = repo / f'docs/releases/{TAG}.md'
        notes.write_text('Fixture user-visible improvements.\n')
        git('init', '-q')
        git('add', '.')
        git('commit', '-qm', 'Source gate fixture')
        commit = git('rev-parse', 'HEAD')
        git('update-ref', 'refs/remotes/origin/main', commit)
        git('tag', TAG)
        check(True)
        check(False, 'v1.2.3')
        check(False, 'v1.2.4.0')
        git('tag', '-d', TAG)
        git('tag', '-a', TAG, '-m', 'Annotated fixture')
        check(True)
        notes.write_text('')
        check(False)
        notes.unlink()
        check(False)
        git('checkout', '--', str(notes.relative_to(repo)))
        (repo / 'LitePdfViewer/Package.ARM64.appxmanifest').write_text('<Package><Identity Version="1.2.4.0" /></Package>')
        check(False)
        git('checkout', '--', 'LitePdfViewer/Package.ARM64.appxmanifest')
        notes.write_text('Second source fixture.\n')
        git('add', '.')
        git('commit', '-qm', 'Source outside main')
        check(False)  # Tag still points to the old commit.
        git('tag', '-f', TAG)
        check(False)  # Correct tag/HEAD, but source is not on main.
    print('PASS: lightweight/annotated tags accepted; invalid versions, absent notes, '
          'tag/source mismatches and source outside main rejected.')


if __name__ == '__main__':
    main()
