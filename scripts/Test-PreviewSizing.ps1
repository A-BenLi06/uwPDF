$ErrorActionPreference='Stop'
$sizingRepo=Split-Path $PSScriptRoot -Parent
$sizingTests=Join-Path $sizingRepo 'artifacts/native-tests/preview-sizing'
New-Item -ItemType Directory -Path $sizingTests -Force | Out-Null
$sizingExe=Join-Path $sizingTests 'PreviewSizingTests.exe'
& (& (Join-Path $PSScriptRoot 'Get-TestCSharpCompiler.ps1')) /nologo /target:exe "/out:$sizingExe" (Join-Path $sizingRepo 'LitePdfViewer/PreviewSizing.cs') (Join-Path $sizingRepo 'tests/PreviewSizingTests.cs')
if($LASTEXITCODE) { throw 'Preview sizing test compilation failed.' }
& $sizingExe
if($LASTEXITCODE) { throw 'Preview sizing tests failed.' }
