param([switch]$AllowAltNegativeControl)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$source=(Get-Content -LiteralPath (Join-Path $repoRoot 'LitePdfViewer/MainPage.xaml.cs') -Raw).Replace("`r`n","`n")
$start=$source.IndexOf('        private async void Page_KeyDown(')
$end=$source.IndexOf('        private void GoToPage(', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Keyboard method boundaries changed' }
$method=$source.Substring($start,$end-$start)
if ($AllowAltNegativeControl) {
    $guard="            if (Window.Current.CoreWindow.GetKeyState(Windows.System.VirtualKey.Menu)`n                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) return;"
    if (!$method.Contains($guard)) { throw 'Alt guard boundary changed' }
    $method=$method.Replace($guard,'')
}
$tag=if ($AllowAltNegativeControl) { 'allow-alt' } else { 'current' }
$outputRoot=Join-Path $repoRoot 'artifacts/native-tests/keyboard'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$generated=Join-Path $outputRoot "Keyboard.$tag.production.cs"
('using System; using System.Threading.Tasks; using Windows.UI.Xaml; using Windows.UI.Xaml.Controls; using Windows.UI.Xaml.Input; namespace LitePdfViewer { public sealed partial class MainPage { '+$method+' } }') |
    Set-Content -LiteralPath $generated -Encoding UTF8
$exe=Join-Path $outputRoot "PdfKeyboardTests.$tag.exe"
& 'C:/Program Files (x86)/MSBuild/14.0/Bin/csc.exe' /nologo /target:exe "/out:$exe" $generated (Join-Path $repoRoot 'tests/PdfKeyboardTests.cs')
if ($LASTEXITCODE) { throw 'Keyboard test compilation failed' }
& $exe
if ($LASTEXITCODE) { throw "Keyboard regression test failed ($tag)" }
if ($AllowAltNegativeControl) { throw 'Alt negative control unexpectedly passed' }
$hashes=[ordered]@{}
foreach ($path in @('LitePdfViewer/MainPage.xaml.cs','tests/PdfKeyboardTests.cs','scripts/Test-Keyboard.ps1')) {
    $hashes[$path]=(Get-FileHash -LiteralPath (Join-Path $repoRoot $path)).Hash
}
[pscustomobject]@{completed_utc=[DateTime]::UtcNow.ToString('o');source_sha256=$hashes
    scope='Actual Page_KeyDown with synchronous action/key-state doubles: Alt/AltGr-style chords, plain page navigation, search/save/open/copy shortcuts and native text input. Excludes actual OS system-menu routing and input latency.'} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'result.json') -Encoding UTF8
