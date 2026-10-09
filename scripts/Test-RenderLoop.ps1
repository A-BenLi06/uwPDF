$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $repoRoot 'artifacts/native-tests'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$sourcePath = Join-Path $repoRoot 'LitePdfViewer/MainPage.xaml.cs'
$source = Get-Content -LiteralPath $sourcePath -Raw
$start = $source.IndexOf('private async Task RunRenderLoopAsync(ulong token)', [StringComparison]::Ordinal)
$end = $source.IndexOf('private int FindNextRenderIndex()', [StringComparison]::Ordinal)
if ($start -lt 0 -or $end -le $start) { throw 'Production render-loop boundaries changed; update the test extraction.' }
$method = $source.Substring($start, $end - $start)
$generatedPath = Join-Path $outputRoot 'MainPage.RenderLoop.production.cs'
('using System; using System.Threading.Tasks; using Windows.UI.Xaml; namespace LitePdfViewer { public sealed partial class MainPage { ' + $method + ' } }') | Set-Content -LiteralPath $generatedPath -Encoding UTF8
$executable = Join-Path $outputRoot 'PdfRenderLoopTests.exe'
& 'C:/Program Files (x86)/MSBuild/14.0/Bin/csc.exe' /nologo /target:exe "/out:$executable" $generatedPath (Join-Path $repoRoot 'tests/PdfRenderLoopTests.cs')
if ($LASTEXITCODE) { throw 'Render-loop test compilation failed.' }
& $executable
if ($LASTEXITCODE) { throw 'Production render-loop regression test failed.' }
[PSCustomObject]@{
    completed_utc = [DateTime]::UtcNow.ToString('o')
    source_sha256 = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
    scope = 'Actual current RunRenderLoopAsync with platform/work doubles; excludes XAML presentation and input latency'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'render-loop-result.json') -Encoding UTF8
