$ErrorActionPreference='Stop'
$printRepo=Split-Path $PSScriptRoot -Parent
$printTests=Join-Path $printRepo 'artifacts/native-tests/printing'
New-Item -ItemType Directory -Path $printTests -Force | Out-Null
$printExe=Join-Path $printTests 'PdfPrintLayoutTests.exe'
& (& (Join-Path $PSScriptRoot 'Get-TestCSharpCompiler.ps1')) /nologo /target:exe "/out:$printExe" (Join-Path $printRepo 'LitePdfViewer/PdfPrintLayout.cs') (Join-Path $printRepo 'tests/PdfPrintLayoutTests.cs')
if($LASTEXITCODE) { throw 'Print layout test compilation failed.' }
& $printExe
if($LASTEXITCODE) { throw 'Print layout tests failed.' }
