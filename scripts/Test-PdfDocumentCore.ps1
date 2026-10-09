param(
    [ValidateSet('x86','x64','ARM','ARM64')][string]$Platform='x64',
    [ValidateSet('Debug','Release')][string]$Configuration='Release',
    [switch]$CompileOnly
)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Test-CppWinRTBinary.ps1') -Platform $Platform -Configuration $Configuration
if (!$CompileOnly -and $Platform -in @('ARM','ARM64') -and $env:PROCESSOR_ARCHITECTURE -ne $Platform -and $env:PROCESSOR_ARCHITEW6432 -ne $Platform) { throw "Use -CompileOnly; $Platform execution requires a matching device" }
$outputRoot=Join-Path $repoRoot "artifacts/cppwinrt/$Platform/$Configuration"
$build=Get-Content -LiteralPath (Join-Path $outputRoot 'build-result.json') -Raw | ConvertFrom-Json
$vc=$build.toolchain_root; $sdkRoot=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
$native=Join-Path $sdkRoot "Include/$($build.sdk_version)"; $support=Join-Path $sdkRoot "Include/$($build.ucrt_wrl_header_sdk_version)"
$savedInclude=$env:INCLUDE; $savedLib=$env:LIB
try {
    $env:INCLUDE="$vc/include;$support/ucrt;$native/shared;$native/um;$support/winrt;$repoRoot/artifacts/mupdf-source/include"
    $env:LIB="$vc/lib/onecore/$Platform;$sdkRoot/Lib/$($build.sdk_version)/ucrt/$Platform;$sdkRoot/Lib/$($build.sdk_version)/um/$Platform"
    $objects=Join-Path $outputRoot 'DocumentCoreTests'; New-Item -ItemType Directory -Path $objects -Force | Out-Null
    $exe=Join-Path $outputRoot 'PdfDocumentCoreTests.exe'
    $crt=if ($Configuration -eq 'Debug') { '/MTd' } else { '/MT' }
    & "$vc/bin/Hostx64/$Platform/cl.exe" /nologo /std:c++20 /EHsc $crt /O2 /DNOMINMAX "/Fo$objects/" "/Fe$exe" (Join-Path $repoRoot 'tests/PdfDocumentCoreTests.cpp') (Join-Path $repoRoot 'PdfNative/PdfDocumentCore.cpp') /link "$outputRoot/DocumentEngine/MuPdfCore.lib" bcrypt.lib
    if ($LASTEXITCODE) { throw 'Document core test compilation failed' }
    $output=@(); $fixture=Join-Path $repoRoot 'artifacts/native-tests/known-text.pdf'
    if (!$CompileOnly) { $output=@(& $exe $fixture); $code=$LASTEXITCODE; $output | Write-Output; if ($code) { throw "Document core check failed (exit $code)" } }
    [pscustomobject]@{completed_utc=[DateTime]::UtcNow.ToString('o');platform=$Platform;configuration=$Configuration
        component_sha256=$build.component_sha256;executable_sha256=(Get-FileHash -LiteralPath $exe).Hash
        test_sha256=(Get-FileHash -LiteralPath (Join-Path $repoRoot 'tests/PdfDocumentCoreTests.cpp')).Hash
        core_sha256=(Get-FileHash -LiteralPath (Join-Path $repoRoot 'PdfNative/PdfDocumentCore.cpp')).Hash
        fixture_sha256=(Get-FileHash -LiteralPath $fixture).Hash;script_sha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash
        runtime_verified=!$CompileOnly;output=$output;scope='Shared production parser/writer core and stream failure/lifetime checks; excludes projection activation, XAML and device input'} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'document-core-result.json') -Encoding UTF8
}
finally { $env:INCLUDE=$savedInclude; $env:LIB=$savedLib }
