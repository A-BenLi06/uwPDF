$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$vsRoot = 'C:/Program Files (x86)/Microsoft Visual Studio 14.0/VC'
$sdkRoot = 'C:/Program Files (x86)/Windows Kits/10'
$outputRoot = Join-Path $repoRoot 'artifacts/native-tests/legacy-renderer'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$savedInclude = $env:INCLUDE; $savedLib = $env:LIB; $savedLibPath = $env:LIBPATH
try {
    $env:INCLUDE = "$vsRoot/include;$sdkRoot/Include/10.0.14393.0/ucrt;$sdkRoot/Include/10.0.14393.0/shared;$sdkRoot/Include/10.0.14393.0/um;$sdkRoot/Include/10.0.14393.0/winrt"
    $env:LIB = "$vsRoot/lib/amd64;$sdkRoot/Lib/10.0.14393.0/ucrt/x64;$sdkRoot/Lib/10.0.14393.0/um/x64"
    $env:LIBPATH = "$vsRoot/vcpackages;$sdkRoot/UnionMetadata"
    $executable = Join-Path $outputRoot 'LegacyRendererSmoke.exe'
    & (Join-Path $vsRoot 'bin/amd64/cl.exe') /nologo /ZW /EHsc /MD /O2 /DWINAPI_FAMILY=WINAPI_FAMILY_APP "/AI$vsRoot/vcpackages" "/AI$sdkRoot/UnionMetadata" "/Fo$outputRoot/" "/Fe$executable" (Join-Path $repoRoot 'tests/LegacyRendererSmoke.cpp') (Join-Path $repoRoot 'PdfNative/PdfSurfaceRenderer.cpp') (Join-Path $repoRoot 'PdfNative/PdfRenderDevice.cpp') /link /WINMD:NO runtimeobject.lib windows.data.pdf.lib d3d11.lib d2d1.lib
    if ($LASTEXITCODE) { throw 'Legacy renderer smoke compilation failed.' }
    & $executable
    if ($LASTEXITCODE) { throw 'Legacy renderer smoke checks failed.' }
}
finally { $env:INCLUDE = $savedInclude; $env:LIB = $savedLib; $env:LIBPATH = $savedLibPath }
