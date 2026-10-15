param([Parameter(Mandatory=$true)][string]$PackagePath)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
$identity=Get-Content (Join-Path $repoRoot 'LitePdfViewer/StoreIdentity.json') -Raw | ConvertFrom-Json
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
try {
    $reader=New-Object IO.StreamReader($archive.GetEntry('AppxManifest.xml').Open())
    try { [xml]$manifest=$reader.ReadToEnd() } finally { $reader.Dispose() }
    $package=$manifest.SelectSingleNode("/*[local-name()='Package']")
    $actual=$package.SelectSingleNode("*[local-name()='Identity']")
    if ($actual.Name -cne $identity.name -or $actual.Publisher -cne $identity.publisher) { throw 'Package does not use the supplied Store identity.' }
    if ($package.SelectSingleNode("*[local-name()='Properties']/*[local-name()='PublisherDisplayName']").InnerText -cne $identity.publisherDisplayName) { throw 'Store publisher display name differs.' }
    $languages=@($package.SelectNodes("*[local-name()='Resources']/*[local-name()='Resource']") | ForEach-Object { $_.Language.ToLowerInvariant() })
    $difference=Compare-Object @('en-us','ja-jp','zh-hans','zh-hant') $languages
    if ($difference) { throw "Package languages differ: $languages" }
    if (!$archive.GetEntry('resources.pri')) { throw 'Compiled language resources missing.' }
    if ($archive.GetEntry('AppxSignature.p7x')) { throw 'Store package unexpectedly used a local signing certificate.' }
    foreach ($element in $package.SelectNodes(".//*[@DisplayName or @Description]")) {
        foreach ($name in @('DisplayName','Description')) {
            if ($element.HasAttribute($name) -and !$element.GetAttribute($name).StartsWith('ms-resource:')) { throw "Unlocalized manifest $name" }
        }
    }
    Write-Output "PASS: Store identity/publisher, all four language declarations, localized manifest, compiled PRI and unsigned upload package ($($actual.ProcessorArchitecture))."
} finally { $archive.Dispose() }

# PFN and SID are derived identifiers, not extra AppxManifest identity fields.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class StoreSidVerifier {
    [DllImport("userenv.dll", CharSet=CharSet.Unicode)] static extern int DeriveAppContainerSidFromAppContainerName(string name, out IntPtr sid);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool ConvertSidToStringSid(IntPtr sid, out IntPtr text);
    [DllImport("advapi32.dll")] static extern IntPtr FreeSid(IntPtr sid);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr text);
    public static string Derive(string name) {
        IntPtr sid, text;
        Marshal.ThrowExceptionForHR(DeriveAppContainerSidFromAppContainerName(name,out sid));
        try {
            if(!ConvertSidToStringSid(sid,out text)) throw new System.ComponentModel.Win32Exception();
            try { return Marshal.PtrToStringUni(text); } finally { LocalFree(text); }
        } finally { FreeSid(sid); }
    }
}
'@
if ([StoreSidVerifier]::Derive($identity.packageFamilyName) -cne $identity.packageSid) { throw 'Store PFN and SID do not match.' }
Write-Output 'PASS: supplied package SID matches the PFN derived by Windows.'
