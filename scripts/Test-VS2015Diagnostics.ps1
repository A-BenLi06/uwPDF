# Checks the collector infrastructure without opening VS or changing registration.
[CmdletBinding()]
param([string]$ReportPath = '')
$ErrorActionPreference = 'Stop'
if (!$ReportPath) { $ReportPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/diagnostics/collector-check.json' }
$serviceName = 'VSStandardCollectorService140'
$collectorPath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio 14.0/Team Tools/DiagnosticsHub/Collector/StandardCollector.Service.exe'
$before = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
$instance = $null
$activationSucceeded = $false
$activationError = $null
try {
    $collectorType = [Type]::GetTypeFromCLSID([Guid]'{874E03B4-29BD-4628-A0F6-78B8B011ADA9}')
    # COM may start the existing on-demand service; it does not reinstall it.
    $instance = [Activator]::CreateInstance($collectorType)
    $activationSucceeded = $true
} catch { $activationError = $_.Exception.ToString() }
finally { if ($instance) { [Runtime.InteropServices.Marshal]::ReleaseComObject($instance) | Out-Null } }
$after = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
$report = [PSCustomObject]@{
    checked_utc = [DateTime]::UtcNow.ToString('o')
    collector_exists = Test-Path -LiteralPath $collectorPath
    service_registered = $null -ne $before
    service_before = if ($before) { $before.Status.ToString() } else { $null }
    service_after = if ($after) { $after.Status.ToString() } else { $null }
    com_activation_succeeded = $activationSucceeded
    activation_error = $activationError
    vs_graph_collection_verified = $false
    scope = 'Collector executable, service and COM activation; excludes Visual Studio profiling session and graph collection'
}
New-Item -ItemType Directory -Path (Split-Path $ReportPath -Parent) -Force | Out-Null
$report | ConvertTo-Json | Set-Content -LiteralPath $ReportPath -Encoding UTF8
if (!$report.collector_exists -or !$report.service_registered -or !$activationSucceeded -or $report.service_after -ne 'Running') {
    throw "VS 2015 collector check failed. See $ReportPath"
}
Write-Output "PASS: VS 2015 collector service and COM activation. Report: $ReportPath (VS graph collection remains unverified)."
