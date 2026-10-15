param(
    [ValidateSet('x86','x64','ARM','ARM64')][string]$Platform,
    [ValidateSet('Debug','Release')][string]$Configuration,
    [Parameter(Mandatory=$true)][string]$ToolchainRoot,
    [Parameter(Mandatory=$true)][string]$SdkVersion
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$sourceRoot = Join-Path $repoRoot 'artifacts/mupdf-source'
$propsPath = Join-Path $repoRoot 'artifacts/native-dependencies/MuPdfSources.props'
if (!(Test-Path -LiteralPath $propsPath)) { throw 'Run Prepare-NativeDependencies.ps1 first.' }
$revision = (& git -C $sourceRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -or $revision -ne '8ad45e92f0935d3d87f1db3f873086472a5e1b24') { throw 'Unexpected document engine revision' }
$outputRoot = Join-Path $repoRoot "artifacts/cppwinrt/$Platform/$Configuration/DocumentEngine"
$wrappers = Join-Path $outputRoot 'Sources'; $objects = Join-Path $outputRoot 'Objects'
foreach ($directory in @($outputRoot,$wrappers,$objects)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
$compiler = Join-Path $ToolchainRoot "bin/Hostx64/$Platform/cl.exe"
$librarian = Join-Path $ToolchainRoot "bin/Hostx64/$Platform/lib.exe"
$library = Join-Path $outputRoot 'MuPdfCore.lib'
$recordPath = Join-Path $outputRoot 'build-result.json'
function Hash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path); $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
    finally { $sha.Dispose(); $stream.Dispose() }
}
[xml]$props = Get-Content -LiteralPath $propsPath -Raw
$sources = @($props.SelectNodes("//*[local-name()='ClCompile']") | ForEach-Object {
    [IO.Path]::GetFullPath($_.Include.Replace('$(ProjectDir)..\', "$repoRoot\"))
})
$inputs = @($sources) + @($PSCommandPath, $propsPath, (Join-Path $repoRoot 'PdfNative/MuPdfCore.vcxproj'), (Join-Path $repoRoot 'scripts/Prepare-NativeDependencies.ps1'))
foreach ($directory in @('include','source/fitz','source/pdf','scripts/freetype','scripts/libjpeg','thirdparty/freetype','thirdparty/jbig2dec','thirdparty/libjpeg','thirdparty/zlib')) {
    $inputs += @(Get-ChildItem -LiteralPath (Join-Path $sourceRoot $directory) -Filter '*.h' -File -Recurse | ForEach-Object FullName)
}
$hashes = [ordered]@{}
foreach ($inputPath in ($inputs | Sort-Object -Unique)) {
    $relative = $inputPath.Substring($repoRoot.Length + 1).Replace('\','/')
    $hashes[$relative] = Hash $inputPath
}
$compilerHash = Hash $compiler
$fresh = Test-Path -LiteralPath $recordPath
if ($fresh) {
    $old = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
    $fresh = $old.compiler_sha256 -eq $compilerHash -and $old.sdk_version -eq $SdkVersion -and
        $old.configuration -eq $Configuration -and $old.platform -eq $Platform -and
        @($old.source_sha256.PSObject.Properties).Count -eq $hashes.Count -and (Test-Path -LiteralPath $library)
    if ($fresh) { foreach ($key in $hashes.Keys) { if ($old.source_sha256.$key -ne $hashes[$key]) { $fresh = $false; break } } }
    if ($fresh) { $fresh = $old.library_sha256 -eq (Hash $library) }
}
if ($fresh) { Write-Output "Current static document engine: $Platform/$Configuration"; return }
[xml]$project = Get-Content -LiteralPath (Join-Path $repoRoot 'PdfNative/MuPdfCore.vcxproj') -Raw
$definitions = $project.SelectSingleNode("//*[local-name()='ClCompile']/*[local-name()='PreprocessorDefinitions']").InnerText.Split(';') |
    Where-Object { $_ -and $_ -ne '%(PreprocessorDefinitions)' }
$includes = @('include','scripts/freetype','scripts/libjpeg','thirdparty/freetype/include','thirdparty/jbig2dec','thirdparty/libjpeg','thirdparty/zlib') |
    ForEach-Object { '/I"' + (Join-Path $sourceRoot $_) + '"' }
$crt = if ($Configuration -eq 'Debug') { '/MTd' } else { '/MT' }
$arguments = @('/nologo','/c','/TC','/O2','/W1','/bigobj','/MP4',$crt,"/Fo`"$objects/`"") + @($includes) + @($definitions | ForEach-Object { '/D' + $_.Replace('"','\"') })
$objectPaths = @()
foreach ($source in $sources) {
    $relative = $source.Substring($repoRoot.Length + 1).Replace('\','/')
    $name = ($relative -replace '[^a-zA-Z0-9]','_')
    $wrapper = Join-Path $wrappers "$name.c"
    # Distinct names prevent object collisions; including the original .c keeps
    # its relative includes and line information. No vendor source is rewritten.
    [IO.File]::WriteAllText($wrapper, '#include "' + $source.Replace('\','/') + '"' + "`r`n", [Text.UTF8Encoding]::new($false))
    $arguments += '"' + $wrapper + '"'
    $objectPaths += Join-Path $objects "$name.obj"
}
$response = Join-Path $outputRoot 'compile.rsp'
[IO.File]::WriteAllLines($response, $arguments, [Text.UTF8Encoding]::new($false))
$compileLog = Join-Path $outputRoot 'compile.log'
& $compiler "@$response" *> $compileLog
if ($LASTEXITCODE) { Get-Content -LiteralPath $compileLog -Tail 60 | Write-Output; throw "Static document engine compilation failed: $compileLog" }
$archiveResponse = Join-Path $outputRoot 'archive.rsp'
[IO.File]::WriteAllLines($archiveResponse, @('/nologo',('/OUT:"' + $library + '"')) + @($objectPaths | ForEach-Object { '"' + $_ + '"' }), [Text.UTF8Encoding]::new($false))
& $librarian "@$archiveResponse"
if ($LASTEXITCODE) { throw 'Static document engine archive failed' }
[pscustomobject]@{completed_utc=[DateTime]::UtcNow.ToString('o');platform=$Platform;configuration=$Configuration
    sdk_version=$SdkVersion;compiler_sha256=$compilerHash;toolchain_root=$ToolchainRoot;revision=$revision
    source_sha256=$hashes;library_sha256=(Hash $library);source_count=$sources.Count;crt=$crt
    scope='Pinned auxiliary PDF parser/writer compiled as C with the component toolchain and static OneCore CRT; no C++/CX or vendor source changes introduced by this build'} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $recordPath -Encoding UTF8
Write-Output "Built static document engine: $Platform/$Configuration ($($sources.Count) C sources)"
