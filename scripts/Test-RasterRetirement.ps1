$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $repoRoot 'artifacts/native-tests'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$executable = Join-Path $outputRoot 'PdfRasterRetirementTests.exe'
& 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe' /nologo /target:exe "/out:$executable" (Join-Path $repoRoot 'LitePdfViewer/PdfRasterRetirement.cs') (Join-Path $repoRoot 'tests/PdfRasterRetirementTests.cs')
if ($LASTEXITCODE) { throw 'Raster retirement test compilation failed.' }
& $executable
if ($LASTEXITCODE) { throw 'Raster retirement tests failed.' }
