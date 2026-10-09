$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $repoRoot 'artifacts/native-tests'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$executable = Join-Path $outputRoot 'PdfRasterBackendTests.exe'
& 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe' /nologo /target:exe "/out:$executable" (Join-Path $repoRoot 'LitePdfViewer/PdfPageRaster.cs') (Join-Path $repoRoot 'tests/PdfRasterBackendStubs.cs') (Join-Path $repoRoot 'tests/PdfRasterBackendTests.cs')
if ($LASTEXITCODE) { throw 'Raster backend test compilation failed.' }
& $executable
if ($LASTEXITCODE) { throw 'Raster backend tests failed.' }
