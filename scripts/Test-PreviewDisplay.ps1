$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$vsRoot = 'C:/Program Files (x86)/Microsoft Visual Studio 14.0/VC'
$sdkRoot = 'C:/Program Files (x86)/Windows Kits/10'
$outputRoot = Join-Path $repoRoot 'artifacts/native-tests/preview-display'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$savedInclude = $env:INCLUDE; $savedLib = $env:LIB
try {
    $env:INCLUDE = "$vsRoot/include;$sdkRoot/Include/10.0.14393.0/ucrt;$sdkRoot/Include/10.0.14393.0/shared;$sdkRoot/Include/10.0.14393.0/um;$sdkRoot/Include/10.0.14393.0/winrt"
    $env:LIB = "$vsRoot/lib/amd64;$sdkRoot/Lib/10.0.14393.0/ucrt/x64;$sdkRoot/Lib/10.0.14393.0/um/x64"
    $executable = Join-Path $outputRoot 'PreviewDisplayCoreTests.exe'
    & (Join-Path $vsRoot 'bin/amd64/cl.exe') /nologo /EHsc /MD /O2 /DNOMINMAX "/Fo$outputRoot/" "/Fe$executable" (Join-Path $repoRoot 'tests/PreviewDisplayCoreTests.cpp')
    if ($LASTEXITCODE) { throw 'Display-region core test compilation failed.' }
    & $executable
    if ($LASTEXITCODE) { throw 'Production display-region core checks failed.' }
    $hashes = [ordered]@{}
    foreach ($relative in @('PdfNative/PreviewDisplayCore.h', 'tests/PreviewDisplayCoreTests.cpp', 'scripts/Test-PreviewDisplay.ps1')) {
        $hashes[$relative] = (Get-FileHash -LiteralPath (Join-Path $repoRoot $relative) -Algorithm SHA256).Hash
    }
    [pscustomobject]@{ completed_utc = [DateTime]::UtcNow.ToString('o'); source_sha256 = $hashes
        scope = 'Production shared display-region core with COM/API doubles on SDK 14393; excludes actual ApplicationView, monitor selection, DPI/taskbar and XAML runtime' } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'result.json') -Encoding UTF8
}
finally { $env:INCLUDE = $savedInclude; $env:LIB = $savedLib }
