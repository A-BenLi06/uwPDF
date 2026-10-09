param([ValidateRange(1, 1000)][int]$RepeatCount = 1)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$vsRoot = 'C:/Program Files (x86)/Microsoft Visual Studio 14.0/VC'
$sdkRoot = 'C:/Program Files (x86)/Windows Kits/10'
$outputRoot = Join-Path $repoRoot 'artifacts/native-tests'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$env:INCLUDE = "$vsRoot/include;$sdkRoot/Include/10.0.14393.0/ucrt;$sdkRoot/Include/10.0.14393.0/shared;$sdkRoot/Include/10.0.14393.0/um;$sdkRoot/Include/10.0.14393.0/winrt"
$env:LIB = "$vsRoot/lib/amd64;$sdkRoot/Lib/10.0.14393.0/ucrt/x64;$sdkRoot/Lib/10.0.14393.0/um/x64"
$env:LIBPATH = "$vsRoot/vcpackages;$sdkRoot/UnionMetadata"
& (Join-Path $vsRoot 'bin/amd64/cl.exe') /nologo /ZW /EHsc /MDd /DWINAPI_FAMILY=WINAPI_FAMILY_APP "/AI$vsRoot/vcpackages" "/AI$sdkRoot/UnionMetadata" "/Fo$outputRoot/" "/Fd$outputRoot/native-ink.pdb" "/Fe$outputRoot/NativeInkSmoke.exe" (Join-Path $repoRoot 'tests/NativeInkSmoke.cpp') (Join-Path $repoRoot 'PdfNative/InkOutlineExporter.cpp') (Join-Path $repoRoot 'PdfNative/InkOutlineCore.cpp') /link /WINMD:NO runtimeobject.lib d3d11.lib d2d1.lib ole32.lib
if ($LASTEXITCODE) { throw 'Native ink test compilation failed.' }
$runs = @()
$baselinePath = Join-Path $repoRoot 'artifacts/ink-winrt/legacy-before-port.json'
$baselineHash = if (Test-Path -LiteralPath $baselinePath) { (Get-FileHash -LiteralPath $baselinePath -Algorithm SHA256).Hash } else { $null }
foreach ($iteration in 1..$RepeatCount) {
    $output = @(& (Join-Path $outputRoot 'NativeInkSmoke.exe') (Join-Path $outputRoot 'ink-outlines.json'))
    $runExitCode = $LASTEXITCODE
    if ($runExitCode) { $output | Write-Output; throw "Native ink test failed at iteration $iteration (exit $runExitCode)" }
    $outlineHash = (Get-FileHash -LiteralPath (Join-Path $outputRoot 'ink-outlines.json') -Algorithm SHA256).Hash
    if ($baselineHash -and $outlineHash -ne $baselineHash) { throw 'Legacy shared-core outlines differ from the pre-port baseline' }
    $runs += [pscustomobject]@{iteration=$iteration;exit_code=$runExitCode;outline_sha256=$outlineHash}
    if ($RepeatCount -gt 1 -and ($iteration % 10 -eq 0 -or $iteration -eq $RepeatCount)) { Write-Output "PASS: $iteration/$RepeatCount legacy ink runs" }
}
$output | Write-Output
$hashes = [ordered]@{}
foreach ($relative in @('PdfNative/InkOutlineCore.h','PdfNative/InkOutlineCore.cpp','PdfNative/InkOutlineExporter.h',
    'PdfNative/InkOutlineExporter.cpp','tests/NativeInkSmoke.cpp','scripts/Test-NativeInk.ps1')) {
    $hashes[$relative] = (Get-FileHash -LiteralPath (Join-Path $repoRoot $relative) -Algorithm SHA256).Hash
}
[pscustomobject]@{completed_utc=[DateTime]::UtcNow.ToString('o');source_sha256=$hashes
    executable_sha256=(Get-FileHash -LiteralPath (Join-Path $outputRoot 'NativeInkSmoke.exe') -Algorithm SHA256).Hash
    runs=$runs;baseline_sha256=$baselineHash;scope='SDK 14393 legacy production ink bridge/core: pressure, constant, transformed rectangular outlines and 16 concurrent requests during Close; excludes DLL activation, live pen/XAML and total process memory'} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'legacy-ink-result.json') -Encoding UTF8
