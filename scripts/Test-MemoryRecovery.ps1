$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $repoRoot 'artifacts/native-tests'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$renderingSource = Get-Content -LiteralPath (Join-Path $repoRoot 'LitePdfViewer/MainPage.Rendering.cs') -Raw
$pageSource = Get-Content -LiteralPath (Join-Path $repoRoot 'LitePdfViewer/MainPage.xaml.cs') -Raw
function Extract-Method([string]$Source, [string]$Start, [string]$End) {
    $startIndex = $Source.IndexOf($Start, [StringComparison]::Ordinal)
    $endIndex = $Source.IndexOf($End, $startIndex + $Start.Length, [StringComparison]::Ordinal)
    if ($startIndex -lt 0 -or $endIndex -le $startIndex) { throw "Production method boundaries changed: $Start" }
    return $Source.Substring($startIndex, $endIndex - $startIndex)
}
$methods = @(
    (Extract-Method $renderingSource 'private void ClearLostRenderSurfaces()' 'private void AttachDetailLayer('),
    (Extract-Method $renderingSource 'private void TrimBitmapCache(' "`n    }"),
    (Extract-Method $pageSource 'private static void ReleaseCleanAnnotations(' 'private void MarkPageDirty('),
    (Extract-Method $pageSource 'private int FindNextRenderIndex()' 'private bool PageNeedsWork(')
)
$generatedPath = Join-Path $outputRoot 'MainPage.MemoryRecovery.production.cs'
('using System; using System.Collections.Generic; using Windows.UI.Xaml.Media.Imaging; namespace LitePdfViewer { public sealed partial class MainPage { ' + ($methods -join "`n") + ' } }') | Set-Content -LiteralPath $generatedPath -Encoding UTF8
$executable = Join-Path $outputRoot 'PdfMemoryRecoveryTests.exe'
& 'C:/Program Files (x86)/MSBuild/14.0/Bin/csc.exe' /nologo /target:exe "/out:$executable" $generatedPath `
    (Join-Path $repoRoot 'LitePdfViewer/MainPage.Memory.cs') `
    (Join-Path $repoRoot 'LitePdfViewer/PdfMemoryBudget.cs') `
    (Join-Path $repoRoot 'LitePdfViewer/PdfRenderScheduler.cs') `
    (Join-Path $repoRoot 'LitePdfViewer/PdfRasterRetirement.cs') `
    (Join-Path $repoRoot 'tests/PdfMemoryRecoveryTests.cs')
if ($LASTEXITCODE) { throw 'Memory/recovery test compilation failed.' }
& $executable
if ($LASTEXITCODE) { throw 'Production memory/recovery regression test failed.' }
$hashes = @{}
foreach ($relative in @('LitePdfViewer/MainPage.Rendering.cs', 'LitePdfViewer/MainPage.Memory.cs', 'LitePdfViewer/MainPage.xaml.cs', 'LitePdfViewer/PdfMemoryBudget.cs', 'LitePdfViewer/PdfRasterRetirement.cs', 'LitePdfViewer/PdfRenderScheduler.cs', 'tests/PdfMemoryRecoveryTests.cs', 'scripts/Test-MemoryRecovery.ps1')) {
    $hashes[$relative] = (Get-FileHash -LiteralPath (Join-Path $repoRoot $relative) -Algorithm SHA256).Hash
}
[PSCustomObject]@{
    completed_utc = [DateTime]::UtcNow.ToString('o')
    source_sha256 = $hashes
    scope = 'Production memory event/controller/policy, cache trimming, failure recovery and clean annotation eviction with UWP/platform/work doubles; excludes actual OS pressure and XAML/GPU presentation'
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputRoot 'memory-recovery-result.json') -Encoding UTF8
