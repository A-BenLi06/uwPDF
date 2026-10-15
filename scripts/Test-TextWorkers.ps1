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
        $search=$search.Replace('var next = (searchHitIndex + (forward ? 1 : -1) + searchHits.Count) % searchHits.Count;',
            'var next = searchHits.FindIndex(candidate => candidate.Page == pageIndex);')
    }
    'UiGlyphs' { $text=$text.Replace('await Task.Run(() => ConvertGlyphs(text, values, cancellation))', 'ConvertGlyphs(text, values, cancellation)') }
    'UiSearch' {
        $search=$search.Replace('return Task.Run(() =>','return Task.FromResult(((Func<List<SearchHit>>)(() =>')
        $search=$search.Replace('            });','            }))());')
    }
    'StaleSearch' {
        $search=$search.Replace('if (version != searchVersion || token != activeRenderToken) return;', '')
    }
}
$selectionSource=(Get-Content (Join-Path $repoRoot 'LitePdfViewer/MainPage.Text.cs') -Raw -Encoding UTF8).Replace("`r`n","`n")
$copyStart=$selectionSource.IndexOf('        private string SelectedText()')
$copyEnd=$selectionSource.IndexOf('        private async void CopySelectedText()', $copyStart)
$spacingStart=$selectionSource.IndexOf('        private static void GlyphSpacing(')
$spacingEnd=$selectionSource.IndexOf('        private void ClearTextSelection()', $spacingStart)
if ($copyStart -lt 0 -or $copyEnd -lt 0 -or $spacingStart -lt 0 -or $spacingEnd -lt 0) { throw 'Selection method boundaries changed' }
$search += $selectionSource.Substring($copyStart,$copyEnd-$copyStart) + $selectionSource.Substring($spacingStart,$spacingEnd-$spacingStart)
$rangeSource=(Get-Content (Join-Path $repoRoot 'LitePdfViewer/MainPage.Selection.cs') -Raw -Encoding UTF8).Replace("`r`n","`n")
$rangeStart=$rangeSource.IndexOf('        private bool TryGetSelectionRange(')
$rangeEnd=$rangeSource.IndexOf('        private bool IsTextSelectionEndpoint(',$rangeStart)
$appendStart=$rangeSource.IndexOf('        private static void AppendSelectedPageText(')
$appendEnd=$rangeSource.IndexOf('        private async Task<string> SelectedTextAsync(',$appendStart)
$search += $rangeSource.Substring($rangeStart,$rangeEnd-$rangeStart)+$rangeSource.Substring($appendStart,$appendEnd-$appendStart)
$textPath=Join-Path $outputRoot "Text.$NegativeControl.production.cs"
$searchPath=Join-Path $outputRoot "Search.$NegativeControl.production.cs"
$text | Set-Content $textPath -Encoding UTF8
('using System; using System.Text; using System.Collections.Generic; using System.Threading; using System.Threading.Tasks; namespace LitePdfViewer { public sealed partial class MainPage { ' + $search + ' } }') | Set-Content $searchPath -Encoding UTF8
$exe=Join-Path $outputRoot "PdfTextWorkerTests.$NegativeControl.exe"
& 'C:/Program Files (x86)/MSBuild/14.0/Bin/csc.exe' /nologo /target:exe /r:System.Xml.Linq.dll "/out:$exe" $textPath $searchPath (Join-Path $repoRoot 'LitePdfViewer/PdfTextSearch.cs') (Join-Path $repoRoot 'LitePdfViewer/L.cs') (Join-Path $repoRoot 'tests/ResourceLoaderStub.cs') (Join-Path $repoRoot 'tests/PdfTextWorkerTests.cs')
if ($LASTEXITCODE) { throw 'Text worker test compilation failed' }
& $exe
if ($LASTEXITCODE) { throw "Text worker tests failed ($NegativeControl)." }
if ($NegativeControl -ne 'None') { throw 'Negative control unexpectedly passed.' }
$hashes=[ordered]@{}
foreach ($relative in @('LitePdfViewer/PdfTextSource.cs','LitePdfViewer/PdfTextSearch.cs','LitePdfViewer/MainPage.Search.cs','tests/PdfTextWorkerTests.cs','scripts/Test-TextWorkers.ps1')) { $hashes[$relative]=(Get-FileHash (Join-Path $repoRoot $relative)).Hash }
[pscustomobject]@{completed_utc=[DateTime]::UtcNow.ToString('o');source_sha256=$hashes;scope='Production text source, page matching and search loop with synchronous native/platform doubles and a single-thread UI context. Verifies worker scheduling, geometry/UTF-16 mapping, cancellation, disposal, cached-page searches and stale-result rejection; excludes native extraction cost and real GUI latency'} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $outputRoot 'result.json') -Encoding UTF8
