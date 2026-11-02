param([Parameter(Mandatory = $true)][string]$Directory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$Directory = (Resolve-Path -LiteralPath $Directory).Path
$info = Get-Content (Join-Path $Directory 'build-info.json') -Raw | ConvertFrom-Json
$expected = @('uwPDF.cer', 'build-info.json')
foreach ($platform in @('x86', 'x64', 'ARM', 'ARM64')) {
    $extension = if ($platform -eq 'ARM64') { '.msix' } else { '.appx' }
    $packageName = "uwPDF_$($info.version)_$platform$extension"
    $zipName = "uwPDF_$($info.version)_$($platform)_Install.zip"
    $expected += @($packageName, $zipName)
    $packageHash = (Get-FileHash -LiteralPath (Join-Path $Directory $packageName)).Hash
    if ($packageHash -ne $info.signed_package_sha256.$packageName) { throw 'Signed package differs from build provenance.' }
    $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $Directory $zipName))
    try {
        $entries = @($archive.Entries | Where-Object Name)
        foreach ($entry in $entries) {
            $name = $entry.FullName.Replace('\', '/')
            if ($name -ne $packageName -and $name -ne 'uwPDF.cer' -and $name -ne 'Add-AppDevPackage.ps1' -and
                $name -notmatch '^Dependencies/[^/]+\.(appx|msix)$') { throw "Unexpected installer entry: $name" }
        }
        foreach ($required in @($packageName, 'uwPDF.cer', 'Add-AppDevPackage.ps1')) {
            if (@($entries | Where-Object FullName -eq $required).Count -ne 1) { throw "Missing/duplicate installer entry: $required" }
        }
        if (!@($entries | Where-Object FullName -match '^Dependencies[/\\]').Count) { throw 'Installer ZIP has no dependencies.' }
        foreach ($name in @($packageName, 'uwPDF.cer')) {
            $stream = $archive.GetEntry($name).Open(); $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
            finally { $stream.Dispose(); $sha.Dispose() }
            if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $Directory $name)).Hash) { throw "Installer contains different bytes: $name" }
        }
    } finally { $archive.Dispose() }
}
$files = @(Get-ChildItem -LiteralPath $Directory -Force)
if (@($files | Where-Object { $_.Name -notin ($expected + 'SHA256SUMS.txt') }).Count -or $files.Count -ne $expected.Count + 1) {
    throw 'Release asset allowlist mismatch; upload containers and private keys are forbidden.'
}
$lines = @(Get-Content -LiteralPath (Join-Path $Directory 'SHA256SUMS.txt'))
if ($lines.Count -ne $expected.Count) { throw 'Checksum manifest does not cover every asset.' }
$seen = @{}
foreach ($line in $lines) {
    if ($line -notmatch '^([a-fA-F0-9]{64})  ([A-Za-z0-9_.-]+)$') { throw 'Invalid checksum manifest.' }
    $hash = $Matches[1]; $name = $Matches[2]
    if ($name -notin $expected -or $seen.ContainsKey($name)) { throw 'Unexpected or duplicate checksum entry.' }
    $seen[$name] = $true
    if ((Get-FileHash -LiteralPath (Join-Path $Directory $name)).Hash -ne $hash) { throw "Release checksum differs: $name" }
}
Write-Output 'PASS: four installers, exact package/certificate bytes, asset allowlist and SHA-256 checksums.'
