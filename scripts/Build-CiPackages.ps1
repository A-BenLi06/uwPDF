$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$lockPath = Join-Path $repoRoot 'LitePdfViewer/project.lock.json'
$originalLock = [IO.File]::ReadAllBytes($lockPath)
Push-Location $repoRoot
try {
    if ((& git status --porcelain).Count) { throw 'CI packaging requires a clean checkout, including untracked files.' }
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE) { throw 'Cannot resolve source commit.' }
    $output = Join-Path $repoRoot 'artifacts/ci/unsigned'
    if (Test-Path -LiteralPath $output) { throw 'Use a clean checkout; unsigned output already exists.' }
    $msbuild14 = 'C:/Program Files (x86)/MSBuild/14.0/Bin/MSBuild.exe'
    if (!(Test-Path -LiteralPath $msbuild14)) { throw 'Install VS 2015 UWP tooling (MSBuild 14), SDK 14393 and .NET Native 1.x. See docs/ci-cd.md.' }
    if ((Get-Content 'LitePdfViewer/LitePdfViewer.Shared.projitems' -Raw) -match 'tests\\.*Qa.cs') { throw 'Temporary QA sources must not be packaged.' }
    & python tests/ValidateLocalization.py
    if ($LASTEXITCODE) { throw 'Localization validation failed.' }
    & ./.tools/nuget-3.5.0.exe restore LitePdfViewer.sln -ConfigFile NuGet.Config -NonInteractive
    if ($LASTEXITCODE) { throw 'Legacy UWP NuGet restore failed.' }
    $expectedPackages = (([Text.Encoding]::UTF8.GetString($originalLock) | ConvertFrom-Json).libraries.PSObject.Properties.Name | Sort-Object)
    $resolvedPackages = ((Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json).libraries.PSObject.Properties.Name | Sort-Object)
    if (Compare-Object $expectedPackages $resolvedPackages) { throw 'NuGet restore changed the committed dependency versions.' }
    $restoredLockHash = (Get-FileHash -LiteralPath $lockPath -Algorithm SHA256).Hash
    & ./scripts/Prepare-NativeDependencies.ps1
    & ./scripts/Prepare-CppWinRTArm32.ps1
    & ./scripts/Prepare-CppWinRTArm64.ps1
    & ./scripts/Prepare-UwpArm64Targets.ps1
    $tools = Get-Content artifacts/arm64-tools/toolchain.json -Raw | ConvertFrom-Json
    $targets = Get-Content artifacts/arm64-tools/uwp-targets.json -Raw | ConvertFrom-Json
    [xml]$manifest = Get-Content LitePdfViewer/Package.appxmanifest
    $version = [string]$manifest.Package.Identity.Version
    [xml]$armManifest = Get-Content LitePdfViewer/Package.ARM64.appxmanifest
    if ($armManifest.Package.Identity.Version -ne $version) { throw 'Application manifest versions differ.' }
    foreach ($platform in @('x86', 'x64', 'ARM', 'ARM64')) {
        $packageOutput = Join-Path $repoRoot "artifacts/ci/build/$platform"
        $arguments = @('/nologo', '/m:2', '/nr:false', '/v:minimal', '/p:Configuration=Release',
            "/p:Platform=$platform", '/p:UseStoreIdentity=false', '/p:AppxPackageSigningEnabled=false',
            '/p:UapAppxPackageBuildMode=SideloadOnly', '/p:AppxBundle=Never', "/p:AppxPackageDir=$packageOutput\")
        if ($platform -eq 'ARM64') {
            & $targets.msbuild_path LitePdfViewer.ARM64.sln /restore @arguments '/p:CL_MPCount=4' '/p:PreferredToolArchitecture=x64' `
                "/p:VCToolsInstallDir=$($tools.toolchain_root)\" "/p:VCTargetsPath=$($targets.vc_targets_path)\"
        } else {
            & $msbuild14 LitePdfViewer/LitePdfViewer.csproj @arguments
        }
        if ($LASTEXITCODE) { throw "$platform Release build failed." }
        $packages = @(Get-ChildItem -LiteralPath $packageOutput -Recurse -File | Where-Object {
            $_.BaseName -like 'LitePdfViewer*' -and $_.Extension -in @('.appx', '.msix')
        })
        if ($packages.Count -ne 1) { throw "Expected exactly one application package for $platform." }
        $package = $packages[0]
        if ($platform -eq 'ARM64') {
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-UwpArm64Package.ps1 -PackagePath $package.FullName
        } else {
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-PackageBaseline.ps1 -PackagePath $package.FullName
            if ($LASTEXITCODE) { throw "$platform OS baseline validation failed." }
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-CppWinRTPackage.ps1 -PackagePath $package.FullName -Platform $platform
        }
        if ($LASTEXITCODE) { throw "$platform package validation failed." }
        $stage = Join-Path $output $platform
        New-Item -ItemType Directory -Path $stage -Force | Out-Null
        Copy-Item -LiteralPath $package.FullName -Destination (Join-Path $stage "uwPDF_$($version)_$platform$($package.Extension)")
        $dependencyRoot = Join-Path $package.DirectoryName "Dependencies/$platform"
        $dependencies = @(Get-ChildItem -LiteralPath $dependencyRoot -File | Where-Object { $_.Extension -in @('.appx', '.msix') })
        if (!$dependencies.Count) { throw "Missing $platform installer dependencies." }
        $stageDependencies = Join-Path $stage 'Dependencies'
        New-Item -ItemType Directory -Path $stageDependencies -Force | Out-Null
        foreach ($dependency in $dependencies) { Copy-Item -LiteralPath $dependency.FullName -Destination $stageDependencies }
    }
    # NuGet 3.5 upgrades this generated asset to schema 2 with a machine-specific
    # package cache path. Preserve its exact resolved version set without committing
    # that path or treating generated schema changes as application source changes.
    [IO.File]::WriteAllBytes($lockPath, $originalLock)
    if ((& git status --porcelain).Count) { throw 'Build modified the source checkout.' }
    $hashes = @{}
    foreach ($file in Get-ChildItem -LiteralPath $output -Recurse -File) {
        $hashes[$file.FullName.Substring($output.Length + 1).Replace('\', '/')] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    [ordered]@{ commit = $commit; version = $version; configuration = 'Release';
        platforms = @('x86', 'x64', 'ARM', 'ARM64'); files = $hashes;
        restored_lock_sha256 = $restoredLockHash; resolved_packages = $resolvedPackages;
        toolchain = 'VS 2015 UWP/MSBuild 14 + VS 2026 UWP; SDK 14393/26100; repository-pinned ARM toolchains';
        validation = 'Localization, package baselines, native architecture, source/DLL hashes, six WinRT registrations; no device runtime validation' } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'build-info.json') -Encoding UTF8
} finally {
    [IO.File]::WriteAllBytes($lockPath, $originalLock)
    Pop-Location
}
