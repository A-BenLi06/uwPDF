# Repair only VS 2015's Diagnostics Hub collector using its own installer.
# Run from an elevated Windows PowerShell after stopping all VS 2015 profiling.
[CmdletBinding()]
param([string]$ReportDirectory = (Join-Path $env:TEMP 'uwPDF-VS2015Diagnostics'))

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Administrator rights are required to repair the collector service registration.'
}
$collector = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio 14.0\Team Tools\DiagnosticsHub\Collector\StandardCollector.Service.exe'
if (-not (Test-Path -LiteralPath $collector)) { throw 'VS 2015 collector executable was not found.' }
$serviceName = 'VSStandardCollectorService140'
$clsid = '{874E03B4-29BD-4628-A0F6-78B8B011ADA9}'
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$report = Join-Path $ReportDirectory 'repair.log'
Start-Transcript -LiteralPath $report -Force | Out-Null
try {
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne 'Stopped') { Stop-Service -Name $serviceName }
    # Save existing class, AppID and service registration before repairing them.
    $keys = @(
        "HKLM\SOFTWARE\Classes\CLSID\$clsid",
        "HKLM\SOFTWARE\Classes\AppID\$clsid",
        "HKLM\SOFTWARE\Classes\WOW6432Node\CLSID\$clsid",
        "HKLM\SOFTWARE\Classes\WOW6432Node\AppID\$clsid",
        "HKLM\SYSTEM\CurrentControlSet\Services\$serviceName"
    )
    for ($i = 0; $i -lt $keys.Count; $i++) {
        if (Test-Path ('Registry::' + $keys[$i].Replace('HKLM', 'HKEY_LOCAL_MACHINE'))) {
            & reg.exe export $keys[$i] (Join-Path $ReportDirectory ("registration-$i.reg")) /y
            if ($LASTEXITCODE -ne 0) { throw 'Could not back up collector registration.' }
        }
    }
    if ($service) {
        & $collector uninstall
        if ($LASTEXITCODE -ne 0) { throw "Collector uninstall failed: $LASTEXITCODE" }
    }
    & $collector install
    if ($LASTEXITCODE -ne 0) { throw "Collector install failed: $LASTEXITCODE" }
    Start-Service -Name $serviceName
    $instance = [Activator]::CreateInstance([Type]::GetTypeFromCLSID([Guid]$clsid))
    [Runtime.InteropServices.Marshal]::ReleaseComObject($instance) | Out-Null
    Get-Service -Name $serviceName | Format-Table Name,Status
    'Collector COM activation succeeded.'
    Set-Content -LiteralPath (Join-Path $ReportDirectory 'success.txt') -Value (Get-Date).ToString('o')
} catch {
    $_ | Out-String | Write-Output
    Set-Content -LiteralPath (Join-Path $ReportDirectory 'failure.txt') -Value $_.Exception.ToString()
    throw
} finally {
    Stop-Transcript | Out-Null
}
