param([ValidateSet('None','UiWriterClose','UiTextClose')][string]$NegativeControl='None')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$outputRoot=Join-Path $repoRoot 'artifacts/native-tests/native-cleanup'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$export=(Get-Content (Join-Path $repoRoot 'LitePdfViewer/MainPage.Export.cs') -Raw).Replace("`r`n", "`n")
$start=$export.IndexOf('        private async void ExportButton_Click(')
$end=$export.LastIndexOf("`n    }")
if ($start -lt 0 -or $end -le $start) { throw 'Export handler boundaries changed' }
$export=$export.Substring($start,$end-$start)
$text=Get-Content (Join-Path $repoRoot 'LitePdfViewer/PdfTextSource.cs') -Raw
if ($NegativeControl -eq 'UiWriterClose') { $export=$export.Replace('await Task.Run(() => writer.Dispose());','writer.Dispose();') }
if ($NegativeControl -eq 'UiTextClose') { $text=$text.Replace('await Task.Run(() => opened.Dispose());','opened.Dispose();') }
$production=Join-Path $outputRoot "Cleanup.$NegativeControl.production.cs"
('using System; using System.Threading.Tasks; using PdfNative; using Windows.Storage; using Windows.Storage.Pickers; using Windows.UI.Xaml; using Windows.UI.Xaml.Controls; namespace LitePdfViewer { public sealed partial class MainPage { ' + $export + ' } }') | Set-Content $production -Encoding UTF8
$textPath=Join-Path $outputRoot "Text.$NegativeControl.production.cs"
$text | Set-Content $textPath -Encoding UTF8
$executable=Join-Path $outputRoot "PdfNativeCleanupTests.$NegativeControl.exe"
& 'C:/Program Files (x86)/MSBuild/14.0/Bin/csc.exe' /nologo /target:exe "/out:$executable" $production $textPath (Join-Path $repoRoot 'tests/PdfNativeCleanupTests.cs')
if ($LASTEXITCODE) { throw 'Cleanup test compilation failed' }
& $executable
if ($LASTEXITCODE) { throw "Cleanup tests failed ($NegativeControl)." }
if ($NegativeControl -ne 'None') { throw 'Negative control unexpectedly passed.' }
$hashes=[ordered]@{}
foreach ($relative in @('LitePdfViewer/MainPage.Export.cs','LitePdfViewer/PdfTextSource.cs','tests/PdfNativeCleanupTests.cs','scripts/Test-NativeCleanup.ps1')) { $hashes[$relative]=(Get-FileHash (Join-Path $repoRoot $relative)).Hash }
[pscustomobject]@{ completed_utc=[DateTime]::UtcNow.ToString('o');source_sha256=$hashes;scope='Actual export handler and PdfTextSource methods with platform/native doubles and a single-thread UI context. Proves UI heartbeat can run during teardown, input/cleanup ordering, success/error/cancellation paths and late-open release; excludes real PDF cleanup cost and GUI performance' } | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $outputRoot 'result.json') -Encoding UTF8
