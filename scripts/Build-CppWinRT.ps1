param(
    [ValidateSet('x86', 'x64', 'ARM', 'ARM64')][string]$Platform = 'x64',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$SdkVersion = '10.0.26100.0',
    [string]$ToolchainRoot,
    [string]$ProjectionSdkVersion,
    [switch]$Incremental
)
$ErrorActionPreference = 'Stop'
function Get-FileSha256([string]$Path) {
    # MSBuild 14 can invoke a PowerShell environment without Get-FileHash.
    # Hashing through .NET keeps build records independent of module discovery.
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $sha.Dispose(); $stream.Dispose() }
}
$repoRoot = Split-Path $PSScriptRoot -Parent
if ($Platform -eq 'ARM' -and !$PSBoundParameters.ContainsKey('SdkVersion')) { $SdkVersion = '10.0.14393.0' }
if (!$ProjectionSdkVersion) { $ProjectionSdkVersion = if ($Platform -eq 'ARM') { '10.0.26100.0' } else { $SdkVersion } }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if ($ToolchainRoot) {
    $vcRoot = (Resolve-Path -LiteralPath $ToolchainRoot).Path
} else {
    # The latest IDE may lack this architecture while another instance has it.
    $vsRoots = @(& $vswhere -all -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)
    $vcRoot = $null
    foreach ($vsRoot in $vsRoots) {
        $compilerVersion = (Get-Content (Join-Path $vsRoot 'VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt') -Raw).Trim()
        $candidate = Join-Path $vsRoot "VC/Tools/MSVC/$compilerVersion"
        if ((Test-Path -LiteralPath "$candidate/bin/Hostx64/$Platform/cl.exe") -and
            (Test-Path -LiteralPath "$candidate/lib/onecore/$Platform/libcpmt.lib")) { $vcRoot = $candidate; break }
    }
    $localManifest = Join-Path $repoRoot 'artifacts/arm64-tools/toolchain.json'
    if (!$vcRoot -and $Platform -eq 'ARM64' -and (Test-Path -LiteralPath $localManifest)) {
        $vcRoot = (Get-Content -LiteralPath $localManifest -Raw | ConvertFrom-Json).toolchain_root
    }
    $arm32Manifest = Join-Path $repoRoot 'artifacts/arm32-tools/toolchain.json'
    if (!$vcRoot -and $Platform -eq 'ARM' -and (Test-Path -LiteralPath $arm32Manifest)) {
        $vcRoot = (Get-Content -LiteralPath $arm32Manifest -Raw | ConvertFrom-Json).toolchain_root
    }
    if (!$vcRoot) { throw "Install MSVC $Platform tools or prepare its local overlay with scripts/Prepare-CppWinRTArm32.ps1 or scripts/Prepare-CppWinRTArm64.ps1." }
}
$compilerRoot = Join-Path $vcRoot "bin/Hostx64/$Platform"
$compiler = Join-Path $compilerRoot 'cl.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw "The installed MSVC toolchain has no $Platform compiler. Install its architecture tools before building this component." }
if (!(Test-Path -LiteralPath "$vcRoot/lib/onecore/$Platform/libcpmt.lib")) { throw "The toolchain has no static OneCore CRT for $Platform." }
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
$sdkBin = Join-Path $sdkRoot "bin/$ProjectionSdkVersion/x64"
$sdkInclude = Join-Path $sdkRoot "Include/$SdkVersion"
$projectionInclude = Join-Path $sdkRoot "Include/$ProjectionSdkVersion"
$metadata = Join-Path $sdkRoot "UnionMetadata/$ProjectionSdkVersion"
$cppwinrtTool = Join-Path $sdkBin 'cppwinrt.exe'
if ($Platform -eq 'ARM') {
    $arm32Tools = Get-Content -LiteralPath (Join-Path $repoRoot 'artifacts/arm32-tools/toolchain.json') -Raw | ConvertFrom-Json
    $cppwinrtTool = $arm32Tools.cppwinrt_path
    if (!$cppwinrtTool -or (Get-FileSha256 $cppwinrtTool) -ne $arm32Tools.cppwinrt_sha256) { throw 'Prepare the pinned ARM32 C++/WinRT generator with scripts/Prepare-CppWinRTArm32.ps1.' }
}
foreach ($required in @("$sdkRoot/Lib/$SdkVersion/um/$Platform/windows.data.pdf.lib", "$sdkRoot/Lib/$SdkVersion/ucrt/$Platform/ucrt.lib",
    $cppwinrtTool, "$metadata/Windows.winmd", "$projectionInclude/cppwinrt/winrt/base.h")) {
    if (!(Test-Path -LiteralPath $required)) { throw "Missing native SDK or projection dependency: $required" }
}
$componentRoot = Join-Path $repoRoot 'PdfNative/WinRT'
$outputRoot = Join-Path $repoRoot "artifacts/cppwinrt/$Platform/$Configuration"
$generated = Join-Path $outputRoot 'Generated'
New-Item -ItemType Directory -Path $generated -Force | Out-Null
$savedInclude = $env:INCLUDE; $savedLib = $env:LIB; $savedPath = $env:PATH
try {
    $env:PATH = "$compilerRoot;$sdkBin;$savedPath"
    # Current STL needs current UCRT declarations, and current WRL fixes template
    # parsing in the 14393 headers. Actual Win32/PDF declarations and all linked
    # UCRT/UM libraries still come from the chosen native SDK.
    $supportInclude = if ($Platform -eq 'ARM') { $projectionInclude } else { $sdkInclude }
    $env:INCLUDE = "$vcRoot/include;$supportInclude/ucrt;$sdkInclude/shared;$sdkInclude/um;$supportInclude/winrt;$projectionInclude/cppwinrt"
    # Static OneCore CRT avoids a second architecture-specific VCLibs package.
    $env:LIB = "$vcRoot/lib/onecore/$Platform;$sdkRoot/Lib/$SdkVersion/ucrt/$Platform;$sdkRoot/Lib/$SdkVersion/um/$Platform"
    & (Join-Path $PSScriptRoot 'Build-CppWinRTDocumentEngine.ps1') -Platform $Platform -Configuration $Configuration -ToolchainRoot $vcRoot -SdkVersion $SdkVersion
    $sourceHashes = [ordered]@{}
    foreach ($relative in @('scripts/Build-CppWinRT.ps1', 'PdfNative/PdfRenderDevice.h', 'PdfNative/PdfRenderDevice.cpp',
        'PdfNative/WinRT/PdfNative.Rendering.idl', 'PdfNative/WinRT/PdfSurfaceRenderer.h',
        'PdfNative/WinRT/PdfSurfaceRenderer.cpp', 'PdfNative/WinRT/Exports.def',
        'PdfNative/InkOutlineCore.h', 'PdfNative/InkOutlineCore.cpp',
        'PdfNative/WinRT/InkOutlineExporter.h', 'PdfNative/WinRT/InkOutlineExporter.cpp',
        'PdfNative/PreviewDisplayCore.h', 'PdfNative/WinRT/PreviewDisplay.h', 'PdfNative/WinRT/PreviewDisplay.cpp',
        'PdfNative/PdfDocumentCore.h', 'PdfNative/PdfDocumentCore.cpp', 'PdfNative/PdfAnnotationJson.cpp',
        'PdfNative/WinRT/PdfTextPage.h', 'PdfNative/WinRT/PdfTextDocument.h', 'PdfNative/WinRT/PdfTextDocument.cpp',
        'PdfNative/WinRT/PdfAnnotationWriter.h', 'PdfNative/WinRT/PdfAnnotationWriter.cpp', 'PdfNative/WinRT/PdfStreamWinRT.h',
        'scripts/Build-CppWinRTDocumentEngine.ps1', "artifacts/cppwinrt/$Platform/$Configuration/DocumentEngine/build-result.json")) {
        $sourceHashes[$relative] = Get-FileSha256 (Join-Path $repoRoot $relative)
    }
    if ($Platform -eq 'ARM') {
        $sourceHashes['scripts/Prepare-CppWinRTArm32.ps1'] = Get-FileSha256 (Join-Path $repoRoot 'scripts/Prepare-CppWinRTArm32.ps1')
    }
    $compilerHash = Get-FileSha256 $compiler
    $generatorHash = Get-FileSha256 $cppwinrtTool
    $referenceHash = Get-FileSha256 (Join-Path $metadata 'Windows.winmd')
    $recordPath = Join-Path $outputRoot 'build-result.json'
    if ($Incremental -and (Test-Path -LiteralPath $recordPath)) {
        $old = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
        $fresh = $old.platform -eq $Platform -and $old.configuration -eq $Configuration -and
            $old.sdk_version -eq $SdkVersion -and $old.projection_sdk_version -eq $ProjectionSdkVersion -and
            $old.compiler_sha256 -eq $compilerHash -and $old.cppwinrt_tool_sha256 -eq $generatorHash -and
            $old.reference_metadata_sha256 -eq $referenceHash -and
            @($old.source_sha256.PSObject.Properties).Count -eq $sourceHashes.Count -and
            (Test-Path -LiteralPath "$outputRoot/PdfNative.Rendering.dll") -and (Test-Path -LiteralPath "$outputRoot/PdfNative.Rendering.winmd")
        if ($fresh) { foreach ($key in $sourceHashes.Keys) { if ($old.source_sha256.$key -ne $sourceHashes[$key]) { $fresh = $false; break } } }
        if ($fresh) { $fresh = $old.component_sha256 -eq (Get-FileSha256 "$outputRoot/PdfNative.Rendering.dll") -and $old.metadata_sha256 -eq (Get-FileSha256 "$outputRoot/PdfNative.Rendering.winmd") }
        if ($fresh) { Write-Output "Current C++/WinRT component: $Platform/$Configuration"; return }
    }
    $midlPlatform = if ($Platform -eq 'x86') { 'win32' } elseif ($Platform -eq 'ARM') { 'arm32' } else { $Platform.ToLowerInvariant() }
    & (Join-Path $sdkBin 'midl.exe') /nologo /winrt /nomidl /env $midlPlatform /metadata_dir $metadata /reference (Join-Path $metadata 'Windows.winmd') /I "$projectionInclude/winrt" /I "$projectionInclude/um" /out $generated /h PdfNative.Rendering.abi.h /winmd (Join-Path $outputRoot 'PdfNative.Rendering.winmd') (Join-Path $componentRoot 'PdfNative.Rendering.idl')
    if ($LASTEXITCODE) { throw 'C++/WinRT metadata generation failed.' }
    if ($Platform -eq 'ARM') {
        # Base and Windows projections must come from the same ARM32-capable
        # generator; mixing them with SDK 26100 headers is unsupported.
        & $cppwinrtTool -input $metadata -output $generated -base
        if ($LASTEXITCODE) { throw 'ARM32 Windows projection generation failed.' }
    }
    $projectionOptions = @()
    if ($Platform -eq 'ARM') { $projectionOptions += '-base' }
    & $cppwinrtTool -input (Join-Path $outputRoot 'PdfNative.Rendering.winmd') -reference $metadata -output $generated -component -pch . @projectionOptions
    if ($LASTEXITCODE) { throw 'C++/WinRT projection generation failed.' }
    $optimization = if ($Configuration -eq 'Debug') { @('/Od', '/Zi', '/MTd') } else { @('/O2', '/DNDEBUG', '/MT') }
    # MSBuild invokes architecture builds from the same working directory.
    # Keep the compiler PDB beside its objects instead of sharing vc140.pdb.
    & $compiler /nologo /std:c++20 /EHsc /W4 /external:anglebrackets /external:W0 /permissive- /bigobj /guard:cf /DWINAPI_FAMILY=WINAPI_FAMILY_APP /DNOMINMAX @optimization "/I$componentRoot" "/I$generated" "/I$repoRoot/artifacts/mupdf-source/include" "/Fo$outputRoot/" "/Fd$outputRoot/PdfNative.Rendering.compiler.pdb" "/Fe$outputRoot/PdfNative.Rendering.dll" (Join-Path $componentRoot 'PdfSurfaceRenderer.cpp') (Join-Path $componentRoot 'InkOutlineExporter.cpp') (Join-Path $componentRoot 'PreviewDisplay.cpp') (Join-Path $componentRoot 'PdfTextDocument.cpp') (Join-Path $componentRoot 'PdfAnnotationWriter.cpp') (Join-Path $repoRoot 'PdfNative/PdfRenderDevice.cpp') (Join-Path $repoRoot 'PdfNative/InkOutlineCore.cpp') (Join-Path $repoRoot 'PdfNative/PdfDocumentCore.cpp') (Join-Path $repoRoot 'PdfNative/PdfAnnotationJson.cpp') (Join-Path $generated 'module.g.cpp') /link /DLL /APPCONTAINER /SUBSYSTEM:WINDOWS /DYNAMICBASE /NXCOMPAT /OPT:REF /OPT:ICF "/DEF:$componentRoot/Exports.def" "$outputRoot/DocumentEngine/MuPdfCore.lib" windowsapp.lib windows.data.pdf.lib d3d11.lib d2d1.lib bcrypt.lib
    if ($LASTEXITCODE) { throw 'C++/WinRT component compilation failed.' }
    [pscustomobject]@{
        completed_utc = [DateTime]::UtcNow.ToString('o')
        platform = $Platform; configuration = $Configuration; sdk_version = $SdkVersion
        projection_sdk_version = $ProjectionSdkVersion
        ucrt_wrl_header_sdk_version = if ($Platform -eq 'ARM') { $ProjectionSdkVersion } else { $SdkVersion }
        cppwinrt_tool_sha256 = $generatorHash
        cppwinrt_version = if ($Platform -eq 'ARM') { $arm32Tools.cppwinrt_version } else { 'SDK bundled' }
        reference_metadata_sha256 = $referenceHash
        toolchain_root = $vcRoot
        compiler_sha256 = $compilerHash
        source_sha256 = $sourceHashes
        component_sha256 = Get-FileSha256 "$outputRoot/PdfNative.Rendering.dll"
        metadata_sha256 = Get-FileSha256 "$outputRoot/PdfNative.Rendering.winmd"
        scope = 'Cross compilation; does not establish device runtime or XAML presentation'
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'build-result.json') -Encoding UTF8
    Write-Output "Built C++/WinRT renderer: $outputRoot/PdfNative.Rendering.dll"
}
finally { $env:INCLUDE = $savedInclude; $env:LIB = $savedLib; $env:PATH = $savedPath }
