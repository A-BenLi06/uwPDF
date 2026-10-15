param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$SkipRestore
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$toolsPath = Join-Path $repoRoot 'artifacts/arm64-tools/toolchain.json'
$targetsPath = Join-Path $repoRoot 'artifacts/arm64-tools/uwp-targets.json'
if (!(Test-Path -LiteralPath $toolsPath)) { & (Join-Path $PSScriptRoot 'Prepare-CppWinRTArm64.ps1') }
if (!(Test-Path -LiteralPath $targetsPath)) { & (Join-Path $PSScriptRoot 'Prepare-UwpArm64Targets.ps1') }
$tools = Get-Content -LiteralPath $toolsPath -Raw | ConvertFrom-Json
$targets = Get-Content -LiteralPath $targetsPath -Raw | ConvertFrom-Json
foreach ($relative in @('bin/Hostx64/arm64/cl.exe', 'lib/arm64/store/vccorlib.lib', 'lib/x86/store/references/platform.winmd')) {
    if (!(Test-Path -LiteralPath (Join-Path $tools.toolchain_root $relative))) { throw "Local toolchain is incomplete. Rerun Prepare-CppWinRTArm64.ps1: $relative" }
}
$project = Join-Path $repoRoot 'LitePdfViewer.ARM64.sln'
$logPath = Join-Path $repoRoot "artifacts/arm64-tools/app-$($Configuration.ToLowerInvariant()).log"
$arguments = @($project, '/m:2', '/nologo', '/v:minimal', '/nr:false',
    "/p:Configuration=$Configuration", '/p:Platform=ARM64', '/p:CL_MPCount=4', '/p:PreferredToolArchitecture=x64',
    "/p:VCToolsInstallDir=$($tools.toolchain_root)\", "/p:VCTargetsPath=$($targets.vc_targets_path)\",
    '/p:RestoreRecursive=false', '/fl', "/flp:logfile=$logPath;verbosity=minimal")
if (!$SkipRestore) { $arguments += '/restore' }
& $targets.msbuild_path @arguments
if ($LASTEXITCODE) { throw "ARM64 UWP $Configuration build failed. See $logPath" }
Write-Output "Built ARM64 UWP $Configuration application; device runtime remains unverified."
