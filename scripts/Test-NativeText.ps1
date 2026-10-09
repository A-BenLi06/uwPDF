param(
    [string]$PdfPath = 'LitePdfViewer/TestAssets/SkimSample.pdf',
    [uint32]$PageIndex = 0,
    [string]$ExpectedText = 'PDF'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$vsRoot = 'C:/Program Files (x86)/Microsoft Visual Studio 14.0/VC'
$sdkRoot = 'C:/Program Files (x86)/Windows Kits/10'
$outputRoot = Join-Path $repoRoot 'artifacts/native-tests'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$env:INCLUDE = "$vsRoot/include;$sdkRoot/Include/10.0.14393.0/ucrt;$sdkRoot/Include/10.0.14393.0/shared;$sdkRoot/Include/10.0.14393.0/um;$sdkRoot/Include/10.0.14393.0/winrt;$repoRoot/artifacts/mupdf-source/include"
$env:LIB = "$vsRoot/lib/amd64;$sdkRoot/Lib/10.0.14393.0/ucrt/x64;$sdkRoot/Lib/10.0.14393.0/um/x64"
$env:LIBPATH = "$vsRoot/vcpackages;$sdkRoot/UnionMetadata"
$compiler = Join-Path $vsRoot 'bin/amd64/cl.exe'
& $compiler /nologo /ZW /EHsc /MDd /DWINAPI_FAMILY=WINAPI_FAMILY_APP "/AI$vsRoot/vcpackages" "/AI$sdkRoot/UnionMetadata" "/Fo$outputRoot/" "/Fd$outputRoot/native-tests.pdb" "/Fe$outputRoot/NativeTextSmoke.exe" (Join-Path $repoRoot 'tests/NativeTextSmoke.cpp') (Join-Path $repoRoot 'PdfNative/PdfTextDocument.cpp') (Join-Path $repoRoot 'PdfNative/PdfDocumentCore.cpp') (Join-Path $repoRoot 'PdfNative/PdfAnnotationJson.cpp') /link /WINMD:NO (Join-Path $repoRoot 'PdfNative/bin/x64/Debug/MuPdfCore.lib') runtimeobject.lib bcrypt.lib
if ($LASTEXITCODE) { throw 'Native text test compilation failed.' }
& (Join-Path $outputRoot 'NativeTextSmoke.exe') (Resolve-Path $PdfPath).Path $PageIndex $ExpectedText
if ($LASTEXITCODE) { throw "Native text test failed: $LASTEXITCODE" }
