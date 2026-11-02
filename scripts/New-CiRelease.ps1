param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$CertificateThumbprint
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Test-ReleaseSource.ps1') -Tag $Tag
Add-Type -AssemblyName System.IO.Compression.FileSystem
$unsigned = Join-Path $repoRoot 'artifacts/ci/unsigned'
$output = Join-Path $repoRoot 'artifacts/ci/release'
$info = Get-Content (Join-Path $unsigned 'build-info.json') -Raw | ConvertFrom-Json
$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -or $info.commit -ne $commit -or "v$($info.version)" -ne $Tag) { throw 'Build provenance does not match the release source.' }
$files = @(Get-ChildItem -LiteralPath $unsigned -Recurse -File | Where-Object Name -ne 'build-info.json')
if ($files.Count -ne @($info.files.PSObject.Properties).Count) { throw 'Unexpected unsigned artifact contents.' }
foreach ($file in $files) {
    $relative = $file.FullName.Substring($unsigned.Length + 1).Replace('\', '/')
    if ($file.Extension -notin @('.appx', '.msix') -or (Get-FileHash -LiteralPath $file.FullName).Hash -ne $info.files.$relative) {
        throw "Unsigned artifact hash/type mismatch: $relative"
    }
}
if (!$CertificateThumbprint -and (!$env:UWPDF_SIGNING_PFX_BASE64 -or !$env:UWPDF_SIGNING_PFX_PASSWORD)) {
    throw 'Configure both UWPDF signing secrets, or select an existing local signing certificate by thumbprint.'
}
$sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
$signTool = Get-ChildItem -LiteralPath $sdkBin -Directory | Where-Object Name -match '^10\.0\.\d+\.0$' |
    Sort-Object { [version]$_.Name } -Descending | ForEach-Object { Join-Path $_.FullName 'x64/signtool.exe' } |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$signTool) { throw 'Windows SDK SignTool is required.' }
