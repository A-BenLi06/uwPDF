$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $repoRoot 'artifacts/native-tests'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$compiler = 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$executable = Join-Path $outputRoot 'PdfTextSearchTests.exe'
& $compiler /nologo /target:exe "/out:$executable" (Join-Path $repoRoot 'LitePdfViewer/PdfTextSearch.cs') (Join-Path $repoRoot 'tests/PdfTextSearchTests.cs')
if ($LASTEXITCODE) { throw 'Text search test compilation failed.' }
& $executable
if ($LASTEXITCODE) { throw 'Text search regression tests failed.' }
