# In-process native UWP tests; no desktop input or UI automation.
$ErrorActionPreference='Stop'
$editingRepo=Split-Path $PSScriptRoot -Parent
$editingApp=Join-Path $editingRepo 'LitePdfViewer'
$editingOutput=Join-Path $editingRepo 'artifacts/reader-editing-qa'
$editingState=Join-Path $env:LOCALAPPDATA 'Packages/Ben.LitePdfViewerPreview_z6xh1x1kg5n3j/LocalState/reader-editing-qa'
if (-not (Get-AppxPackage Ben.LitePdfViewerPreview)) { throw 'Deploy the x64 Debug app from Visual Studio first.' }
if (Get-Process LitePdfViewer -ErrorAction SilentlyContinue) { throw 'Close the viewer first; an existing session will not be interrupted.' }
New-Item $editingOutput -ItemType Directory -Force | Out-Null
New-Item $editingState -ItemType Directory -Force | Out-Null
Add-Type -AssemblyName System.Drawing
$editingBitmap=New-Object Drawing.Bitmap 1000,1400
$editingGraphics=[Drawing.Graphics]::FromImage($editingBitmap)
$editingFont=New-Object Drawing.Font 'Segoe UI',44
try {
    $editingGraphics.Clear([Drawing.Color]::White)
    $editingGraphics.DrawString('Windows OCR recognition',$editingFont,[Drawing.Brushes]::Black,55,150)
    $editingGraphics.DrawString('Needle search selection',$editingFont,[Drawing.Brushes]::Black,55,250)
    $editingGraphics.DrawString('Move ink and restore saves',$editingFont,[Drawing.Brushes]::Black,55,350)
    $editingBitmap.Save((Join-Path $editingOutput 'scanned.jpg'),[Drawing.Imaging.ImageFormat]::Jpeg)
} finally { $editingFont.Dispose(); $editingGraphics.Dispose(); $editingBitmap.Dispose() }
& python (Join-Path $editingRepo 'tests/GenerateReaderEditingFixtures.py') $editingOutput
if ($LASTEXITCODE) { throw 'Fixture generation failed.' }
foreach ($editingName in @('source.pdf','scanned.pdf','selection.pdf')) { Copy-Item -LiteralPath (Join-Path $editingOutput $editingName) -Destination (Join-Path $editingState $editingName) -Force }
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
    $editingMarker='            Window.Current.Activate();'
    if (-not $editingAppCode.Contains($editingMarker)) { throw 'Launch hook changed.' }
    [IO.File]::WriteAllText((Join-Path $editingApp 'App.xaml.cs'),$editingAppCode.Replace($editingMarker,$editingMarker+"`r`n            if (launchArgs == `"reader-editing-qa`") { page.RunReaderEditingQa(); return; }"))
    $editingShared=[IO.File]::ReadAllText((Join-Path $editingApp 'LitePdfViewer.Shared.projitems'))
    [IO.File]::WriteAllText((Join-Path $editingApp 'LitePdfViewer.Shared.projitems'),$editingShared.Replace('<Compile Include="L.cs" />','<Compile Include="L.cs" />'+"`r`n    <Compile Include=`"..\tests\ReaderEditingQa.cs`" />"))
    Invoke-EditingBuild 'build.log'; Sync-EditingLayout
    $editingReportPath=Join-Path $editingState 'report.txt'
    [IO.File]::WriteAllText($editingReportPath,'')
    Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $editingRepo 'tests/ReaderQaActivation.cs')))
    $editingStartedPid=[ReaderQaActivation]::Launch()
    $editingDeadline=[DateTime]::UtcNow.AddMinutes(4)
    do { Start-Sleep -Milliseconds 500; $editingReport=[IO.File]::ReadAllText($editingReportPath) } while (-not $editingReport -and [DateTime]::UtcNow -lt $editingDeadline)
    $editingReport | Write-Output
    Copy-Item -LiteralPath $editingReportPath -Destination (Join-Path $editingOutput 'report.txt') -Force
    if ($editingReport -notmatch 'PASS: all reader editing QA completed' -or $editingReport -match 'FAIL:') { throw 'Native reader editing QA failed.' }
} finally {
    if ($editingStartedPid) {
        $editingProcess=Get-Process -Id $editingStartedPid -ErrorAction SilentlyContinue
        $editingExpectedPath=[IO.Path]::GetFullPath((Join-Path $editingApp 'bin/x64/Debug-CppWinRT/AppX/LitePdfViewer.exe'))
        if ($editingProcess -and $editingProcess.Path -eq $editingExpectedPath) { Stop-Process -Id $editingStartedPid; $editingProcess.WaitForExit(10000) | Out-Null }
    }
    foreach ($editingName in $editingBackups.Keys) { [IO.File]::WriteAllBytes((Join-Path $editingApp $editingName),$editingBackups[$editingName]); (Get-Item (Join-Path $editingApp $editingName)).LastWriteTimeUtc=[DateTime]::UtcNow }
    Invoke-EditingBuild 'production-build.log'; Sync-EditingLayout
}
