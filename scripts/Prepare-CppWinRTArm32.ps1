param([string]$CatalogPath = '')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (!$CatalogPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $instances = @((& $vswhere -all -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -format json) -join "`n" | ConvertFrom-Json)
    $instance = $instances | Where-Object { $_.productId -eq 'Microsoft.VisualStudio.Product.BuildTools' } | Select-Object -First 1
    if (!$instance) { $instance = $instances | Select-Object -First 1 }
    if (!$instance) { throw 'Install Visual Studio C++ tools before preparing the local ARM32 toolchain.' }
    $CatalogPath = Join-Path $env:ProgramData "Microsoft/VisualStudio/Packages/_Instances/$($instance.instanceId)/catalog.json"
}
$catalog = Get-Content -LiteralPath $CatalogPath -Raw | ConvertFrom-Json
# Last VS 2022 family with ARM32, pinned separately from the VS 2026 toolchain.
$family = 'Microsoft.VC.14.44.17.14'
$ids = @("$family.Tools.HostX64.TargetARM.base", "$family.Tools.HostX64.TargetARM.Res.base",
    "$family.CRT.Headers.base", "$family.CRT.arm.OneCore.Desktop.base")
$packages = foreach ($id in $ids) {
    $matches = @($catalog.packages | Where-Object { $_.id -eq $id -and (!$_.language -or $_.language -eq 'en-US') })
    if ($matches.Count -ne 1 -or $matches[0].type -ne 'Vsix') { throw "Expected one pinned VSIX in the catalog: $id" }
    $matches[0]
}
$outputRoot = Join-Path $repoRoot 'artifacts/arm32-tools'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$client = New-Object Net.WebClient
$compilerVersion = $null
$vcRoot = $null
try {
    foreach ($package in $packages) {
        foreach ($payload in $package.payloads) {
            $uri = [uri]$payload.url
            if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'download.visualstudio.microsoft.com') { throw 'Expected a Microsoft Visual Studio payload URL.' }
            if ([IO.Path]::GetFileName($payload.fileName) -ne $payload.fileName) { throw 'Unexpected payload filename.' }
            $archivePath = Join-Path $outputRoot $payload.fileName
            if (!(Test-Path -LiteralPath $archivePath)) { $client.DownloadFile($uri, $archivePath) }
            if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $payload.sha256) { throw "Payload differs from the installed catalog: $archivePath" }
            $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
            try {
                $files = @($archive.Entries | Where-Object { $_.FullName -match '^Contents/VC/Tools/MSVC/[0-9.]+/' -and $_.Name })
                if (!$files.Count) { throw 'Payload contains no MSVC files.' }
                foreach ($entry in $files) {
                    if ($entry.FullName -notmatch '^Contents/VC/Tools/MSVC/([0-9.]+)/(.*)$') { throw 'Unexpected compiler payload path.' }
                    $entryVersion = $Matches[1]; $relative = [uri]::UnescapeDataString($Matches[2])
                    if (!$compilerVersion) {
                        $compilerVersion = $entryVersion
                        $vcRoot = [IO.Path]::GetFullPath((Join-Path $outputRoot "VC/Tools/MSVC/$compilerVersion"))
                    }
                    if ($entryVersion -ne $compilerVersion) { throw 'Pinned compiler, headers and CRT use different toolchain directories.' }
                    $destination = [IO.Path]::GetFullPath((Join-Path $vcRoot $relative))
                    if (!$destination.StartsWith($vcRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Payload escapes the local toolchain directory.' }
                    $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
                    try { $entryHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
                    finally { $sha.Dispose(); $stream.Dispose() }
                    if ((Test-Path -LiteralPath $destination) -and (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -eq $entryHash) { continue }
                    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
                    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $true)
                }
            } finally { $archive.Dispose() }
            Write-Output "Prepared verified payload: $($package.id)"
        }
    }
} finally { $client.Dispose() }
foreach ($relative in @('bin/Hostx64/arm/cl.exe','bin/Hostx64/arm/link.exe','include/coroutine',
    'lib/onecore/arm/libcpmt.lib','lib/onecore/arm/libcpmtd.lib')) {
    if (!(Test-Path -LiteralPath (Join-Path $vcRoot $relative))) { throw "Local ARM32 toolchain is incomplete: $relative" }
}
# SDK 26100's C++/WinRT base library dropped ARM32. Use the unmodified
# Microsoft-signed 2023 generator to emit its ARM32-capable base/projections.
$cppVersion = '2.0.230706.1'
$cppPackageHash = 'A99ECA1C244DD730B31554E6D4850E685F40BFB7CB0BD1CFB1561169FC3B692B'
$cppPackagePath = Join-Path $outputRoot "microsoft.windows.cppwinrt.$cppVersion.nupkg"
if (!(Test-Path -LiteralPath $cppPackagePath)) {
    $client = New-Object Net.WebClient
    try { $client.DownloadFile("https://api.nuget.org/v3-flatcontainer/microsoft.windows.cppwinrt/$cppVersion/microsoft.windows.cppwinrt.$cppVersion.nupkg", $cppPackagePath) }
    finally { $client.Dispose() }
}
if ((Get-FileHash -LiteralPath $cppPackagePath -Algorithm SHA256).Hash -ne $cppPackageHash) { throw 'Pinned C++/WinRT package hash differs.' }
$cppRoot = Join-Path $outputRoot "cppwinrt/$cppVersion"
$archive = [IO.Compression.ZipFile]::OpenRead($cppPackagePath)
try {
    foreach ($relative in @('bin/cppwinrt.exe','LICENSE')) {
        $entry = $archive.GetEntry($relative)
        if (!$entry) { throw "Pinned C++/WinRT package is incomplete: $relative" }
        $destination = Join-Path $cppRoot $relative
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $true)
    }
} finally { $archive.Dispose() }
$licenseRoot = Join-Path $repoRoot 'artifacts/native-dependencies/licenses'
New-Item -ItemType Directory -Path $licenseRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $cppRoot 'LICENSE') -Destination (Join-Path $licenseRoot 'cppwinrt-LICENSE') -Force
[pscustomobject]@{
    completed_utc = [DateTime]::UtcNow.ToString('o')
    compiler_version = $compilerVersion
    toolchain_root = $vcRoot
    catalog_path = (Resolve-Path -LiteralPath $CatalogPath).Path
    catalog_sha256 = (Get-FileHash -LiteralPath $CatalogPath -Algorithm SHA256).Hash
    packages = @($packages | Select-Object id,version,chip,language,payloads)
    cppwinrt_version = $cppVersion
    cppwinrt_package_sha256 = $cppPackageHash
    cppwinrt_path = Join-Path $cppRoot 'bin/cppwinrt.exe'
    cppwinrt_sha256 = (Get-FileHash -LiteralPath (Join-Path $cppRoot 'bin/cppwinrt.exe') -Algorithm SHA256).Hash
    scope = 'Local hash-verified ARM32 compiler/headers/static OneCore CRT; no system installation or registration'
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputRoot 'toolchain.json') -Encoding UTF8
Write-Output "Prepared local ARM32 toolchain: $vcRoot"
