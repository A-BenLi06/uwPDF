$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $repoRoot 'artifacts/native-tests'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$compiler = 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$executable = Join-Path $outputRoot 'PdfRenderSchedulerTests.exe'
& $compiler /nologo /target:exe "/out:$executable" (Join-Path $repoRoot 'LitePdfViewer/PdfRenderScheduler.cs') (Join-Path $repoRoot 'tests/PdfRenderSchedulerTests.cs')
if ($LASTEXITCODE) { throw 'Render scheduler test compilation failed.' }
& $executable
if ($LASTEXITCODE) { throw 'Render scheduler regression tests failed.' }
