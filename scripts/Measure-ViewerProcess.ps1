param(
    [Parameter(Mandatory=$true)][int]$ProcessId,
    [Parameter(Mandatory=$true)][string]$ExpectedExecutable,
    [ValidateRange(1,3600)][int]$DurationSeconds=180,
    [ValidateRange(50,5000)][int]$IntervalMilliseconds=200,
    [string[]]$FixturePaths=@(),
    [string]$ReportPath='artifacts/performance/process-run.json'
)
$ErrorActionPreference='Stop'
$expected=(Resolve-Path -LiteralPath $ExpectedExecutable).Path
$process=Get-Process -Id $ProcessId
if ($process.ProcessName -ne 'LitePdfViewer' -or $process.Path -ne $expected) {
    throw 'The selected process is not the expected viewer executable.'
}
$processStart=$process.StartTime.ToUniversalTime().ToString('o')
$report=$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ReportPath)
New-Item -ItemType Directory -Path (Split-Path $report -Parent) -Force | Out-Null
if (Test-Path -LiteralPath $report) { throw 'Use a new report path for each measurement.' }
$fixtures=@($FixturePaths | ForEach-Object {
    $fixture=Get-Item -LiteralPath $_
    [pscustomobject]@{path=$fixture.FullName;bytes=$fixture.Length;sha256=(Get-FileHash -LiteralPath $fixture.FullName).Hash}
})
$applicationFiles=@(Get-ChildItem -LiteralPath (Split-Path $expected -Parent) -File |
    Where-Object { $_.Name -in @('LitePdfViewer.exe','LitePdfViewer.dll','PdfNative.Rendering.dll','AppxManifest.xml') } |
    ForEach-Object { [pscustomobject]@{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
$samples=New-Object 'System.Collections.Generic.List[object]'
$timer=[Diagnostics.Stopwatch]::StartNew()
$reason='duration'
$samplingError=$null
try {
    while ($timer.Elapsed.TotalSeconds -lt $DurationSeconds) {
        $process.Refresh()
        if ($process.HasExited) { $reason='process exited'; break }
        $samples.Add([pscustomobject]@{
            elapsed_ms=$timer.Elapsed.TotalMilliseconds
            utc=[DateTime]::UtcNow.ToString('o')
            private_bytes=$process.PrivateMemorySize64
            working_set_bytes=$process.WorkingSet64
            peak_working_set_bytes=$process.PeakWorkingSet64
            cpu_ms=$process.TotalProcessorTime.TotalMilliseconds
            handles=$process.HandleCount
        })
        Start-Sleep -Milliseconds $IntervalMilliseconds
    }
} catch {
    if ($process.HasExited) { $reason='process exited' }
    else { $reason='sampling error'; $samplingError=$_.Exception.Message; throw }
} finally {
    $timer.Stop()
    $loadedModules=@()
    if (!$process.HasExited) {
        $loadedModules=@($process.Modules | ForEach-Object { $_.ModuleName })
    }
    [pscustomobject]@{
        completed_utc=[DateTime]::UtcNow.ToString('o')
        process_id=$ProcessId;process_started_utc=$processStart;expected_executable=$expected
        duration_ms=$timer.Elapsed.TotalMilliseconds;requested_interval_ms=$IntervalMilliseconds
        logical_processors=[Environment]::ProcessorCount;termination_reason=$reason
        sampling_error=$samplingError
        application_files=$applicationFiles;fixtures=$fixtures;loaded_modules=$loadedModules
        script_sha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash
        scope='Observed process private bytes, working set, handles and accumulated CPU at actual sample times. Sampling can miss peaks; excludes GPU memory, frame/presentation/input latency and controlled cold startup. UI actions and their times must be recorded separately.'
        samples=@($samples.ToArray())
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $report -Encoding UTF8
    $process.Dispose()
}
Write-Output "Recorded $($samples.Count) viewer process samples: $report"
