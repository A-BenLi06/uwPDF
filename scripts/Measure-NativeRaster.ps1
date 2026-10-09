param(
    [string]$PdfPath = 'artifacts/native-tests/performance-600.pdf',
    [int]$Samples = 32
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$vsRoot = 'C:/Program Files (x86)/Microsoft Visual Studio 14.0/VC'
$sdkRoot = 'C:/Program Files (x86)/Windows Kits/10'
$outputRoot = Join-Path $repoRoot 'artifacts/native-tests'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$env:INCLUDE = "$vsRoot/include;$sdkRoot/Include/10.0.14393.0/ucrt;$sdkRoot/Include/10.0.14393.0/shared;$sdkRoot/Include/10.0.14393.0/um;$sdkRoot/Include/10.0.14393.0/winrt"
$env:LIB = "$vsRoot/lib/amd64;$sdkRoot/Lib/10.0.14393.0/ucrt/x64;$sdkRoot/Lib/10.0.14393.0/um/x64"
$env:LIBPATH = "$vsRoot/vcpackages;$sdkRoot/UnionMetadata"
$executable = Join-Path $outputRoot 'NativeRasterBenchmark.exe'
& (Join-Path $vsRoot 'bin/amd64/cl.exe') /nologo /ZW /EHsc /MD /O2 "/AI$vsRoot/vcpackages" "/AI$sdkRoot/UnionMetadata" "/Fo$outputRoot/" "/Fe$executable" (Join-Path $repoRoot 'tests/NativeRasterBenchmark.cpp') (Join-Path $repoRoot 'PdfNative/PdfRenderDevice.cpp') /link /WINMD:NO runtimeobject.lib windows.data.pdf.lib d3d11.lib d2d1.lib psapi.lib
if ($LASTEXITCODE) { throw 'Native raster benchmark compilation failed.' }
$inputPath = (Resolve-Path -LiteralPath $PdfPath).Path
$inputHash = (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash
$renderSources = @{}
foreach ($relativeSource in @('PdfNative/PdfRenderDevice.h', 'PdfNative/PdfRenderDevice.cpp', 'tests/NativeRasterBenchmark.cpp', 'scripts/Measure-NativeRaster.ps1')) {
    $renderSources[$relativeSource] = (Get-FileHash -LiteralPath (Join-Path $repoRoot $relativeSource) -Algorithm SHA256).Hash
}
foreach ($width in @(768, 1600)) {
    $reportPath = Join-Path $outputRoot "raster-benchmark-$width.json"
    & $executable $inputPath $reportPath $width $Samples
    if ($LASTEXITCODE) { throw "Native raster benchmark failed: $LASTEXITCODE" }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    $report | Add-Member -NotePropertyName input_sha256 -NotePropertyValue $inputHash
    $report | Add-Member -NotePropertyName input_path -NotePropertyValue $inputPath
    $report | Add-Member -NotePropertyName source_sha256 -NotePropertyValue $renderSources
    $report | Add-Member -NotePropertyName sdk_version -NotePropertyValue '10.0.14393.0'
    $report | Add-Member -NotePropertyName completed_utc -NotePropertyValue ([DateTime]::UtcNow.ToString('o'))
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    $renderTimes = @($report.samples.gpu_complete_ms | Sort-Object)
    $pageTimes = @($report.samples.get_page_ms | Sort-Object)
    $p95 = [Math]::Max(0, [Math]::Ceiling($renderTimes.Count * 0.95) - 1)
    [PSCustomObject]@{
        Width = $width
        GetPageP95Ms = [Math]::Round($pageTimes[$p95], 2)
        RenderGpuP95Ms = [Math]::Round($renderTimes[$p95], 2)
        PeakWorkingSetMiB = [Math]::Round($report.peak_working_set_bytes / 1MB, 1)
        Report = $reportPath
    } | Format-List
}
