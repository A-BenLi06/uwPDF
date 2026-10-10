param(
    [ValidateSet('None', 'WaitForSizing', 'StaleFirstPage', 'ResizeOwnership', 'DuplicateProbe', 'NoFinalFit')]
    [string]$NegativeControl = 'None'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $repoRoot 'artifacts/native-tests/preview-startup'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
function Extract-Method([string]$Source, [string]$Start, [string]$End) {
    $startIndex = $Source.IndexOf($Start, [StringComparison]::Ordinal)
    $endIndex = $Source.IndexOf($End, $startIndex + $Start.Length, [StringComparison]::Ordinal)
    if ($startIndex -lt 0 -or $endIndex -le $startIndex) { throw "Production boundaries changed: $Start" }
    return $Source.Substring($startIndex, $endIndex - $startIndex)
}
$pageSource = Get-Content -LiteralPath (Join-Path $repoRoot 'LitePdfViewer/MainPage.xaml.cs') -Raw
$windowSource = Get-Content -LiteralPath (Join-Path $repoRoot 'LitePdfViewer/MainPage.Window.cs') -Raw
$methods = (Extract-Method $pageSource 'private async Task LoadDocumentAsync(' 'private void ThumbsToggle_Click(') +
    (Extract-Method $windowSource 'private async Task ApplyPreviewWindowSizeAsync(' 'private float FitZoom(')
switch ($NegativeControl) {
    'NoFinalFit' { $methods = $methods.Replace('                RebuildLayout();', '') }
    'WaitForSizing' { $methods = $methods.Replace('var previewSizing = ApplyPreviewWindowSizeAsync(renderToken, hasIntrinsicSize);', "var previewSizing = ApplyPreviewWindowSizeAsync(renderToken, hasIntrinsicSize);`n await previewSizing;") }
    'StaleFirstPage' { $methods = $methods -replace '(?s)(await RenderPageCoreAsync\(0, renderToken\);\s*\}\s*)if \(renderToken != activeRenderToken\) return;', '$1' }
    'ResizeOwnership' { $methods = $methods.Replace('if (applyingPreviewSizeToken == token) applyingPreviewSize = false;', 'applyingPreviewSize = false;') }
    'DuplicateProbe' { $methods = $methods.Replace('if (pendingWorkAreaRead == null || pendingWorkAreaRead.IsCompleted)', 'if (true)') }
}
$productionPath = Join-Path $outputRoot "MainPage.$NegativeControl.production.cs"
('using System; using System.Threading.Tasks; using Windows.Foundation; using Windows.UI.Core; using Windows.UI.ViewManagement; using Windows.UI.Xaml; using Windows.UI.Xaml.Controls; using Windows.Data.Json; using Windows.Data.Pdf; using Windows.Storage; using PreviewDisplay = PdfNative.PreviewDisplay; namespace LitePdfViewer { public sealed partial class MainPage { ' + $methods + ' } }') |
    Set-Content -LiteralPath $productionPath -Encoding UTF8
$executable = Join-Path $outputRoot "PdfPreviewStartupTests.$NegativeControl.exe"
& 'C:/Program Files (x86)/MSBuild/14.0/Bin/csc.exe' /nologo /target:exe "/out:$executable" $productionPath `
    (Join-Path $repoRoot 'LitePdfViewer/PreviewSizing.cs') `
    (Join-Path $repoRoot 'tests/PdfPreviewStartupTests.cs')
if ($LASTEXITCODE) { throw 'Production preview startup test compilation failed.' }
& $executable
if ($LASTEXITCODE) { throw "Production preview startup tests failed ($NegativeControl)." }
if ($NegativeControl -ne 'None') { throw 'Negative control unexpectedly passed.' }
$hashes = [ordered]@{}
foreach ($relative in @('LitePdfViewer/MainPage.xaml.cs', 'LitePdfViewer/MainPage.Window.cs', 'LitePdfViewer/PreviewSizing.cs', 'tests/PdfPreviewStartupTests.cs', 'scripts/Test-PreviewStartup.ps1')) {
    $hashes[$relative] = (Get-FileHash -LiteralPath (Join-Path $repoRoot $relative) -Algorithm SHA256).Hash
}
[pscustomobject]@{
    completed_utc = [DateTime]::UtcNow.ToString('o'); source_sha256 = $hashes
    scope = 'Actual LoadDocumentAsync, ApplyPreviewWindowSizeAsync, ReadWorkAreaAsync and legacy probe with platform/work doubles and a single-thread synchronization context; first-page/worker independence, final layout dispatch, file-switch races, manual resize, probe sharing/cleanup and nonfatal sizing failures. The final-layout double checks dispatch/zoom-mode ownership, not actual viewport math. Excludes actual window/monitor behavior, PDF rendering and measured latency'
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'result.json') -Encoding UTF8
