param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Test-PackageBaseline.ps1') -PackagePath $PackagePath -MinimumVersion '10.0.17763.0' -TargetVersion '10.0.26100.0'
& (Join-Path $PSScriptRoot 'Test-CppWinRTPackage.ps1') -PackagePath $PackagePath -Platform ARM64 -Configuration $Configuration
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
try {
    $reader = [IO.StreamReader]::new($archive.GetEntry('AppxManifest.xml').Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $dependencies = @($manifest.SelectNodes("//*[local-name()='PackageDependency']") | ForEach-Object { $_.Name })
    $frameworkDependency = if ($Configuration -eq 'Debug') { 'Microsoft.NET.Native.Framework.Debug.2.2' } else { 'Microsoft.NET.Native.Framework.2.2' }
    foreach ($dependency in @($frameworkDependency, 'Microsoft.NET.Native.Runtime.2.2')) {
        if ($dependency -notin $dependencies) { throw "Missing ARM64 .NET Native dependency: $dependency" }
    }
    $nativeImages = [Collections.Generic.List[string]]::new()
    foreach ($entry in $archive.Entries | Where-Object { $_.FullName -match '\.(dll|exe)$' }) {
        $stream = $entry.Open()
        $memory = [IO.MemoryStream]::new()
        try { $stream.CopyTo($memory); $bytes = $memory.ToArray() }
        finally { $stream.Dispose(); $memory.Dispose() }
        if ($bytes.Length -lt 128 -or $bytes[0] -ne 0x4d -or $bytes[1] -ne 0x5a) { throw "Invalid PE payload: $($entry.FullName)" }
        $peOffset = [BitConverter]::ToInt32($bytes, 60)
        if ($peOffset -lt 64 -or $peOffset + 96 -gt $bytes.Length -or [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x4550) { throw "Invalid PE header: $($entry.FullName)" }
        $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
        $magic = [BitConverter]::ToUInt16($bytes, $peOffset + 24)
        $dataDirectory = $peOffset + 24 + $(if ($magic -eq 0x10b) { 96 } elseif ($magic -eq 0x20b) { 112 } else { throw 'Unexpected PE optional header.' })
        $clrDirectory = $dataDirectory + 14 * 8
        # This build uses .NET Native in both configurations. WinMD metadata is
        # packaged separately; executable/DLL payloads must all be native ARM64.
        $hasClr = $clrDirectory + 8 -le $bytes.Length -and [BitConverter]::ToUInt32($bytes, $clrDirectory) -ne 0
        if ($hasClr) { throw "Unexpected managed executable/DLL in the .NET Native package: $($entry.FullName)" }
        if ($machine -ne 0xaa64) { throw "Native payload is not ARM64: $($entry.FullName)" }
        $nativeImages.Add($entry.FullName)
    }
    foreach ($required in @('LitePdfViewer.exe', 'PdfNative.Rendering.dll')) {
        if ($required -notin $nativeImages) { throw "Missing ARM64 native payload: $required" }
    }
    [pscustomobject]@{
        completed_utc = [DateTime]::UtcNow.ToString('o')
        configuration = $Configuration
        package_path = (Resolve-Path -LiteralPath $PackagePath).Path
        package_sha256 = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash
        native_images = @($nativeImages)
        dependencies = $dependencies
        scope = 'Packaged OS baseline, ARM64 native images, six WinRT registrations, current DLL hash, no duplicate document engine and .NET Native dependencies; excludes installation and device runtime'
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $repoRoot "artifacts/arm64-tools/package-$($Configuration.ToLowerInvariant())-result.json")
    Write-Output "PASS: full ARM64/$Configuration UWP package architecture, C++/WinRT registrations and .NET Native dependencies."
} finally { $archive.Dispose() }
