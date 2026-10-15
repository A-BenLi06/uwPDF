param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [string]$MinimumVersion = '10.0.14393.0',
    [string]$TargetVersion = '10.0.14393.0'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
try {
    $manifestEntry = $archive.GetEntry('AppxManifest.xml')
    if (!$manifestEntry) { throw 'Package manifest is missing.' }
    $reader = New-Object IO.StreamReader($manifestEntry.Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $families = $manifest.SelectNodes("//*[local-name()='TargetDeviceFamily']")
    if ($families.Count -ne 1 -or $families[0].Name -ne 'Windows.Universal') { throw 'Expected one Windows.Universal device family.' }
    if ($families[0].MinVersion -ne $MinimumVersion -or $families[0].MaxVersionTested -ne $TargetVersion) {
        throw "Package baseline differs: minimum=$($families[0].MinVersion), target=$($families[0].MaxVersionTested)."
    }
    $identity = $manifest.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']")
    Write-Output "PASS: $($identity.ProcessorArchitecture) package minimum=$MinimumVersion, target=$TargetVersion."
} finally { $archive.Dispose() }
