$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $repoRoot 'artifacts/native-tests/scrollbar-drag'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$exe = Join-Path $outputRoot 'PdfScrollBarDragTests.exe'
& 'C:/Program Files (x86)/MSBuild/14.0/Bin/csc.exe' /nologo /target:exe "/out:$exe" (Join-Path $repoRoot 'LitePdfViewer/PdfScrollBarDrag.cs') (Join-Path $repoRoot 'tests/PdfScrollBarDragTests.cs')
if ($LASTEXITCODE) { throw 'Scrollbar drag test compilation failed.' }
& $exe
if ($LASTEXITCODE) { throw 'Scrollbar drag test failed.' }
