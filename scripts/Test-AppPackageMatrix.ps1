param([string]$ReportPath='artifacts/text-winrt/package-checks.json')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName System.IO.Compression.FileSystem
$results=@()
foreach ($platform in @('x86','x64','ARM','ARM64')) {
    foreach ($configuration in @('Debug','Release')) {
        $tag=if ($configuration -eq 'Debug') { '_Debug' } else { '' }
        if ($platform -eq 'ARM64') {
            $stem="LitePdfViewer.ARM64_1.0.8.0_ARM64$tag"
            $package=Join-Path $repoRoot "LitePdfViewer/AppPackages/ARM64/${stem}_Test/$stem.msix"
            & (Join-Path $PSScriptRoot 'Test-UwpArm64Package.ps1') -PackagePath $package -Configuration $configuration
        } else {
            $stem="LitePdfViewer_1.0.8.0_$platform$tag"
            $package=Join-Path $repoRoot "LitePdfViewer/AppPackages/CppWinRT/${stem}_Test/$stem.appx"
            & (Join-Path $PSScriptRoot 'Test-PackageBaseline.ps1') -PackagePath $package
            & (Join-Path $PSScriptRoot 'Test-CppWinRTPackage.ps1') -PackagePath $package -Platform $platform -Configuration $configuration
        }
        $archive=[IO.Compression.ZipFile]::OpenRead($package)
        try {
            foreach ($license in (Get-ChildItem -LiteralPath (Join-Path $repoRoot 'artifacts/native-dependencies/licenses') -File)) {
                $entry=$archive.GetEntry("ThirdPartyLicenses/$($license.Name)")
                if (!$entry) { throw "Missing packaged license: $($license.Name)" }
                $sha=[Security.Cryptography.SHA256]::Create(); $stream=$entry.Open()
                try { $hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
                finally { $sha.Dispose(); $stream.Dispose() }
                if ($hash -ne (Get-FileHash -LiteralPath $license.FullName).Hash) { throw "Stale packaged license: $($license.Name)" }
            }
            $component=$archive.GetEntry('PdfNative.Rendering.dll')
            $results += [pscustomobject]@{platform=$platform;configuration=$configuration;package_path=$package
                package_sha256=(Get-FileHash -LiteralPath $package).Hash;package_bytes=(Get-Item -LiteralPath $package).Length
                component_bytes=$component.Length;legacy_bridge_absent=$true;native_classes=6;licenses_verified=$true}
        } finally { $archive.Dispose() }
    }
}
$sources=@(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'LitePdfViewer') -File | Where-Object { $_.Extension -in @('.cs','.xaml','.csproj','.projitems','.appxmanifest') })
$sources+=@(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'PdfNative') -File | Where-Object { $_.Extension -in @('.h','.cpp','.vcxproj') })
$sources+=@(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'PdfNative/WinRT') -File)
$sources+=@(Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object { $_.Name -match '^(Build-CppWinRT|Build-UwpArm64|Test-(CppWinRTBinary|CppWinRTPackage|UwpArm64Package|PackageBaseline|AppPackageMatrix))' })
$hashes=[ordered]@{}
foreach ($source in ($sources | Sort-Object FullName -Unique)) { $hashes[$source.FullName.Substring($repoRoot.Length+1).Replace('\','/')]=(Get-FileHash -LiteralPath $source.FullName).Hash }
$report=Join-Path $repoRoot $ReportPath
New-Item -ItemType Directory -Path (Split-Path $report -Parent) -Force | Out-Null
[pscustomobject]@{completed_utc=[DateTime]::UtcNow.ToString('o');source_sha256=$hashes;packages=$results
    scope='Eight actual default C++/WinRT app packages: OS baselines, architecture, current native source/engine/DLL hashes, all six registrations, no legacy bridge, exact licenses; ARM64 native payload/.NET Native dependencies. Source snapshot accompanies package evidence; excludes app launch, XAML input/frame latency, total process memory and old-device runtime'} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $report -Encoding UTF8
Write-Output "PASS: all eight default C++/WinRT application packages. Evidence: $report"
