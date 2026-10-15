param([string]$PerformancePdf)
# In-process native UWP tests; no desktop input or UI automation.
$ErrorActionPreference='Stop'
$editingRepo=Split-Path $PSScriptRoot -Parent
$editingApp=Join-Path $editingRepo 'LitePdfViewer'
$editingOutput=Join-Path $editingRepo 'artifacts/zoom-quality-qa'
$editingState=Join-Path $env:LOCALAPPDATA 'Packages/Ben.LitePdfViewerPreview_z6xh1x1kg5n3j/LocalState/zoom-quality-qa'
if (-not (Get-AppxPackage Ben.LitePdfViewerPreview)) { throw 'Deploy the x64 Debug app from Visual Studio first.' }
if (Get-Process LitePdfViewer -ErrorAction SilentlyContinue) { throw 'Close the viewer first; an existing session will not be interrupted.' }
New-Item $editingOutput -ItemType Directory -Force | Out-Null
New-Item $editingState -ItemType Directory -Force | Out-Null
& python (Join-Path $editingRepo 'tests/GenerateZoomQualityFixture.py') $editingOutput
if ($LASTEXITCODE) { throw 'Fixture generation failed.' }
foreach ($editingFixture in @('source.pdf','navigation.pdf')) { Copy-Item -LiteralPath (Join-Path $editingOutput $editingFixture) -Destination (Join-Path $editingState $editingFixture) -Force }
if ($PerformancePdf) {
    Copy-Item -LiteralPath (Resolve-Path -LiteralPath $PerformancePdf) -Destination (Join-Path $editingState 'performance.pdf') -Force
}
$editingBackups=@{}
$editingStartedPid=$null
function Invoke-EditingBuild([string]$name) {
    & 'C:/Program Files (x86)/MSBuild/14.0/Bin/MSBuild.exe' (Join-Path $editingApp 'LitePdfViewer.csproj') /p:Configuration=Debug /p:Platform=x64 /p:BuildProjectReferences=false /v:minimal /nologo *> (Join-Path $editingOutput $name)
    if ($LASTEXITCODE) { Get-Content (Join-Path $editingOutput $name) -Tail 15; throw 'Reader build failed.' }
}
function Sync-EditingLayout {
    [xml]$editingRecipe=Get-Content (Join-Path $editingApp 'bin/x64/Debug-CppWinRT/LitePdfViewer.build.appxrecipe')
    $editingLayout=(Resolve-Path (Join-Path $editingApp 'bin/x64/Debug-CppWinRT/AppX')).Path
    foreach ($editingItem in $editingRecipe.SelectNodes("//*[local-name()='AppxPackagedFile']")) {
        $editingDestination=[IO.Path]::GetFullPath((Join-Path $editingLayout $editingItem.PackagePath))
        if (-not $editingDestination.StartsWith($editingLayout+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected layout path.' }
        New-Item (Split-Path $editingDestination) -ItemType Directory -Force | Out-Null
        Copy-Item -LiteralPath ([Uri]::UnescapeDataString($editingItem.Include)) -Destination $editingDestination -Force
    }
}
try {
    foreach ($editingName in @('App.xaml.cs','LitePdfViewer.Shared.projitems')) { $editingBackups[$editingName]=[IO.File]::ReadAllBytes((Join-Path $editingApp $editingName)) }
    $editingAppCode=[IO.File]::ReadAllText((Join-Path $editingApp 'App.xaml.cs'))
    $editingHeavy=if ($PerformancePdf) { 'true' } else { 'false' }
    $editingMarker='            Window.Current.Activate();'
    if (-not $editingAppCode.Contains($editingMarker)) { throw 'Launch hook changed.' }
    [IO.File]::WriteAllText((Join-Path $editingApp 'App.xaml.cs'),$editingAppCode.Replace($editingMarker,$editingMarker+"`r`n            if (launchArgs == `"zoom-quality-qa`") { page.RunZoomQualityQa($editingHeavy); return; }"))
    $editingShared=[IO.File]::ReadAllText((Join-Path $editingApp 'LitePdfViewer.Shared.projitems'))
    [IO.File]::WriteAllText((Join-Path $editingApp 'LitePdfViewer.Shared.projitems'),$editingShared.Replace('<Compile Include="L.cs" />','<Compile Include="L.cs" />'+"`r`n    <Compile Include=`"..\tests\ZoomQualityQa.cs`" />"))
    Invoke-EditingBuild 'build.log'; Sync-EditingLayout
    $editingReportPath=Join-Path $editingState 'report.txt'
    [IO.File]::WriteAllText($editingReportPath,'')
    Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $editingRepo 'tests/ReaderQaActivation.cs')))
    $editingStartedPid=[ReaderQaActivation]::Launch("zoom-quality-qa")
    $editingDeadline=[DateTime]::UtcNow.AddMinutes(4)
    do { Start-Sleep -Milliseconds 500; $editingReport=[IO.File]::ReadAllText($editingReportPath) } while (-not $editingReport -and [DateTime]::UtcNow -lt $editingDeadline)
    $editingReport | Write-Output
    Copy-Item -LiteralPath $editingReportPath -Destination (Join-Path $editingOutput 'report.txt') -Force
    if ($editingReport -notmatch 'PASS: zoom and raster quality QA completed' -or $editingReport -match 'FAIL:') { throw 'Native reader editing QA failed.' }
} finally {
    if ($editingStartedPid) {
        $editingProcess=Get-Process -Id $editingStartedPid -ErrorAction SilentlyContinue
        $editingExpectedPath=[IO.Path]::GetFullPath((Join-Path $editingApp 'bin/x64/Debug-CppWinRT/AppX/LitePdfViewer.exe'))
        if ($editingProcess -and $editingProcess.Path -eq $editingExpectedPath) { Stop-Process -Id $editingStartedPid; $editingProcess.WaitForExit(10000) | Out-Null }
    }
    foreach ($editingName in $editingBackups.Keys) { [IO.File]::WriteAllBytes((Join-Path $editingApp $editingName),$editingBackups[$editingName]); (Get-Item (Join-Path $editingApp $editingName)).LastWriteTimeUtc=[DateTime]::UtcNow }
    Invoke-EditingBuild 'production-build.log'; Sync-EditingLayout
}
