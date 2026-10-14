param([string]$SourcePath, [string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot -Parent
if (!$SourcePath) { $SourcePath=Join-Path $repoRoot 'LitePdfViewer/Branding/LogoSource.png' }
if (!$OutputDirectory) { $OutputDirectory=Join-Path $repoRoot 'LitePdfViewer/Assets' }
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$source=[Drawing.Bitmap]::FromFile((Resolve-Path -LiteralPath $SourcePath).Path)
try {
    # Ignore nearly invisible export specks when measuring the subject; retain
    # the original pixels and a bleed margin around its meaningful alpha bounds.
    $left=$source.Width; $top=$source.Height; $right=-1; $bottom=-1
    for ($y=0; $y -lt $source.Height; $y++) {
        for ($x=0; $x -lt $source.Width; $x++) {
            if ($source.GetPixel($x,$y).A -ge 32) {
                $left=[Math]::Min($left,$x); $top=[Math]::Min($top,$y)
                $right=[Math]::Max($right,$x); $bottom=[Math]::Max($bottom,$y)
            }
        }
    }
    if ($right -lt 0) { throw 'Logo contains no visible pixels.' }
    $bleed=[int][Math]::Ceiling([Math]::Max($source.Width,$source.Height)*0.01)
    $left=[Math]::Max(0,$left-$bleed); $top=[Math]::Max(0,$top-$bleed)
    $right=[Math]::Min($source.Width-1,$right+$bleed); $bottom=[Math]::Min($source.Height-1,$bottom+$bleed)
    $crop=New-Object Drawing.Rectangle $left,$top,($right-$left+1),($bottom-$top+1)
    function Write-LogoAsset([string]$Name,[int]$Width,[int]$Height,[double]$Fill) {
        $bitmap=New-Object Drawing.Bitmap $Width,$Height,([Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics=[Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode=[Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality=[Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $ratio=[Math]::Min($Width*$Fill/$crop.Width,$Height*$Fill/$crop.Height)
            $drawWidth=[single]($crop.Width*$ratio); $drawHeight=[single]($crop.Height*$ratio)
            $destination=New-Object Drawing.RectangleF ([single](($Width-$drawWidth)/2)),([single](($Height-$drawHeight)/2)),$drawWidth,$drawHeight
            $sourceBounds=New-Object Drawing.RectangleF $crop.X,$crop.Y,$crop.Width,$crop.Height
            $graphics.DrawImage($source,$destination,$sourceBounds,[Drawing.GraphicsUnit]::Pixel)
            $bitmap.Save((Join-Path $OutputDirectory $Name),[Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
    $definitions=@(
        @{Name='Square44x44Logo'; Width=44; Height=44; Fill=0.84},
        @{Name='Square150x150Logo'; Width=150; Height=150; Fill=0.72},
        @{Name='StoreLogo'; Width=50; Height=50; Fill=0.90},
        @{Name='Wide310x150Logo'; Width=310; Height=150; Fill=0.72},
        @{Name='SplashScreen'; Width=620; Height=300; Fill=0.54}
    )
    foreach ($definition in $definitions) {
        foreach ($scale in @(100,125,150,200,400)) {
            $width=[int][Math]::Ceiling($definition.Width*$scale/100.0)
            $height=[int][Math]::Ceiling($definition.Height*$scale/100.0)
            Write-LogoAsset ($definition.Name+'.scale-'+$scale+'.png') $width $height $definition.Fill
        }
    }
    foreach ($size in @(16,20,24,30,32,36,40,48,60,64,72,80,96,256)) {
        foreach ($theme in @('','_altform-unplated','_altform-lightunplated')) {
            Write-LogoAsset ('Square44x44Logo.targetsize-'+$size+$theme+'.png') $size $size 0.94
        }
    }
    # SDK-14393 limits tile PNGs to 200 KiB. Refilter/compress large PNGs
    # losslessly, retaining RGBA pixels; do not quantize this glossy artwork.
    $largeAssets=@(Get-ChildItem -LiteralPath $OutputDirectory -Filter '*.png' | Where-Object { $_.Length -gt 204800 })
    if ($largeAssets.Count -gt 0) {
        $optimizer=Get-Command ffmpeg -ErrorAction SilentlyContinue
        if (!$optimizer) { throw 'Regenerating these assets requires FFmpeg for lossless PNG compression.' }
        foreach ($asset in $largeAssets) {
            $optimizedPath=$asset.FullName+'.optimized.png'
            & $optimizer.Source -y -loglevel error -i $asset.FullName -frames:v 1 -c:v png -pred mixed -compression_level 9 -pix_fmt rgba $optimizedPath
            if ($LASTEXITCODE) { throw "PNG compression failed: $($asset.Name)" }
            Move-Item -LiteralPath $optimizedPath -Destination $asset.FullName -Force
            if ($asset.Name -ne 'SplashScreen.scale-400.png' -and (Get-Item -LiteralPath $asset.FullName).Length -gt 204800) {
                throw "Tile exceeds the 200-KiB limit: $($asset.Name)"
            }
        }
    }
    Write-Output "Generated 67 transparent PNG assets from original artwork; subject crop=$crop."
} finally { $source.Dispose() }
