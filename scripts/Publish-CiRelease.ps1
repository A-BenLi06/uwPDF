param([Parameter(Mandatory = $true)][string]$Tag)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Test-ReleaseSource.ps1') -Tag $Tag
$output = Join-Path $repoRoot 'artifacts/ci/release'
& (Join-Path $PSScriptRoot 'Test-ReleaseAssets.ps1') -Directory $output
$info = Get-Content (Join-Path $output 'build-info.json') -Raw | ConvertFrom-Json
$headCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -or $info.commit -ne $headCommit -or "v$($info.version)" -ne $Tag) { throw 'Release provenance differs from source/tag.' }
if (!$env:GITHUB_REPOSITORY) { throw 'GITHUB_REPOSITORY is required.' }
$repository = $env:GITHUB_REPOSITORY
function Assert-RemoteTag {
    $remoteRef = (& gh api "repos/$repository/git/ref/tags/$Tag" | ConvertFrom-Json).object
    if ($LASTEXITCODE) { throw 'Cannot verify remote release tag.' }
    for ($depth = 0; $remoteRef.type -eq 'tag' -and $depth -lt 8; $depth++) {
        $remoteRef = (& gh api "repos/$repository/git/tags/$($remoteRef.sha)" | ConvertFrom-Json).object
        if ($LASTEXITCODE) { throw 'Cannot resolve annotated release tag.' }
    }
    if ($remoteRef.type -ne 'commit' -or $remoteRef.sha -ne $info.commit) { throw 'Remote tag no longer matches the build commit.' }
}
# Check both before upload and immediately before publication (annotated tags included).
Assert-RemoteTag
$assets = @(Get-ChildItem -LiteralPath $output -File | ForEach-Object FullName)
& gh release create $Tag --repo $repository --verify-tag --draft --title "uwPDF $($info.version)" `
    --notes-file (Join-Path $repoRoot 'artifacts/ci/release-notes.md') @assets
if ($LASTEXITCODE) { throw 'Draft release creation failed. Existing releases are never overwritten; inspect any existing draft before retrying.' }
$download = Join-Path $repoRoot "artifacts/ci/download-$([guid]::NewGuid().ToString('N'))"
& gh release download $Tag --repo $repository --dir $download
if ($LASTEXITCODE) { throw 'Release download verification failed; release remains a draft.' }
& (Join-Path $PSScriptRoot 'Test-ReleaseAssets.ps1') -Directory $download
foreach ($asset in $assets) {
    $downloaded = Join-Path $download ([IO.Path]::GetFileName($asset))
    if ((Get-FileHash -LiteralPath $downloaded).Hash -ne (Get-FileHash -LiteralPath $asset).Hash) { throw 'Uploaded asset differs from local output; release remains a draft.' }
}
Assert-RemoteTag
& gh release edit $Tag --repo $repository --draft=false
if ($LASTEXITCODE) { throw 'Release publication failed.' }
