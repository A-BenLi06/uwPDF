param(
    [ValidateSet('x86', 'x64', 'ARM', 'ARM64')][string]$Platform = 'x64',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$SkipBuild,
    [switch]$CompileOnly,
    [ValidateRange(1, 1000)][int]$RepeatCount = 1,
    [string]$BaselinePath = 'artifacts/ink-winrt/legacy-before-port.json'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (!$SkipBuild) { & (Join-Path $PSScriptRoot 'Build-CppWinRT.ps1') -Platform $Platform -Configuration $Configuration }
& (Join-Path $PSScriptRoot 'Test-CppWinRTBinary.ps1') -Platform $Platform -Configuration $Configuration
if (!$CompileOnly -and (($Platform -eq 'ARM' -and $env:PROCESSOR_ARCHITECTURE -ne 'ARM' -and $env:PROCESSOR_ARCHITEW6432 -ne 'ARM') -or
    ($Platform -eq 'ARM64' -and $env:PROCESSOR_ARCHITECTURE -ne 'ARM64' -and $env:PROCESSOR_ARCHITEW6432 -ne 'ARM64'))) {
    throw "$Platform ink smoke execution requires a matching device. Use -CompileOnly on this host."
}
$outputRoot = Join-Path $repoRoot "artifacts/cppwinrt/$Platform/$Configuration"
$build = Get-Content -LiteralPath (Join-Path $outputRoot 'build-result.json') -Raw | ConvertFrom-Json
$vcRoot = $build.toolchain_root
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
$include = Join-Path $sdkRoot "Include/$($build.sdk_version)"
$projection = Join-Path $sdkRoot "Include/$($build.projection_sdk_version)"
$support = Join-Path $sdkRoot "Include/$($build.ucrt_wrl_header_sdk_version)"
$savedInclude = $env:INCLUDE; $savedLib = $env:LIB
try {
    $env:INCLUDE = "$vcRoot/include;$support/ucrt;$include/shared;$include/um;$support/winrt;$projection/cppwinrt"
    $env:LIB = "$vcRoot/lib/onecore/$Platform;$sdkRoot/Lib/$($build.sdk_version)/ucrt/$Platform;$sdkRoot/Lib/$($build.sdk_version)/um/$Platform"
    $objects = Join-Path $outputRoot 'InkSmoke'
    New-Item -ItemType Directory -Path $objects -Force | Out-Null
    $executable = Join-Path $outputRoot 'CppWinRTInkSmoke.exe'
    & (Join-Path $vcRoot "bin/Hostx64/$Platform/cl.exe") /nologo /std:c++20 /EHsc /MT /O2 /DNOMINMAX "/I$outputRoot/Generated" "/Fo$objects/" "/Fe$executable" (Join-Path $repoRoot 'tests/CppWinRTInkSmoke.cpp') /link runtimeobject.lib ole32.lib oleaut32.lib
    if ($LASTEXITCODE) { throw 'C++/WinRT ink smoke compilation failed.' }
    $output = @(); $outlineHash = $null; $baselineHash = $null; $runs = @()
    if (!$CompileOnly) {
        $outlinePath = Join-Path $outputRoot 'ink-outlines.json'
        if (Test-Path -LiteralPath (Join-Path $repoRoot $BaselinePath)) {
            $baselineHash = (Get-FileHash -LiteralPath (Join-Path $repoRoot $BaselinePath) -Algorithm SHA256).Hash
        }
        for ($iteration = 1; $iteration -le $RepeatCount; $iteration++) {
            $timer = [Diagnostics.Stopwatch]::StartNew()
            $output = @(& $executable (Join-Path $outputRoot 'PdfNative.Rendering.dll') $outlinePath)
            $runExitCode = $LASTEXITCODE
            $timer.Stop()
            if ($runExitCode) {
                $output | Write-Output
                [pscustomobject]@{ completed_utc = [DateTime]::UtcNow.ToString('o'); failed_iteration = $iteration
                    exit_code = $runExitCode; component_sha256 = $build.component_sha256; output = $output; preceding_runs = $runs } |
                    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'ink-smoke-failure.json') -Encoding UTF8
                throw "Actual C++/WinRT ink DLL checks failed at iteration $iteration (exit $runExitCode)."
            }
            $outlineHash = (Get-FileHash -LiteralPath $outlinePath -Algorithm SHA256).Hash
            if ($baselineHash -and $baselineHash -ne $outlineHash) { throw 'Actual C++/WinRT ink outlines differ from the legacy fixture baseline.' }
            $runs += [pscustomobject]@{ iteration = $iteration; exit_code = $runExitCode; elapsed_ms = $timer.Elapsed.TotalMilliseconds; outline_sha256 = $outlineHash }
            if ($RepeatCount -gt 1 -and ($iteration % 10 -eq 0 -or $iteration -eq $RepeatCount)) {
                Write-Output "PASS: $iteration/$RepeatCount actual ink/display DLL runs."
            }
        }
        $output | Write-Output
        if ($baselineHash) {
            Write-Output 'PASS: all three actual C++/WinRT ink outlines match the legacy baseline byte-for-byte.'
        }
    }
    [pscustomobject]@{
        completed_utc = [DateTime]::UtcNow.ToString('o'); platform = $Platform; configuration = $Configuration
        component_sha256 = $build.component_sha256; executable_sha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
        fixture_sha256 = (Get-FileHash -LiteralPath (Join-Path $repoRoot 'tests/CppWinRTInkSmoke.cpp') -Algorithm SHA256).Hash
        script_sha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
        runtime_verified = !$CompileOnly; outline_sha256 = $outlineHash; baseline_sha256 = $baselineHash; output = $output
        runs = $runs
        scope = if ($CompileOnly) { 'Ink smoke cross compilation only; no device activation or rendering executed' }
            else { 'Actual DLL ink/display activation, synthesized pressure/constant/rectangular transformed strokes, snapshot, validation, concurrent close, async lifetime, null-view display fallback and unload; excludes actual monitor, live pen/XAML input, hardware latency and total app memory' }
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'ink-smoke-result.json') -Encoding UTF8
    if ($CompileOnly) { Write-Output "Built $Platform ink smoke executable; runtime remains unverified." }
}
finally { $env:INCLUDE = $savedInclude; $env:LIB = $savedLib }
