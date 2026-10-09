param([string]$VsInstallPath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$instances = @((& $vswhere -all -products '*' -format json) -join "`n" | ConvertFrom-Json)
$instance = $instances | Where-Object {
    (!$VsInstallPath -or $_.installationPath -eq $VsInstallPath) -and
    (Test-Path -LiteralPath (Join-Path $_.installationPath 'MSBuild/Microsoft/WindowsXaml/v18.0'))
} | Select-Object -First 1
if (!$instance) { throw 'An installed VS 2026 instance with UWP XAML build tools is required.' }
$catalogPath = Join-Path $env:ProgramData "Microsoft/VisualStudio/Packages/_Instances/$($instance.instanceId)/catalog.json"
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
$ids = @('Microsoft.VisualStudio.VC.MSBuild.v180.ARM64', 'Microsoft.VisualStudio.VC.MSBuild.v180.ARM64.v145', 'Microsoft.VisualStudio.VC.MSBuild.v180.Arm64.UWP')
$packages = foreach ($id in $ids) {
    $matches = @($catalog.packages | Where-Object { $_.id -eq $id })
    if ($matches.Count -ne 1 -or $matches[0].type -ne 'Vsix') { throw "Expected one VSIX in the installed catalog: $id" }
    $matches[0]
}
$outputRoot = Join-Path $repoRoot "artifacts/arm64-tools/MSBuild/$($instance.installationVersion)"
$vcTargets = Join-Path $outputRoot 'Microsoft/VC/v180'
if (!(Test-Path -LiteralPath $vcTargets)) {
    New-Item -ItemType Directory -Path (Split-Path $vcTargets -Parent) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $instance.installationPath 'MSBuild/Microsoft/VC/v180') -Destination (Split-Path $vcTargets -Parent) -Recurse
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$client = New-Object Net.WebClient
try {
    foreach ($package in $packages) {
        foreach ($payload in $package.payloads) {
            $uri = [uri]$payload.url
            if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'download.visualstudio.microsoft.com') { throw 'Expected an official Visual Studio payload URL.' }
            $archivePath = Join-Path $outputRoot ($package.id + '.vsix')
            if (!(Test-Path -LiteralPath $archivePath)) { $client.DownloadFile($uri, $archivePath) }
            if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $payload.sha256) { throw "Payload differs from catalog: $archivePath" }
            $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
            try {
                $prefix = 'Contents/MSBuild/Microsoft/VC/v180/'
                $entries = @($archive.Entries | Where-Object { $_.FullName.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -and $_.Name })
                if (!$entries.Count) { throw "Payload has no VS 2026 native targets: $($package.id)" }
                foreach ($entry in $entries) {
                    $relativePath = [uri]::UnescapeDataString($entry.FullName.Substring($prefix.Length))
                    $destination = [IO.Path]::GetFullPath((Join-Path $vcTargets $relativePath))
                    if (!$destination.StartsWith($vcTargets + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Payload escapes the local targets directory.' }
                    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
                    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $true)
                }
            } finally { $archive.Dispose() }
            Write-Output "Verified and extracted $($package.id)"
        }
    }
} finally { $client.Dispose() }
[pscustomobject]@{
    completed_utc = [DateTime]::UtcNow.ToString('o')
    instance_id = $instance.instanceId
    vs_install_path = $instance.installationPath
    msbuild_path = Join-Path $instance.installationPath 'MSBuild/Current/Bin/MSBuild.exe'
    vc_targets_path = $vcTargets
    catalog_sha256 = (Get-FileHash -LiteralPath $catalogPath -Algorithm SHA256).Hash
    packages = @($packages | Select-Object id, version, payloads)
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $repoRoot 'artifacts/arm64-tools/uwp-targets.json')
Write-Output "Prepared local ARM64 UWP build targets: $vcTargets"
