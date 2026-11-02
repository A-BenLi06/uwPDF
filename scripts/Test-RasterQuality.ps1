$ErrorActionPreference='Stop'
$qualityRepo=Split-Path $PSScriptRoot -Parent
$qualityOutput=Join-Path $qualityRepo 'artifacts/native-tests/raster-quality'
New-Item $qualityOutput -ItemType Directory -Force | Out-Null
$qualityExe=Join-Path $qualityOutput 'PdfRasterQualityTests.exe'
& (& (Join-Path $PSScriptRoot 'Get-TestCSharpCompiler.ps1')) /nologo /target:exe "/out:$qualityExe" (Join-Path $qualityRepo 'LitePdfViewer/PdfRasterQuality.cs') (Join-Path $qualityRepo 'tests/PdfRasterQualityTests.cs')
if ($LASTEXITCODE) { throw 'Raster quality test build failed.' }
& $qualityExe
if ($LASTEXITCODE) { throw 'Raster quality tests failed.' }
