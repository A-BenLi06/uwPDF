$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    & python (Join-Path $repoRoot 'tests/VerifyCiReleaseAssets.py')
    if ($LASTEXITCODE) { throw 'Release asset gate tests failed.' }
    & python (Join-Path $repoRoot 'tests/VerifyCiReleaseSource.py')
    if ($LASTEXITCODE) { throw 'Release source gate tests failed.' }
    & python (Join-Path $repoRoot 'tests/ValidateLocalization.py')
    if ($LASTEXITCODE) { throw 'Localization validation failed.' }
    # Each process has independent Add-Type definitions and script scope.
    foreach ($test in @('Keyboard', 'MemoryRecovery', 'NativeCleanup', 'PageGeometry',
        'PreviewSizing', 'PreviewStartup', 'PrintLayout', 'RasterBackend', 'RasterQuality',
        'RasterRetirement', 'RenderLoop', 'RenderScheduler', 'ScrollActivity', 'SearchScroll',
        'TextSearch', 'TextWorkers', 'Zoom')) {
        Write-Output "Running $test regression tests"
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Test-$test.ps1")
        if ($LASTEXITCODE) { throw "$test regression tests failed." }
    }
} finally { Pop-Location }
