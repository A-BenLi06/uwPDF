param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [ValidateSet('x86', 'x64', 'ARM', 'ARM64')][string]$Platform = 'x64',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Test-CppWinRTBinary.ps1') -Platform $Platform -Configuration $Configuration
$componentPath = Join-Path $repoRoot "artifacts/cppwinrt/$Platform/$Configuration/PdfNative.Rendering.dll"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
try {
    $manifestEntry = $archive.GetEntry('AppxManifest.xml')
    if (!$manifestEntry) { throw 'Package manifest is missing.' }
    $reader = New-Object IO.StreamReader($manifestEntry.Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $identity = $manifest.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']")
    if ($identity.ProcessorArchitecture -ne $Platform) { throw "Package architecture is $($identity.ProcessorArchitecture), expected $Platform." }
    $serverPath = 'PdfNative.Rendering.dll'
    foreach ($className in @('PdfSurfaceRenderer','InkOutlineExporter','PreviewDisplay','PdfTextDocument','PdfTextPage','PdfAnnotationWriter')) {
        $classes = $manifest.SelectNodes("//*[local-name()='ActivatableClass'][@ActivatableClassId='PdfNative.Rendering.$className']")
        if ($classes.Count -ne 1 -or $classes[0].ParentNode.SelectSingleNode("*[local-name()='Path']").InnerText -ne $serverPath) {
            throw "Expected exactly one C++/WinRT registration in $serverPath : $className"
        }
    }
    if ($archive.GetEntry('PdfNative.dll') -or $archive.GetEntry('PdfNative.winmd') -or
        $manifest.SelectNodes("//*[local-name()='ActivatableClass'][starts-with(@ActivatableClassId,'PdfNative.') and not(starts-with(@ActivatableClassId,'PdfNative.Rendering.'))]").Count) {
        throw 'Modern package includes the legacy bridge or a duplicate document engine.'
    }
    $dllEntry = $archive.GetEntry($serverPath)
    if (!$dllEntry) { throw 'The registered renderer DLL is not packaged.' }
    $dllStream = $dllEntry.Open()
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $packagedHash = [BitConverter]::ToString($sha.ComputeHash($dllStream)).Replace('-', '') }
    finally { $sha.Dispose(); $dllStream.Dispose() }
    $builtHash = (Get-FileHash -LiteralPath $componentPath -Algorithm SHA256).Hash
    if ($packagedHash -ne $builtHash) { throw 'The package contains a stale or different renderer DLL.' }
    Write-Output "PASS: $Platform/$Configuration package has all six C++/WinRT registrations, the exact built DLL and no legacy bridge."
}
finally { $archive.Dispose() }
