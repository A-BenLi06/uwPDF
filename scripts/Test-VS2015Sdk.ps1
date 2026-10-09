# Check the SDK lookup that VS 2015 uses, without installing or retargeting.
[CmdletBinding()]
param([string]$ReportPath = '')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (!$ReportPath) { $ReportPath = Join-Path $repoRoot 'artifacts/diagnostics/sdk2015-check.json' }
$ReportPath = [IO.Path]::GetFullPath($ReportPath)
# VS 2015 is a 32-bit .NET Framework process. PowerShell 7 uses a different CLR.
if ([Environment]::Is64BitProcess -or [Environment]::Version.Major -ge 5) {
    $frameworkShell = Join-Path $env:WINDIR 'SysWOW64/WindowsPowerShell/v1.0/powershell.exe'
    & $frameworkShell -NoProfile -File $PSCommandPath -ReportPath $ReportPath
    if ($LASTEXITCODE -ne 0) { throw "VS 2015 SDK probe failed: $LASTEXITCODE" }
    return
}
$utilities = Join-Path ${env:ProgramFiles(x86)} 'MSBuild/14.0/Bin/Microsoft.Build.Utilities.Core.dll'
Add-Type -Path $utilities
$helper = [Microsoft.Build.Utilities.ToolLocationHelper]
$version = '10.0.14393.0'
$location = $helper::GetPlatformSDKLocation('UAP', $version)
$platforms = @($helper::GetPlatformsForSDK('Windows', [Version]'10.0'))
$platformSdk = @($helper::GetTargetPlatformSdks() | Where-Object { $_.ContainsPlatform('UAP', $version) })
$required = @(
    "Platforms/UAP/$version/Platform.xml",
    "Platforms/UAP/$version/PreviousPlatforms.xml",
    "Include/$version/um/windows.h",
    "Include/$version/winrt/windows.data.pdf.h",
    "Lib/$version/um/x86/WindowsApp.lib",
    "Lib/$version/um/x64/WindowsApp.lib",
    "Lib/$version/um/arm/WindowsApp.lib"
)
$missing = @($required | Where-Object { !$location -or !(Test-Path -LiteralPath (Join-Path $location $_)) })
$contracts = @()
if ($location -and !$missing.Count) {
    [xml]$platform = Get-Content -LiteralPath (Join-Path $location "Platforms/UAP/$version/Platform.xml")
    foreach ($contract in $platform.ApplicationPlatform.ContainedApiContracts.ApiContract) {
        $relative = "References/$($contract.name)/$($contract.version)/$($contract.name).winmd"
        $exists = Test-Path -LiteralPath (Join-Path $location $relative)
        $contracts += [pscustomobject]@{ name=$contract.name; version=$contract.version; exists=$exists }
        if (!$exists) { $missing += $relative }
    }
}
$report = [pscustomobject]@{
    checked_utc = [DateTime]::UtcNow.ToString('o')
    process_bitness = 32
    utilities_assembly = $helper.Assembly.Location
    sdk_version = $version
    sdk_location = $location
    enumerated_platforms = $platforms
    contains_platform = $platformSdk.Count -gt 0
    contracts = $contracts
    missing_files = $missing
    ide_project_load_verified = $false
    scope = 'Fresh VS 2015/MSBuild 14 SDK enumeration, required headers/libraries and every platform contract; excludes the running IDE cache, project loading, debugging and Diagnostic Tools graphs'
}
New-Item -ItemType Directory -Path (Split-Path $ReportPath -Parent) -Force | Out-Null
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
if (!$report.contains_platform -or $missing.Count -or $platforms -notcontains "UAP, Version=$version") {
    throw "SDK $version lookup or payload check failed. See $ReportPath"
}
Write-Output "PASS: VS 2015 SDK $version enumeration and payload. Report: $ReportPath (live IDE behavior remains separate)."
