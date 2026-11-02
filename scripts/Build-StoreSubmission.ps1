param([ValidateSet('x86','x64','ARM','ARM64')][string[]]$Platforms=@('x86','x64','ARM','ARM64'))
$ErrorActionPreference='Stop'
$storeRepo=Split-Path $PSScriptRoot -Parent
if ((Get-Content (Join-Path $storeRepo 'LitePdfViewer/LitePdfViewer.Shared.projitems') -Raw) -match 'tests\\.*Qa.cs') { throw 'Remove temporary QA sources before packaging.' }
& python (Join-Path $storeRepo 'tests/ValidateLocalization.py')
if ($LASTEXITCODE) { throw 'Localization validation failed.' }
& python (Join-Path $storeRepo 'tests/ValidateStoreSubmission.py')
if ($LASTEXITCODE) { throw 'Store metadata validation failed.' }
[xml]$storeManifest=Get-Content (Join-Path $storeRepo 'LitePdfViewer/Package.appxmanifest')
$storeVersion=$storeManifest.Package.Identity.Version
$storeOutput=Join-Path $storeRepo "artifacts/store-submission/$storeVersion"
New-Item $storeOutput -ItemType Directory -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$storeResults=@()
foreach ($storePlatform in $Platforms) {
    $storePlatformOutput=Join-Path $storeOutput $storePlatform
    New-Item $storePlatformOutput -ItemType Directory -Force | Out-Null
    $storeLog=Join-Path $storeOutput "$storePlatform-build.log"
    Write-Output "Building Store upload: $storePlatform $storeVersion"
    if ($storePlatform -eq 'ARM64') {
        & (Join-Path $PSScriptRoot 'Build-UwpArm64.ps1') -Configuration Release -SkipRestore -StoreUpload -PackageOutputDirectory $storePlatformOutput *> $storeLog
    } else {
        & 'C:/Program Files (x86)/MSBuild/14.0/Bin/MSBuild.exe' (Join-Path $storeRepo 'LitePdfViewer/LitePdfViewer.csproj') /p:Configuration=Release "/p:Platform=$storePlatform" /p:BuildProjectReferences=false /p:UseStoreIdentity=true /p:UapAppxPackageBuildMode=StoreUpload /p:AppxPackageSigningEnabled=false "/p:AppxPackageDir=$storePlatformOutput\" /nologo /v:minimal /nr:false *> $storeLog
    }
    if ($LASTEXITCODE) { Get-Content $storeLog -Tail 15; throw "Store build failed: $storePlatform" }
    $storePackages=@(Get-ChildItem -LiteralPath $storePlatformOutput -Recurse -File | Where-Object { $_.Name -like 'LitePdfViewer*' -and $_.Extension -in @('.appx','.msix') })
    $storeUploads=@(Get-ChildItem -LiteralPath $storePlatformOutput -File | Where-Object { $_.Extension -in @('.appxupload','.msixupload') })
    if ($storePackages.Count -ne 1 -or $storeUploads.Count -ne 1) { throw "Expected one application package and one upload for $storePlatform." }
    $storePackage=$storePackages[0]
    $storeUpload=$storeUploads[0]
    # Separate processes avoid duplicate Add-Type definitions in package checks.
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-StorePackage.ps1') -PackagePath $storePackage.FullName
    if ($LASTEXITCODE) { throw 'Store identity/capability validation failed.' }
    if ($storePlatform -eq 'ARM64') {
        & (Join-Path $PSScriptRoot 'Test-PackageBaseline.ps1') -PackagePath $storePackage.FullName -MinimumVersion '10.0.17763.0' -TargetVersion '10.0.26100.0'
        & (Join-Path $PSScriptRoot 'Test-UwpArm64Package.ps1') -PackagePath $storePackage.FullName -Configuration Release
    } else {
        & (Join-Path $PSScriptRoot 'Test-PackageBaseline.ps1') -PackagePath $storePackage.FullName
        & (Join-Path $PSScriptRoot 'Test-CppWinRTPackage.ps1') -PackagePath $storePackage.FullName -Platform $storePlatform -Configuration Release
    }
    $storeArchive=[IO.Compression.ZipFile]::OpenRead($storeUpload.FullName)
    try {
        if (@($storeArchive.Entries | Where-Object { $_.FullName -match '\.(pfx|key)$' }).Count) { throw 'Private signing material in upload.' }
        $storeEntries=@($storeArchive.Entries | Where-Object { $_.FullName -match '\.(appx|msix)$' })
        if ($storeEntries.Count -ne 1) { throw 'Upload does not contain exactly one application package.' }
        $storeHash=[Security.Cryptography.SHA256]::Create()
        $storeStream=$storeEntries[0].Open()
        try { $storeInnerHash=[BitConverter]::ToString($storeHash.ComputeHash($storeStream)).Replace('-','') }
        finally { $storeStream.Dispose(); $storeHash.Dispose() }
        # StoreUpload includes an intermediate package for Store-side .NET
        # Native compilation; the local _Test package is already native. Verify
        # the upload payload independently rather than assuming equal bytes.
        $storePayloadDirectory=Join-Path $storeOutput "upload-payloads/$storePlatform"
        New-Item $storePayloadDirectory -ItemType Directory -Force | Out-Null
        $storePayloadPath=Join-Path $storePayloadDirectory ([IO.Path]::GetFileName($storeEntries[0].FullName))
        [IO.Compression.ZipFileExtensions]::ExtractToFile($storeEntries[0],$storePayloadPath,$true)
        if ($storeInnerHash -ne (Get-FileHash -LiteralPath $storePayloadPath -Algorithm SHA256).Hash) { throw 'Extracted payload hash differs from the upload entry.' }
    } finally { $storeArchive.Dispose() }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-StorePackage.ps1') -PackagePath $storePayloadPath
    if ($LASTEXITCODE) { throw 'Upload payload identity/capability validation failed.' }
    & (Join-Path $PSScriptRoot 'Test-CppWinRTPackage.ps1') -PackagePath $storePayloadPath -Platform $storePlatform -Configuration Release
    $storeResults += [pscustomobject]@{architecture=$storePlatform;package=$storePackage.FullName;package_sha256=(Get-FileHash -LiteralPath $storePackage.FullName -Algorithm SHA256).Hash;upload_payload=$storePayloadPath;upload_payload_sha256=$storeInnerHash;upload=$storeUpload.FullName;upload_sha256=(Get-FileHash -LiteralPath $storeUpload.FullName -Algorithm SHA256).Hash}
    Write-Output "PASS: $storePlatform native package and Store upload payload independently verified."
}
$storeSourceFiles=@(Get-ChildItem (Join-Path $storeRepo 'LitePdfViewer') -Recurse -File | Where-Object { $_.FullName -notmatch '\\(bin|obj|AppPackages)\\' -and $_.Extension -in @('.cs','.xaml','.resw','.appxmanifest','.csproj','.projitems') })
$storeSourceHashes=[ordered]@{}
foreach ($storeSource in ($storeSourceFiles | Sort-Object FullName)) { $storeSourceHashes[$storeSource.FullName.Substring($storeRepo.Length+1)]=(Get-FileHash -LiteralPath $storeSource.FullName -Algorithm SHA256).Hash }
$storeReady=Join-Path $storeOutput 'PartnerCenter'
New-Item $storeReady -ItemType Directory -Force | Out-Null
$storeChecksumLines=@()
foreach ($storeResult in $storeResults) {
    $storeDestination=Join-Path $storeReady ([IO.Path]::GetFileName($storeResult.upload))
    Copy-Item -LiteralPath $storeResult.upload -Destination $storeDestination -Force
    if ((Get-FileHash -LiteralPath $storeDestination -Algorithm SHA256).Hash -ne $storeResult.upload_sha256) { throw 'Final upload copy hash mismatch.' }
    $storeChecksumLines += "$($storeResult.upload_sha256)  $([IO.Path]::GetFileName($storeDestination))"
}
$storeChecksumLines | Set-Content -LiteralPath (Join-Path $storeReady 'SHA256SUMS.txt') -Encoding ASCII
[pscustomobject]@{version=$storeVersion;completed_utc=[DateTime]::UtcNow.ToString('o');source_commit=(& git -C $storeRepo rev-parse HEAD);source_is_working_tree=$true;source_sha256=$storeSourceHashes;packages=$storeResults;scope='Unsigned Store identity packages and Store-only upload containers. Native packages: architecture/native payload and baseline. Both native and upload payloads: identity, version, languages, no capabilities, exact custom native component and separate SHA-256 hashes verified. Not GitHub installers; excludes old-device/ARM runtime and Store certification.'} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $storeOutput 'validation.json') -Encoding UTF8
Write-Output "Store submission artifacts: $storeOutput (local Partner Center uploads only)."
Write-Output "Select the four upload containers in: $storeReady"
