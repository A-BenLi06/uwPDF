param(
    [ValidateSet('x86', 'x64', 'ARM', 'ARM64')][string]$Platform = 'ARM64',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $repoRoot "artifacts/cppwinrt/$Platform/$Configuration"
$report = Get-Content -LiteralPath (Join-Path $outputRoot 'build-result.json') -Raw | ConvertFrom-Json
if ($report.platform -ne $Platform -or $report.configuration -ne $Configuration) { throw 'Build record configuration differs.' }
foreach ($source in $report.source_sha256.PSObject.Properties) {
    if ((Get-FileHash -LiteralPath (Join-Path $repoRoot $source.Name) -Algorithm SHA256).Hash -ne $source.Value) { throw "Source differs from build record: $($source.Name)" }
}
$engineRoot = Join-Path $outputRoot 'DocumentEngine'
$engine = Get-Content -LiteralPath (Join-Path $engineRoot 'build-result.json') -Raw | ConvertFrom-Json
if ($engine.platform -ne $Platform -or $engine.configuration -ne $Configuration -or $engine.sdk_version -ne $report.sdk_version) { throw 'Document engine build configuration differs.' }
foreach ($source in $engine.source_sha256.PSObject.Properties) {
    if ((Get-FileHash -LiteralPath (Join-Path $repoRoot $source.Name) -Algorithm SHA256).Hash -ne $source.Value) { throw "Document engine source differs from build record: $($source.Name)" }
}
if ((Get-FileHash -LiteralPath (Join-Path $engineRoot 'MuPdfCore.lib') -Algorithm SHA256).Hash -ne $engine.library_sha256) { throw 'The document engine archive differs from its build record.' }
$dllPath = Join-Path $outputRoot 'PdfNative.Rendering.dll'
$winmdPath = Join-Path $outputRoot 'PdfNative.Rendering.winmd'
if ((Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash -ne $report.component_sha256 -or
    (Get-FileHash -LiteralPath $winmdPath -Algorithm SHA256).Hash -ne $report.metadata_sha256) { throw 'Built outputs differ from build record.' }
$bytes = [IO.File]::ReadAllBytes($dllPath)
if ($bytes.Length -lt 128 -or $bytes[0] -ne 0x4d -or $bytes[1] -ne 0x5a) { throw 'Expected a PE image.' }
$peOffset = [BitConverter]::ToInt32($bytes, 60)
if ($peOffset -lt 64 -or $peOffset + 96 -gt $bytes.Length -or [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x4550) { throw 'Invalid PE header.' }
$expectedMachine = @{ x86 = 0x14c; x64 = 0x8664; ARM = 0x1c4; ARM64 = 0xaa64 }[$Platform]
if ([BitConverter]::ToUInt16($bytes, $peOffset + 4) -ne $expectedMachine) { throw "DLL is not $Platform." }
if (([BitConverter]::ToUInt16($bytes, $peOffset + 22) -band 0x2000) -eq 0) { throw 'Expected a DLL image.' }
$characteristics = [BitConverter]::ToUInt16($bytes, $peOffset + 24 + 70)
if (($characteristics -band 0x1140) -ne 0x1140) { throw 'Expected AppContainer, ASLR and NX characteristics.' }
$dumpbin = Join-Path $report.toolchain_root "bin/Hostx64/$Platform/dumpbin.exe"
$dump = @(& $dumpbin /nologo /exports /dependents $dllPath)
if ($LASTEXITCODE) { throw 'DLL inspection failed.' }
$dumpText = $dump -join "`n"
foreach ($export in @('DllGetActivationFactory', 'DllCanUnloadNow')) {
    if ($dumpText -notmatch "(?m)^\s+\d+\s+[0-9A-F]+\s+[0-9A-F]+\s+$export(?:\s+=\s+\S+)?\s*$") { throw "Missing WinRT export: $export" }
}
if ($dumpText -match '(?im)^\s*(vcruntime\w*|msvcp\w*|ucrtbase[d]?)\.dll\s*$') { throw 'The renderer unexpectedly depends on a dynamic CRT.' }
$dump | Set-Content -LiteralPath (Join-Path $outputRoot 'binary-inspection.txt')
[pscustomobject]@{
    completed_utc = [DateTime]::UtcNow.ToString('o')
    platform = $Platform; configuration = $Configuration
    component_sha256 = $report.component_sha256
    metadata_sha256 = $report.metadata_sha256
    scope = 'PE architecture, AppContainer/ASLR/NX, WinRT exports, static CRT and source/output hashes; excludes activation, rendering, packaging and device runtime'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'binary-result.json')
Write-Output "PASS: $Platform/$Configuration binary architecture, WinRT exports, static CRT and build hashes."
