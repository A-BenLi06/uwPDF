param([ValidateSet('None','UiGlyphs','UiSearch','StaleSearch','ReadingCursor')][string]$NegativeControl='None')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$outputRoot=Join-Path $repoRoot 'artifacts/native-tests/text-workers'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$text=Get-Content (Join-Path $repoRoot 'LitePdfViewer/PdfTextSource.cs') -Raw -Encoding UTF8
$search=(Get-Content (Join-Path $repoRoot 'LitePdfViewer/MainPage.Search.cs') -Raw -Encoding UTF8).Replace("`r`n","`n")
$start=$search.IndexOf('        private async Task FindTextAsync(')
$end=$search.IndexOf('        private async Task ShowSearchMatchAsync(')
if ($start -lt 0 -or $end -le $start) { throw 'Search method boundaries changed' }
$search=$search.Substring($start,$end-$start)
switch ($NegativeControl) {
    'ReadingCursor' {
        $search=$search.Replace('var start = continueSelection ? (int)selectedTextPage.Index : (int)pageIndex;', 'var start = (int)pageIndex;')
        $search=$search.Replace('var anchor = continueSelection', 'var anchor = continueSelection && selectedTextPage.Index == pageIndex')
    }
    'UiGlyphs' { $text=$text.Replace('await Task.Run(() => ConvertGlyphs(text, values, cancellation), cancellation)', 'ConvertGlyphs(text, values, cancellation)') }
    'UiSearch' {
        $search=$search.Replace('return Task.Run(() =>','return Task.FromResult(((Func<PageSearchResult>)(() =>')
        $search=$search.Replace('            }, cancellation);','            }))());')
    }
    'StaleSearch' {
        $search=$search.Replace("                    cancellation.ThrowIfCancellationRequested();`n                    if (version != searchVersion || token != activeRenderToken) return;`n                    var found = result.Found;", '                    var found = result.Found;')
    }
}
$textPath=Join-Path $outputRoot "Text.$NegativeControl.production.cs"
$searchPath=Join-Path $outputRoot "Search.$NegativeControl.production.cs"
$text | Set-Content $textPath -Encoding UTF8
('using System; using System.Collections.Generic; using System.Threading; using System.Threading.Tasks; namespace LitePdfViewer { public sealed partial class MainPage { ' + $search + ' } }') | Set-Content $searchPath -Encoding UTF8
$exe=Join-Path $outputRoot "PdfTextWorkerTests.$NegativeControl.exe"
& 'C:/Program Files (x86)/MSBuild/14.0/Bin/csc.exe' /nologo /target:exe "/out:$exe" $textPath $searchPath (Join-Path $repoRoot 'LitePdfViewer/PdfTextSearch.cs') (Join-Path $repoRoot 'tests/PdfTextWorkerTests.cs')
if ($LASTEXITCODE) { throw 'Text worker test compilation failed' }
& $exe
if ($LASTEXITCODE) { throw "Text worker tests failed ($NegativeControl)." }
if ($NegativeControl -ne 'None') { throw 'Negative control unexpectedly passed.' }
$hashes=[ordered]@{}
foreach ($relative in @('LitePdfViewer/PdfTextSource.cs','LitePdfViewer/PdfTextSearch.cs','LitePdfViewer/MainPage.Search.cs','tests/PdfTextWorkerTests.cs','scripts/Test-TextWorkers.ps1')) { $hashes[$relative]=(Get-FileHash (Join-Path $repoRoot $relative)).Hash }
[pscustomobject]@{completed_utc=[DateTime]::UtcNow.ToString('o');source_sha256=$hashes;scope='Production text source, page matching and search loop with synchronous native/platform doubles and a single-thread UI context. Verifies worker scheduling, geometry/UTF-16 mapping, cancellation, disposal, cached-page searches and stale-result rejection; excludes native extraction cost and real GUI latency'} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $outputRoot 'result.json') -Encoding UTF8
