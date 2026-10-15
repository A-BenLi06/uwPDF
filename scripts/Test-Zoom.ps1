param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (Join-Path $repoRoot 'LitePdfViewer/MainPage.xaml.cs') -Raw
function Extract-ZoomMethod([string]$Start, [string]$End) {
    $first = $source.IndexOf($Start, [StringComparison]::Ordinal)
    $last = $source.IndexOf($End, $first + $Start.Length, [StringComparison]::Ordinal)
    if ($first -lt 0 -or $last -le $first) { throw "Production zoom boundary changed: $Start" }
    return $source.Substring($first, $last - $first)
}
$methods = (Extract-ZoomMethod 'private void UpdatePageCentering()' 'private void CreatePagePlaceholders(') +
    (Extract-ZoomMethod 'private void ResetInitialView()' 'private void SetZoom(float newZoom)') +
    (Extract-ZoomMethod 'private void SetZoom(float newZoom, bool animated)' 'private void ZoomSlider_ValueChanged(') +
    (Extract-ZoomMethod 'private void DocumentScroller_ViewChanged(' '// Scrollbar/layout changes can affect fit')
$outputRoot = Join-Path $repoRoot 'artifacts/native-tests/zoom'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$generated = Join-Path $outputRoot 'Zoom.production.cs'
('using System; using Windows.UI.Xaml.Controls; namespace LitePdfViewer { public sealed partial class MainPage { ' + $methods + ' } }') |
    Set-Content -LiteralPath $generated -Encoding UTF8
$exe = Join-Path $outputRoot 'PdfZoomTests.exe'
& 'C:/Program Files (x86)/MSBuild/14.0/Bin/csc.exe' /nologo /target:exe "/out:$exe" $generated (Join-Path $repoRoot 'tests/PdfZoomTests.cs') (Join-Path $repoRoot 'LitePdfViewer/PdfScrollActivity.cs')
if ($LASTEXITCODE) { throw 'Zoom regression test compilation failed.' }
& $exe
if ($LASTEXITCODE) { throw 'Zoom regression test failed.' }
