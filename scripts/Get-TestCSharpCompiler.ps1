$ErrorActionPreference = 'Stop'
if ($env:UWPDF_CSC_PATH) {
    if (!(Test-Path -LiteralPath $env:UWPDF_CSC_PATH -PathType Leaf)) { throw 'UWPDF_CSC_PATH does not exist.' }
    return $env:UWPDF_CSC_PATH
}
$legacyCompiler = 'C:/Program Files (x86)/MSBuild/14.0/Bin/csc.exe'
if (Test-Path -LiteralPath $legacyCompiler) { return $legacyCompiler }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (Test-Path -LiteralPath $vswhere) {
    $compiler = @(& $vswhere -latest -products '*' -find 'MSBuild/**/Bin/Roslyn/csc.exe') | Select-Object -First 1
    if ($compiler) { return $compiler }
}
throw 'Install Visual Studio C# build tools or set UWPDF_CSC_PATH to a desktop Roslyn csc.exe.'
