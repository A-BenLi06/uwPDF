param(
    [ValidateSet('x86','x64','ARM','ARM64')][string]$Platform='x64',
    [ValidateSet('Debug','Release')][string]$Configuration='Release',
    [switch]$SkipBuild, [switch]$CompileOnly,
    [string]$PdfPath='artifacts/native-tests/known-text.pdf', [uint32]$PageIndex=0, [string]$ExpectedText='uwPDF'
)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
if (!$SkipBuild) { & (Join-Path $PSScriptRoot 'Build-CppWinRT.ps1') -Platform $Platform -Configuration $Configuration }
& (Join-Path $PSScriptRoot 'Test-CppWinRTBinary.ps1') -Platform $Platform -Configuration $Configuration
if (!$CompileOnly -and $Platform -in @('ARM','ARM64') -and $env:PROCESSOR_ARCHITECTURE -ne $Platform -and $env:PROCESSOR_ARCHITEW6432 -ne $Platform) { throw "Use -CompileOnly; $Platform execution requires a matching device" }
$outputRoot=Join-Path $repoRoot "artifacts/cppwinrt/$Platform/$Configuration"
$build=Get-Content -LiteralPath (Join-Path $outputRoot 'build-result.json') -Raw | ConvertFrom-Json
$vc=$build.toolchain_root; $sdkRoot=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
$native=Join-Path $sdkRoot "Include/$($build.sdk_version)"; $support=Join-Path $sdkRoot "Include/$($build.ucrt_wrl_header_sdk_version)"
$projection=Join-Path $sdkRoot "Include/$($build.projection_sdk_version)"
$savedInclude=$env:INCLUDE; $savedLib=$env:LIB
try {
    $env:INCLUDE="$vc/include;$support/ucrt;$native/shared;$native/um;$support/winrt;$projection/cppwinrt"
    $env:LIB="$vc/lib/onecore/$Platform;$sdkRoot/Lib/$($build.sdk_version)/ucrt/$Platform;$sdkRoot/Lib/$($build.sdk_version)/um/$Platform"
    $objects=Join-Path $outputRoot 'DocumentSmoke'; New-Item -ItemType Directory -Path $objects -Force | Out-Null
    $executable=Join-Path $outputRoot 'CppWinRTDocumentSmoke.exe'
    & "$vc/bin/Hostx64/$Platform/cl.exe" /nologo /std:c++20 /EHsc /MT /O2 /DNOMINMAX "/I$outputRoot/Generated" "/Fo$objects/" "/Fe$executable" (Join-Path $repoRoot 'tests/CppWinRTDocumentSmoke.cpp') /link runtimeobject.lib ole32.lib oleaut32.lib
    if ($LASTEXITCODE) { throw 'Document smoke compilation failed' }
    $output=@()
    if (!$CompileOnly) {
        $output=@(& $executable (Join-Path $outputRoot 'PdfNative.Rendering.dll') (Resolve-Path -LiteralPath (Join-Path $repoRoot $PdfPath)).Path $PageIndex $ExpectedText)
        $code=$LASTEXITCODE; $output | Write-Output; if ($code) { throw "Actual document DLL check failed (exit $code)" }
    }
    [pscustomobject]@{completed_utc=[DateTime]::UtcNow.ToString('o');platform=$Platform;configuration=$Configuration
        component_sha256=$build.component_sha256;executable_sha256=(Get-FileHash -LiteralPath $executable).Hash
        fixture_sha256=(Get-FileHash -LiteralPath (Join-Path $repoRoot 'tests/CppWinRTDocumentSmoke.cpp')).Hash
        input_sha256=(Get-FileHash -LiteralPath (Join-Path $repoRoot $PdfPath)).Hash;page_index=$PageIndex;expected_text=$ExpectedText
        script_sha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash;runtime_verified=!$CompileOnly;output=$output
        scope=if ($CompileOnly) {'Document smoke cross compilation only; no device execution'} else {'Actual document/DTO/writer DLL activation, input clone and caller close, UTF-16 geometry, queued document close, argument/closed errors and unload; export modes are exercised separately by PDF verifiers; excludes XAML and total process memory'} } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'document-smoke-result.json') -Encoding UTF8
}
finally { $env:INCLUDE=$savedInclude; $env:LIB=$savedLib }
