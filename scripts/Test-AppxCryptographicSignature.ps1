param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$CertificateThumbprint
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Security
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
function Read-EntryBytes($Entry) {
    if (!$Entry) { throw 'Required signed package entry is missing.' }
    $stream = $Entry.Open(); $memory = New-Object IO.MemoryStream
    try { $stream.CopyTo($memory); return ,$memory.ToArray() }
    finally { $stream.Dispose(); $memory.Dispose() }
}
try {
    $signature = Read-EntryBytes $archive.GetEntry('AppxSignature.p7x')
    if ($signature.Length -lt 5 -or [Text.Encoding]::ASCII.GetString($signature, 0, 4) -ne 'PKCX') { throw 'Invalid APPX signature header.' }
    $cms = New-Object Security.Cryptography.Pkcs.SignedCms
    $cms.Decode($signature[4..($signature.Length - 1)])
    $cms.CheckSignature($true)
    if ($cms.SignerInfos.Count -ne 1 -or $cms.SignerInfos[0].Certificate.Thumbprint -ne $CertificateThumbprint -or
        $cms.SignerInfos[0].DigestAlgorithm.Value -ne '2.16.840.1.101.3.4.2.1') { throw 'Unexpected package signer or digest algorithm.' }
    $signedContent = [BitConverter]::ToString($cms.ContentInfo.Content).Replace('-', '')
    # APPX's authenticated DigestInfo binds the block map, content types and
    # optional code-integrity catalog. Verify these before trusting file hashes.
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($binding in @(@('AXBM', 'AppxBlockMap.xml'), @('AXCT', '[Content_Types].xml'), @('AXCI', 'AppxMetadata/CodeIntegrity.cat'))) {
            $entry = $archive.GetEntry($binding[1])
            if (!$entry -and $binding[0] -eq 'AXCI') { continue }
            $digest = [BitConverter]::ToString($sha.ComputeHash((Read-EntryBytes $entry))).Replace('-', '')
            $marker = [BitConverter]::ToString([Text.Encoding]::ASCII.GetBytes($binding[0])).Replace('-', '')
            $bindingHex = $marker + $digest
            if ($signedContent.IndexOf($bindingHex, [StringComparison]::Ordinal) -lt 0 -or
                $signedContent.IndexOf($bindingHex, [StringComparison]::Ordinal) -ne $signedContent.LastIndexOf($bindingHex, [StringComparison]::Ordinal)) {
                throw "Package signature does not bind $($binding[1])."
            }
        }
        [xml]$blockMap = [Text.Encoding]::UTF8.GetString((Read-EntryBytes $archive.GetEntry('AppxBlockMap.xml')))
        if ($blockMap.BlockMap.HashMethod -ne 'http://www.w3.org/2001/04/xmlenc#sha256') { throw 'Unsupported block-map hash algorithm.' }
        $allowed = @{}
        foreach ($name in @('AppxSignature.p7x', 'AppxBlockMap.xml', '[Content_Types].xml', 'AppxMetadata/CodeIntegrity.cat')) { $allowed[$name] = $true }
        foreach ($file in $blockMap.BlockMap.File) {
            $name = $file.Name.Replace('\', '/')
            if ($allowed.ContainsKey($name)) { throw "Duplicate or reserved block-map path: $name" }
            $allowed[$name] = $true
            $entry = $archive.GetEntry($name)
            if (!$entry -or $entry.Length -ne [long]$file.Size) { throw "Package entry size differs: $name" }
            $stream = $entry.Open(); $buffer = New-Object byte[] 65536; $total = 0L
            try {
                foreach ($block in $file.Block) {
                    $wanted = [int][Math]::Min(65536L, $entry.Length - $total)
                    $read = 0
                    while ($read -lt $wanted) {
                        $count = $stream.Read($buffer, $read, $wanted - $read)
                        if (!$count) { throw "Truncated package entry: $name" }
                        $read += $count
                    }
                    $total += $read
                    if ([Convert]::ToBase64String($sha.ComputeHash($buffer, 0, $read)) -ne $block.Hash) { throw "Package block hash differs: $name" }
                }
                if ($total -ne $entry.Length -or $stream.ReadByte() -ne -1) { throw "Block map does not cover the complete file: $name" }
            } finally { $stream.Dispose() }
        }
        $seen = @{}
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if (!$allowed.ContainsKey($name) -or $seen.ContainsKey($name)) { throw "Unlisted or duplicate package entry: $name" }
            $seen[$name] = $true
        }
    } finally { $sha.Dispose() }
    Write-Output 'PASS: expected certificate, SHA-256 CMS signature, authenticated block map and every packaged file block; OS certificate trust is unchanged.'
} finally { $archive.Dispose() }