if (Test-Path -LiteralPath $output) { throw 'Release output already exists; start with a clean workspace.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$temporaryRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$pfxPath = Join-Path $temporaryRoot ("uwpdf-signing-$([guid]::NewGuid().ToString('N')).pfx")
$certificate = $null
$certificateImported = $false
$rootAdded = $false
try {
    if ($CertificateThumbprint) {
        if ($CertificateThumbprint -notmatch '^[a-fA-F0-9]{40}$') { throw 'Invalid signing certificate thumbprint.' }
        $certificate = Get-Item -LiteralPath "Cert:/CurrentUser/My/$CertificateThumbprint"
    } else {
        [IO.File]::WriteAllBytes($pfxPath, [Convert]::FromBase64String($env:UWPDF_SIGNING_PFX_BASE64))
        $password = ConvertTo-SecureString $env:UWPDF_SIGNING_PFX_PASSWORD -AsPlainText -Force
        # An ephemeral hosted Windows runner owns this imported private key.
        $certificate = Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation Cert:/CurrentUser/My -Password $password
        $certificateImported = $true
    }
    if (@($certificate).Count -ne 1 -or !$certificate.HasPrivateKey -or $certificate.Subject -ne 'CN=Ben' -or
        $certificate.NotAfter -le (Get-Date) -or $certificate.NotBefore -gt (Get-Date)) {
        throw 'Use one valid code-signing certificate with private key and Subject CN=Ben (the sideload publisher).'
    }
    $publicCertificate = Join-Path $output 'uwPDF.cer'
    Export-Certificate -Cert $certificate -FilePath $publicCertificate | Out-Null
    # Add temporary public-certificate trust for SignTool verification, then remove it.
    if (!(Test-Path "Cert:/CurrentUser/Root/$($certificate.Thumbprint)")) {
        Import-Certificate -FilePath $publicCertificate -CertStoreLocation Cert:/CurrentUser/Root | Out-Null
        $rootAdded = $true
    }
    $signedHashes = [ordered]@{}
    foreach ($platform in @('x86', 'x64', 'ARM', 'ARM64')) {
        $stage = Join-Path $unsigned $platform
        $packages = @(Get-ChildItem -LiteralPath $stage -File | Where-Object { $_.Extension -in @('.appx', '.msix') })
        if ($packages.Count -ne 1) { throw "Expected one $platform application package." }
        $package = $packages[0]
        $archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
        try {
            $reader = New-Object IO.StreamReader($archive.GetEntry('AppxManifest.xml').Open())
            try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $identity = $manifest.Package.Identity
            if ($identity.Name -ne 'Ben.LitePdfViewerPreview' -or $identity.Publisher -ne $certificate.Subject -or
                $identity.Version -ne $info.version -or $identity.ProcessorArchitecture -ne $platform) { throw "Invalid $platform sideload identity." }
            if (@($archive.Entries | Where-Object FullName -match '\.(pfx|key|appxupload|msixupload)$').Count) { throw 'Forbidden material in application package.' }
        } finally { $archive.Dispose() }
        & $signTool sign /fd SHA256 /sha1 $certificate.Thumbprint /s My /tr http://timestamp.digicert.com /td SHA256 $package.FullName
        if ($LASTEXITCODE) { throw "$platform package signing failed." }
        & $signTool verify /pa /all /v $package.FullName
        if ($LASTEXITCODE) { throw "$platform signature verification failed." }
        $dependencies = @(Get-ChildItem -LiteralPath (Join-Path $stage 'Dependencies') -File)
        if (!$dependencies.Count) { throw "Missing $platform dependencies." }
        foreach ($dependency in $dependencies) {
            & $signTool verify /pa /all $dependency.FullName
            if ($LASTEXITCODE) { throw "Dependency signature verification failed: $($dependency.Name)" }
        }
        Copy-Item -LiteralPath $publicCertificate -Destination (Join-Path $stage 'uwPDF.cer')
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-CiPackage.ps1') -Destination (Join-Path $stage 'Add-AppDevPackage.ps1')
        Copy-Item -LiteralPath $package.FullName -Destination $output
        $signedHashes[$package.Name] = (Get-FileHash -LiteralPath $package.FullName).Hash
        $zipPath = Join-Path $output "uwPDF_$($info.version)_$($platform)_Install.zip"
        [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath)
    }
    $info | Add-Member -NotePropertyName signing_certificate_thumbprint -NotePropertyValue $certificate.Thumbprint
    $info | Add-Member -NotePropertyName signed_package_sha256 -NotePropertyValue $signedHashes
    $info | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'build-info.json') -Encoding UTF8
    $sums = @(Get-ChildItem -LiteralPath $output -File | Sort-Object Name | ForEach-Object {
        "$( (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() )  $($_.Name)"
    })
    [IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), $sums, [Text.UTF8Encoding]::new($false))
    & (Join-Path $PSScriptRoot 'Test-ReleaseAssets.ps1') -Directory $output
    $notes = (Get-Content (Join-Path $repoRoot "docs/releases/$Tag.md") -Raw).TrimEnd()
    $notes += "`n`n## Developer information`n`n- Source commit: $commit`n- Source ref: refs/tags/$Tag (on main)`n- Toolchain: $($info.toolchain)`n- Validation: $($info.validation); 17 C# regression suites; SignTool verification of application and dependency signatures; SHA-256 checked before and after upload.`n"
    [IO.File]::WriteAllText((Join-Path $repoRoot 'artifacts/ci/release-notes.md'), $notes, [Text.UTF8Encoding]::new($false))
} finally {
    if (Test-Path -LiteralPath $pfxPath) { Remove-Item -LiteralPath $pfxPath -Force }
    if ($rootAdded) { Remove-Item -LiteralPath "Cert:/CurrentUser/Root/$($certificate.Thumbprint)" -Force }
    if ($certificateImported -and $certificate) { $certificate | ForEach-Object { Remove-Item -LiteralPath "Cert:/CurrentUser/My/$($_.Thumbprint)" -DeleteKey -Force } }
}
