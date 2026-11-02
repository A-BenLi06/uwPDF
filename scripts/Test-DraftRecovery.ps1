# Real UWP storage/ink and process restart; no desktop input automation.
$ErrorActionPreference='Stop'
$draftRepo=Split-Path $PSScriptRoot -Parent
$draftApp=Join-Path $draftRepo 'LitePdfViewer'
$draftOutput=Join-Path $draftRepo 'artifacts/draft-recovery-qa'
$draftState=Join-Path $env:LOCALAPPDATA 'Packages/Ben.LitePdfViewerPreview_z6xh1x1kg5n3j/LocalState/draft-recovery-qa'
if (!(Get-AppxPackage Ben.LitePdfViewerPreview)) { throw 'Deploy the x64 Debug viewer first.' }
if (Get-Process LitePdfViewer -ErrorAction SilentlyContinue) { throw 'Close the viewer first; an existing session will not be interrupted.' }
New-Item $draftOutput -ItemType Directory -Force | Out-Null
New-Item $draftState -ItemType Directory -Force | Out-Null
$draftFixture='fixture-'+[Guid]::NewGuid().ToString('N')+'.pdf'
Copy-Item -LiteralPath (Join-Path $draftApp 'TestAssets/SkimSample.pdf') -Destination (Join-Path $draftState $draftFixture)
[IO.File]::WriteAllText((Join-Path $draftState 'fixture-name.txt'),$draftFixture)
$draftBackups=@{}
$draftPid=$null
function Build-DraftQa([string]$log) {
    & 'C:/Program Files (x86)/MSBuild/14.0/Bin/MSBuild.exe' (Join-Path $draftApp 'LitePdfViewer.csproj') /p:Configuration=Debug /p:Platform=x64 /p:BuildProjectReferences=false /v:minimal /nologo /nr:false *> (Join-Path $draftOutput $log)
    if ($LASTEXITCODE) { Get-Content (Join-Path $draftOutput $log) -Tail 12; throw 'Draft QA build failed.' }
    [xml]$draftRecipe=Get-Content (Join-Path $draftApp 'bin/x64/Debug-CppWinRT/LitePdfViewer.build.appxrecipe')
    $draftLayout=[IO.Path]::GetFullPath((Join-Path $draftApp 'bin/x64/Debug-CppWinRT/AppX'))
    foreach ($draftItem in $draftRecipe.SelectNodes("//*[local-name()='AppxPackagedFile']")) {
        $draftDestination=[IO.Path]::GetFullPath((Join-Path $draftLayout $draftItem.PackagePath))
        if (!$draftDestination.StartsWith($draftLayout+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected layout path.' }
        New-Item (Split-Path $draftDestination) -ItemType Directory -Force | Out-Null
        Copy-Item -LiteralPath ([Uri]::UnescapeDataString($draftItem.Include)) -Destination $draftDestination -Force
    }
    Add-AppxPackage -Register (Join-Path $draftLayout 'AppxManifest.xml') -DisableDevelopmentMode
}
function Stop-DraftQa {
    if (!$script:draftPid) { return }
    $draftProcess=Get-Process -Id $script:draftPid -ErrorAction SilentlyContinue
    $draftExpected=[IO.Path]::GetFullPath((Join-Path $draftApp 'bin/x64/Debug-CppWinRT/AppX/LitePdfViewer.exe'))
    if ($draftProcess -and $draftProcess.Path -eq $draftExpected) {
        Stop-Process -Id $script:draftPid
        $draftProcess.WaitForExit(10000) | Out-Null
    }
    $script:draftPid=$null
}
try {
    foreach ($draftName in @('App.xaml.cs','LitePdfViewer.Shared.projitems')) { $draftBackups[$draftName]=[IO.File]::ReadAllBytes((Join-Path $draftApp $draftName)) }
    $draftAppSource=[IO.File]::ReadAllText((Join-Path $draftApp 'App.xaml.cs'))
    $draftMarker='            Window.Current.Activate();'
    if (!$draftAppSource.Contains($draftMarker)) { throw 'Launch hook changed.' }
    $draftHook='            if (launchArgs == "draft-write-qa" || launchArgs == "draft-read-qa") { page.RunDraftRecoveryQa(launchArgs == "draft-write-qa"); return; }'
    [IO.File]::WriteAllText((Join-Path $draftApp 'App.xaml.cs'),$draftAppSource.Replace($draftMarker,$draftMarker+"`r`n"+$draftHook))
    $draftShared=[IO.File]::ReadAllText((Join-Path $draftApp 'LitePdfViewer.Shared.projitems'))
    [IO.File]::WriteAllText((Join-Path $draftApp 'LitePdfViewer.Shared.projitems'),$draftShared.Replace('<Compile Include="L.cs" />','<Compile Include="L.cs" />'+"`r`n    <Compile Include=`"..\tests\DraftRecoveryQa.cs`" />"))
    Build-DraftQa 'qa-build.log'
    Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $draftRepo 'tests/ReaderQaActivation.cs')))
    foreach ($draftPhase in @('write','read')) {
        $draftReportPath=Join-Path $draftState "$draftPhase-report.txt"
        [IO.File]::WriteAllText($draftReportPath,'')
        $script:draftPid=[ReaderQaActivation]::Launch("draft-$draftPhase-qa")
        $draftDeadline=[DateTime]::UtcNow.AddMinutes(3)
        do { Start-Sleep -Milliseconds 500; $draftReport=[IO.File]::ReadAllText($draftReportPath) } while (!$draftReport -and [DateTime]::UtcNow -lt $draftDeadline)
        Copy-Item -LiteralPath $draftReportPath -Destination (Join-Path $draftOutput "$draftPhase-report.txt") -Force
        $draftReport | Write-Output
        if ($draftReport -match 'FAIL:' -or ($draftPhase -eq 'write' -and $draftReport -notmatch 'READY:') -or ($draftPhase -eq 'read' -and $draftReport -notmatch 'PASS: all draft recovery QA completed')) { throw "Draft $draftPhase QA failed." }
        Stop-DraftQa
    }
} finally {
    Stop-DraftQa
    foreach ($draftName in $draftBackups.Keys) { [IO.File]::WriteAllBytes((Join-Path $draftApp $draftName),$draftBackups[$draftName]); (Get-Item (Join-Path $draftApp $draftName)).LastWriteTimeUtc=[DateTime]::UtcNow }
    Build-DraftQa 'production-build.log'
}
