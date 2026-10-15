param([switch]$Offline)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$sourceRoot = Join-Path $repoRoot 'artifacts/mupdf-source'
$generatedRoot = Join-Path $repoRoot 'artifacts/native-dependencies'
$revision = '8ad45e92f0935d3d87f1db3f873086472a5e1b24' # MuPDF 1.28.5
if (!(Test-Path (Join-Path $sourceRoot '.git'))) {
    if ($Offline) { throw 'Pinned MuPDF checkout is missing.' }
    & git clone --depth 1 --branch 1.28.5 https://github.com/ArtifexSoftware/mupdf.git $sourceRoot
    if ($LASTEXITCODE) { throw 'MuPDF clone failed.' }
}
$actualRevision = & git -C $sourceRoot rev-parse HEAD
if ($actualRevision -ne $revision) { throw "Unexpected MuPDF revision: $actualRevision" }
if (!$Offline) {
    & git -C $sourceRoot submodule update --init --depth 1 thirdparty/freetype thirdparty/jbig2dec thirdparty/libjpeg thirdparty/zlib
    if ($LASTEXITCODE) { throw 'MuPDF dependency restore failed.' }
}
New-Item -ItemType Directory -Path $generatedRoot -Force | Out-Null
# Keep the pinned checkout reproducible. These small compatibility edits avoid
# APIs excluded from the AppContainer CRT and an unsigned-negation error in v140.
$patches = @{
    'source/fitz/subset-cff.c' = @('-((b0-251)<<8)', '-((int)(b0-251)<<8)')
    'source/fitz/log.c' = @('getenv(text)', 'NULL', 'getenv("FZ_LOG_FILE")', 'NULL')
    'source/fitz/directory.c' = @('FindFirstFileW(wpath, &dw)', 'FindFirstFileExW(wpath, FindExInfoBasic, &dw, FindExSearchNameMatch, NULL, 0)')
    'source/pdf/pdf-clean-file.c' = @('pdf_obj *last;', 'pdf_obj *last = NULL;')
    'source/pdf/pdf-lex.c' = @('neg ? -i : i', 'neg ? (0ULL - i) : i')
    'source/fitz/random.c' = @('#include <windows.h> // for GetSystemTime and CryptGenRandom', ('#include <windows.h>' + "`n" + '#include <bcrypt.h>'))
    'scripts/freetype/slimftoptions.h' = @('#include <freetype/config/ftoption.h>', ('#include <freetype/config/ftoption.h>' + "`n" + '#undef FT_CONFIG_OPTION_ENVIRONMENT_PROPERTIES'))
    'source/pdf/pdf-op-run.c' = @(("if (image == NULL)`n`t`treturn;"), @'
if (image == NULL)
	{
		/* uwPDF text-only hint: preserve ActualText image bounds without
		 * loading an image or its codec. No image pixels are retained. */
		if (pr->dev->hints & 0x10000)
		{
			fz_image placement = { 0 };
			placement.w = placement.h = 1;
			placement.n = 3;
			placement.bpc = 8;
			placement.colorspace = fz_device_rgb(ctx);
			pop_any_pending_mcid_changes(ctx, pr);
			flush_begin_layer(ctx, pr);
			image_ctm = fz_pre_scale(fz_pre_translate(gstate->ctm, 0, 1), 1, -1);
			/* The hinted text device must not preserve images. This stack
			 * metadata is consumed synchronously and never retained. */
			fz_fill_image(ctx, pr->dev, &placement, image_ctm, gstate->fill.alpha, gstate->fill.color_params);
		}
		return;
	}
'@)
}
foreach ($relative in $patches.Keys) {
    $original = (& git -C $sourceRoot show ($revision + ':' + $relative)) -join "`n"
    if ($LASTEXITCODE) { throw "Cannot read pinned source: $relative" }
    $pairs = $patches[$relative]
    if ($pairs.Count % 2) { throw "Invalid patch pairs: $relative" }
    for ($i = 0; $i -lt $pairs.Count; $i += 2) {
        if (!$pairs[$i] -or !$original.Contains($pairs[$i])) { throw "Patch anchor not found: $relative" }
        $original = $original.Replace($pairs[$i], $pairs[$i + 1])
    }
    if ($relative -eq 'source/fitz/random.c') {
        $original = [regex]::Replace($original, 'HCRYPTPROV prov;[\s\S]*?return !ok;', 'return BCryptGenRandom(NULL, entropy, (ULONG)len, BCRYPT_USE_SYSTEM_PREFERRED_RNG) < 0;')
    }
    $target = Join-Path $sourceRoot $relative
    if ([IO.File]::ReadAllText($target) -ne $original) {
        [IO.File]::WriteAllText($target, $original, [Text.UTF8Encoding]::new($false))
    }
}
$fontRoot = Join-Path $generatedRoot 'fonts'
New-Item -ItemType Directory -Path $fontRoot -Force | Out-Null
$fonts = @(Get-ChildItem (Join-Path $sourceRoot 'resources/fonts/urw') -Filter '*.cff' | Where-Object Name -ne 'NimbusBoxes-Regular.cff')
$fonts += Get-Item (Join-Path $sourceRoot 'resources/fonts/droid/DroidSansFallback.ttf')
foreach ($font in $fonts) {
    $symbol = '_binary_' + ($font.Name -replace '[^a-zA-Z0-9]', '_')
    $output = Join-Path $fontRoot ($symbol + '.c')
    if (Test-Path $output) { continue }
    $bytes = [IO.File]::ReadAllBytes($font.FullName)
    $writer = [IO.StreamWriter]::new($output, $false, [Text.Encoding]::ASCII)
    try {
        $writer.WriteLine('/* Generated from MuPDF bundled fonts. See THIRD-PARTY.md for licenses. */')
        $writer.WriteLine('const unsigned char ' + $symbol + '[] = {')
        for ($offset = 0; $offset -lt $bytes.Length; $offset += 128) {
            $end = [Math]::Min($offset + 127, $bytes.Length - 1)
            $writer.WriteLine([String]::Join(',', [string[]]$bytes[$offset..$end]) + ',')
        }
        $writer.WriteLine('};')
        $writer.WriteLine('const unsigned int ' + $symbol + '_size = ' + $bytes.Length + ';')
    } finally { $writer.Dispose() }
}
$paths = [Collections.Generic.List[string]]::new()
foreach ($projectName in @('libmupdf', 'libthirdparty')) {
    [xml]$project = Get-Content (Join-Path $sourceRoot "platform/win32/$projectName.vcxproj")
    foreach ($item in $project.Project.ItemGroup.ClCompile) {
        $relative = [string]$item.Include
        if (!$relative) { continue }
        if ($projectName -eq 'libmupdf' -and $relative -notmatch '\\source\\(fitz|pdf)\\') { continue }
        if ($projectName -eq 'libthirdparty' -and $relative -notmatch '\\thirdparty\\(freetype|jbig2dec|libjpeg|zlib)\\') { continue }
        $paths.Add([IO.Path]::GetFullPath((Join-Path (Join-Path $sourceRoot 'platform/win32') $relative)))
    }
}
$paths.AddRange([string[]](Get-ChildItem $fontRoot -Filter '*.c' | ForEach-Object FullName))
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003"><ItemGroup>')
foreach ($path in $paths) {
    $relative = $path.Substring($repoRoot.Length + 1).Replace('/', '\')
    $objectName = $relative -replace '[^a-zA-Z0-9]', '_'
    $lines.Add('  <ClCompile Include="$(ProjectDir)..\' + $relative + '"><ObjectFileName>$(IntDir)' + $objectName + '.obj</ObjectFileName></ClCompile>')
}
$lines.Add('</ItemGroup></Project>')
[IO.File]::WriteAllLines((Join-Path $generatedRoot 'MuPdfSources.props'), $lines)
$licenseRoot = Join-Path $generatedRoot 'licenses'
New-Item -ItemType Directory -Path $licenseRoot -Force | Out-Null
$licenses = @{
    'MuPDF-AGPL.txt' = 'COPYING'
    'FreeType.txt' = 'thirdparty/freetype/docs/FTL.TXT'
    'jbig2dec-AGPL.txt' = 'thirdparty/jbig2dec/COPYING'
    'jbig2dec-notice.txt' = 'thirdparty/jbig2dec/LICENSE'
    'JPEG.txt' = 'thirdparty/libjpeg/README'
    'zlib.txt' = 'thirdparty/zlib/LICENSE'
    'URW-fonts-OFL.txt' = 'resources/fonts/urw/OFL.txt'
    'Droid-fonts-NOTICE.txt' = 'resources/fonts/droid/NOTICE'
}
foreach ($name in $licenses.Keys) { Copy-Item -LiteralPath (Join-Path $sourceRoot $licenses[$name]) -Destination (Join-Path $licenseRoot $name) -Force }
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD-PARTY.md') -Destination (Join-Path $licenseRoot 'THIRD-PARTY.md') -Force
Write-Output "Prepared MuPDF 1.28.5: $($paths.Count) C sources, $($fonts.Count) fallback fonts."
