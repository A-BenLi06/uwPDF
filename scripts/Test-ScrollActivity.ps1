$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $repoRoot 'artifacts/native-tests'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$executable = Join-Path $outputRoot 'PdfScrollActivityTests.exe'
& 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe' /nologo /target:exe "/out:$executable" (Join-Path $repoRoot 'LitePdfViewer/PdfScrollActivity.cs') (Join-Path $repoRoot 'tests/PdfScrollActivityTests.cs')
if ($LASTEXITCODE) { throw 'Scroll activity test compilation failed.' }
& $executable
if ($LASTEXITCODE) { throw 'Scroll activity tests failed.' }
