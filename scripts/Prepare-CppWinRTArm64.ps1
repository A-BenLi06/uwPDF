param([string]$VsInstallPath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$instances = @((& $vswhere -all -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -format json) -join "`n" | ConvertFrom-Json)
if ($VsInstallPath) {
    $instance = $instances | Where-Object { $_.installationPath -eq $VsInstallPath } | Select-Object -First 1
} else {
    $instance = $instances | Where-Object { $_.productId -eq 'Microsoft.VisualStudio.Product.BuildTools' } | Select-Object -First 1
    if (!$instance) { $instance = $instances | Select-Object -First 1 }
}
if (!$instance) { throw 'Install MSVC x86/x64 tools before preparing the local ARM64 overlay.' }
$compilerVersion = (Get-Content (Join-Path $instance.installationPath 'VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt') -Raw).Trim()
$compilerFamily = ($compilerVersion.Split('.')[0..1]) -join '.'
$installedVcRoot = Join-Path $instance.installationPath "VC/Tools/MSVC/$compilerVersion"
$catalogPath = Join-Path $env:ProgramData "Microsoft/VisualStudio/Packages/_Instances/$($instance.instanceId)/catalog.json"
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
$ids = @(
    "Microsoft.VC.$compilerFamily.Tools.HostX64.TargetARM64.base",
    "Microsoft.VC.$compilerFamily.Tools.HostX64.TargetARM64.Res.base",
    "Microsoft.VC.$compilerFamily.CRT.ARM64.OneCore.Desktop.base",
    "Microsoft.VC.$compilerFamily.CRT.ARM64.OneCore.Desktop.debug.base",
    "Microsoft.VC.$compilerFamily.CRT.ARM64.Store.base"
)
$packages = foreach ($id in $ids) {
    $matches = @($catalog.packages | Where-Object { $_.id -eq $id -and (!$_.language -or $_.language -eq 'en-US') })
    if ($matches.Count -ne 1 -or $matches[0].type -ne 'Vsix') { throw "Expected one VSIX in the installed catalog: $id" }
    $matches[0]
}
$outputRoot = Join-Path $repoRoot 'artifacts/arm64-tools'
$vcRoot = Join-Path $outputRoot "VC/Tools/MSVC/$compilerVersion"
New-Item -ItemType Directory -Path $vcRoot -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$client = New-Object Net.WebClient
try {
    foreach ($package in $packages) {
        foreach ($payload in $package.payloads) {
            $uri = [uri]$payload.url
            if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'download.visualstudio.microsoft.com') { throw 'Expected a Microsoft Visual Studio payload URL.' }
            if ([IO.Path]::GetFileName($payload.fileName) -ne $payload.fileName) { throw 'Unexpected payload filename.' }
            $archivePath = Join-Path $outputRoot $payload.fileName
            if (!(Test-Path -LiteralPath $archivePath)) { $client.DownloadFile($uri, $archivePath) }
            if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $payload.sha256) {
                throw "Payload differs from the installed catalog: $archivePath"
            }
            $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
            try {
                $prefix = "Contents/VC/Tools/MSVC/$compilerVersion/"
                $files = @($archive.Entries | Where-Object { $_.FullName.StartsWith($prefix, [StringComparison]::Ordinal) -and $_.Name })
                if (!$files.Count) { throw "Payload has no files for toolchain $compilerVersion." }
                foreach ($entry in $files) {
                    $destination = [IO.Path]::GetFullPath((Join-Path $vcRoot $entry.FullName.Substring($prefix.Length)))
                    if (!$destination.StartsWith($vcRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Payload escapes the local toolchain directory.' }
                    # PDB helpers can keep compiler DLLs loaded after a build.
                    # Reusing identical verified files makes preparation repeatable.
                    if ((Test-Path -LiteralPath $destination) -and (Get-Item -LiteralPath $destination).Length -eq $entry.Length) {
                        $entryStream = $entry.Open()
                        $sha = [Security.Cryptography.SHA256]::Create()
                        try { $entryHash = [BitConverter]::ToString($sha.ComputeHash($entryStream)).Replace('-', '') }
                        finally { $sha.Dispose(); $entryStream.Dispose() }
                        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -eq $entryHash) { continue }
                    }
                    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
                    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $true)
                }
            } finally { $archive.Dispose() }
            Write-Output "Verified and extracted $($payload.fileName)"
        }
    }
} finally { $client.Dispose() }
# Headers come from the same installed toolchain; system installation is untouched.
Copy-Item -LiteralPath (Join-Path $installedVcRoot 'include') -Destination $vcRoot -Recurse -Force
# C++/CX uses architecture-neutral platform metadata from this fixed SDK path.
$referenceParent = Join-Path $vcRoot 'lib/x86/store'
New-Item -ItemType Directory -Path $referenceParent -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $installedVcRoot 'lib/x86/store/references') -Destination $referenceParent -Recurse -Force
foreach ($relative in @('bin/Hostx64/arm64/cl.exe', 'bin/Hostx64/arm64/link.exe', 'lib/onecore/arm64/libcpmt.lib', 'lib/onecore/arm64/libcpmtd.lib', 'lib/arm64/store/vccorlib.lib', 'lib/x86/store/references/platform.winmd')) {
    if (!(Test-Path -LiteralPath (Join-Path $vcRoot $relative))) { throw "Local ARM64 toolchain is incomplete: $relative" }
}
[pscustomobject]@{
    completed_utc = [DateTime]::UtcNow.ToString('o')
    instance_id = $instance.instanceId
    compiler_version = $compilerVersion
    toolchain_root = $vcRoot
    catalog_sha256 = (Get-FileHash -LiteralPath $catalogPath -Algorithm SHA256).Hash
    packages = @($packages | Select-Object id, version, chip, language, payloads)
} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $outputRoot 'toolchain.json')
Write-Output "Prepared local ARM64 toolchain: $vcRoot"
