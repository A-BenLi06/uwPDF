param([Parameter(Mandatory = $true)][string]$Tag)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    if ($Tag -notmatch '^v(\d+\.\d+\.\d+\.\d+)$') { throw 'Release tags must use vMajor.Minor.Build.Revision.' }
    $version = $Matches[1]
    foreach ($name in @('Package.appxmanifest', 'Package.ARM64.appxmanifest')) {
        [xml]$manifest = Get-Content (Join-Path $repoRoot "LitePdfViewer/$name")
        if ($manifest.Package.Identity.Version -ne $version) { throw "$name does not match $Tag." }
    }
    $headCommit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE) { throw 'Cannot resolve HEAD.' }
    $tagCommit = (& git rev-parse "refs/tags/$Tag^{commit}").Trim()
    if ($LASTEXITCODE -or $tagCommit -ne $headCommit) { throw 'Tag does not point to the source being built.' }
    & git merge-base --is-ancestor $headCommit refs/remotes/origin/main
    if ($LASTEXITCODE) { throw 'Release source must belong to origin/main.' }
    $notes = Join-Path $repoRoot "docs/releases/$Tag.md"
    if (!(Test-Path -LiteralPath $notes) -or [string]::IsNullOrWhiteSpace((Get-Content -LiteralPath $notes -Raw))) {
        throw "Write user-facing release notes at docs/releases/$Tag.md before tagging."
    }
    Write-Output "Validated $Tag at $headCommit."
} finally { Pop-Location }
