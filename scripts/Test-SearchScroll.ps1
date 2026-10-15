$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (Join-Path $repoRoot 'LitePdfViewer/MainPage.Search.cs') -Raw
$start = $source.IndexOf('        private async Task ScrollToSearchMatchAsync(')
$end = $source.IndexOf('        private void UpdateSearchFocus()', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Search scroll production boundaries changed.' }
$output = Join-Path $repoRoot 'artifacts/native-tests/search-scroll'
New-Item -ItemType Directory $output -Force | Out-Null
$generated = Join-Path $output 'SearchScroll.production.cs'
('using System; using System.Threading; using System.Threading.Tasks; using Windows.UI.Xaml.Controls; namespace LitePdfViewer { public sealed partial class MainPage { ' + $source.Substring($start, $end-$start) + ' } }') | Set-Content $generated -Encoding UTF8
$exe = Join-Path $output 'PdfSearchScrollTests.exe'
& 'C:/Program Files (x86)/MSBuild/14.0/Bin/csc.exe' /nologo /target:exe "/out:$exe" $generated (Join-Path $repoRoot 'tests/PdfSearchScrollTests.cs')
if ($LASTEXITCODE) { throw 'Search scroll test compilation failed.' }
& $exe
if ($LASTEXITCODE) { throw 'Search scroll test failed.' }
