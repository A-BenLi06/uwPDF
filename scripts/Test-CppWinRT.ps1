param(
    [ValidateSet('x86', 'x64', 'ARM', 'ARM64')][string]$Platform = 'x64',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$PdfPath = 'LitePdfViewer/TestAssets/SkimSample.pdf',
    [switch]$SkipBuild,
    [string]$SdkVersion = '10.0.26100.0',
    [switch]$CompileOnly
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if ($Platform -eq 'ARM' -and !$PSBoundParameters.ContainsKey('SdkVersion')) { $SdkVersion = '10.0.14393.0' }
if (!$SkipBuild) { & (Join-Path $PSScriptRoot 'Build-CppWinRT.ps1') -Platform $Platform -Configuration $Configuration -SdkVersion $SdkVersion }
$outputRoot = Join-Path $repoRoot "artifacts/cppwinrt/$Platform/$Configuration"
$buildRecordPath = Join-Path $outputRoot 'build-result.json'
if (Test-Path -LiteralPath $buildRecordPath) {
    $buildRecord = Get-Content -LiteralPath $buildRecordPath -Raw | ConvertFrom-Json
    $vcRoot = $buildRecord.toolchain_root
    if (!$PSBoundParameters.ContainsKey('SdkVersion')) { $SdkVersion = $buildRecord.sdk_version }
} else {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $vsRoot = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    $compilerVersion = (Get-Content (Join-Path $vsRoot 'VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt') -Raw).Trim()
    $vcRoot = Join-Path $vsRoot "VC/Tools/MSVC/$compilerVersion"
}
if ($Platform -eq 'ARM' -and !$CompileOnly -and $env:PROCESSOR_ARCHITECTURE -ne 'ARM' -and $env:PROCESSOR_ARCHITEW6432 -ne 'ARM') {
    throw 'ARM32 smoke execution requires an ARM32 device. Use -CompileOnly to produce its test executable on this host.'
}
if ($Platform -eq 'ARM64' -and !$CompileOnly -and $env:PROCESSOR_ARCHITECTURE -ne 'ARM64' -and $env:PROCESSOR_ARCHITEW6432 -ne 'ARM64') {
    throw 'ARM64 smoke execution requires an ARM64 device. Use -CompileOnly to produce its test executable on this host.'
}
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
$sdkInclude = Join-Path $sdkRoot "Include/$SdkVersion"
$projectionSdk = if ($buildRecord -and $buildRecord.projection_sdk_version) { $buildRecord.projection_sdk_version } elseif ($Platform -eq 'ARM') { '10.0.26100.0' } else { $SdkVersion }
$projectionInclude = Join-Path $sdkRoot "Include/$projectionSdk"
$savedInclude = $env:INCLUDE; $savedLib = $env:LIB
try {
    $supportInclude = if ($Platform -eq 'ARM') { $projectionInclude } else { $sdkInclude }
    $env:INCLUDE = "$vcRoot/include;$supportInclude/ucrt;$sdkInclude/shared;$sdkInclude/um;$supportInclude/winrt;$projectionInclude/cppwinrt"
    $crtPath = if ($Platform -eq 'ARM64' -or $Platform -eq 'ARM') { "$vcRoot/lib/onecore/$Platform" } else { "$vcRoot/lib/$Platform" }
    $env:LIB = "$crtPath;$sdkRoot/Lib/$SdkVersion/ucrt/$Platform;$sdkRoot/Lib/$SdkVersion/um/$Platform"
    $executable = Join-Path $outputRoot 'CppWinRTRendererSmoke.exe'
    $smokeObjects = Join-Path $outputRoot 'Smoke'
    New-Item -ItemType Directory -Path $smokeObjects -Force | Out-Null
    & (Join-Path $vcRoot "bin/Hostx64/$Platform/cl.exe") /nologo /std:c++20 /EHsc /MT /O2 /DNOMINMAX "/I$outputRoot/Generated" "/Fo$smokeObjects/" "/Fe$executable" (Join-Path $repoRoot 'tests/CppWinRTRendererSmoke.cpp') (Join-Path $repoRoot 'PdfNative/PdfRenderDevice.cpp') /link runtimeobject.lib ole32.lib oleaut32.lib windows.data.pdf.lib d3d11.lib d2d1.lib
    if ($LASTEXITCODE) { throw 'C++/WinRT smoke compilation failed.' }
    $inputPath = (Resolve-Path -LiteralPath (Join-Path $repoRoot $PdfPath)).Path
    $componentPath = Join-Path $outputRoot 'PdfNative.Rendering.dll'
    if ($CompileOnly) {
        [pscustomobject]@{
            completed_utc = [DateTime]::UtcNow.ToString('o')
            platform = $Platform; configuration = $Configuration
            component_sha256 = (Get-FileHash -LiteralPath $componentPath -Algorithm SHA256).Hash
            executable_sha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
            smoke_source_sha256 = (Get-FileHash -LiteralPath (Join-Path $repoRoot 'tests/CppWinRTRendererSmoke.cpp') -Algorithm SHA256).Hash
            input_sha256 = (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash
            runtime_verified = $false
            scope = 'Smoke executable cross compilation only; no activation or rendering executed'
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'smoke-build-result.json') -Encoding UTF8
        Write-Output "Built $Platform smoke executable; runtime checks were not executed."
        return
    }
    $smokeOutput = @(& $executable $componentPath $inputPath)
    if ($LASTEXITCODE) { throw 'C++/WinRT smoke checks failed.' }
    $smokeOutput | Write-Output
    [PSCustomObject]@{
        platform = $Platform; configuration = $Configuration
        component_sha256 = (Get-FileHash -LiteralPath $componentPath -Algorithm SHA256).Hash
        input_sha256 = (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash
        completed_utc = [DateTime]::UtcNow.ToString('o')
        scope = 'DLL activation, async device factory, argument validation, failed coroutine lifetime, unload and shared-core pixels; excludes XAML presentation and input latency'
        output = $smokeOutput
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputRoot 'smoke-result.json') -Encoding UTF8
}
finally { $env:INCLUDE = $savedInclude; $env:LIB = $savedLib }
