$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $repoRoot 'artifacts/native-tests'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$compiler = 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$executable = Join-Path $outputRoot 'PdfViewportAnchorTests.exe'
& $compiler /nologo /target:exe "/out:$executable" (Join-Path $repoRoot 'LitePdfViewer/PdfViewportAnchor.cs') (Join-Path $repoRoot 'tests/PdfViewportAnchorTests.cs')
if ($LASTEXITCODE) { throw 'Page geometry test compilation failed.' }
& $executable
if ($LASTEXITCODE) { throw 'Page geometry regression tests failed.' }
