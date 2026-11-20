# apply-logo.ps1 - Apply the chosen root logo.png to the full MSIX asset set.
# ASCII-only (PS 5.1 parses BOM-less UTF-8 as GBK - do not add Chinese here).
# Usage: powershell -File apply-logo.ps1 -Root <repo>
param(
    [Parameter(Mandatory=$true)][string]$Root
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$logoPath = Join-Path $Root "logo.png"
if (-not (Test-Path $logoPath)) { throw "logo.png not found in repo root" }
$master = [System.Drawing.Image]::FromFile($logoPath)
if ($master.Width -ne $master.Height) { throw "logo.png must be square" }

# tile background color sampled from the logo corner (for wide-tile compositing)
$cornerBmp = New-Object System.Drawing.Bitmap($master)
$corner = $cornerBmp.GetPixel(8, 8)
$cornerBmp.Dispose()
# corner color struct used directly
Write-Output ("corner bg: " + $bg)

function Resize([System.Drawing.Image]$src, [int]$w, [int]$h, [System.Drawing.Color]$bgColor, [double]$fit) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    if ($bgColor -ne [System.Drawing.Color]::Empty) { $g.Clear($bgColor) }
    $side = [int][math]::Min($w, $h) * $fit
    $x = [int](($w - $side) / 2); $y = [int](($h - $side) / 2)
    $g.DrawImage($src, $x, $y, $side, $side)
    $g.Dispose()
    return $bmp
}

function Save-Png([System.Drawing.Bitmap]$bmp, [string]$path) {
    $dir = Split-Path -Parent $path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
}

# square icon sizes: name -> size
$squareSets = @(
    @{ dir = "packaging\assets"; names = @(
        @{ n = "Square44x44Logo.scale-100.png";  s = 44 },  @{ n = "Square44x44Logo.scale-125.png";  s = 55 },
        @{ n = "Square44x44Logo.scale-150.png";  s = 66 },  @{ n = "Square44x44Logo.scale-200.png";  s = 88 },
        @{ n = "Square44x44Logo.scale-400.png";  s = 176 },
        @{ n = "Square150x150Logo.scale-100.png"; s = 150 }, @{ n = "Square150x150Logo.scale-125.png"; s = 188 },
        @{ n = "Square150x150Logo.scale-150.png"; s = 225 }, @{ n = "Square150x150Logo.scale-200.png"; s = 300 },
        @{ n = "Square150x150Logo.scale-400.png"; s = 600 },
        @{ n = "Square44x44Logo.targetsize-16.png"; s = 16 }, @{ n = "Square44x44Logo.targetsize-20.png"; s = 20 },
        @{ n = "Square44x44Logo.targetsize-24.png"; s = 24 }, @{ n = "Square44x44Logo.targetsize-30.png"; s = 30 },
        @{ n = "Square44x44Logo.targetsize-32.png"; s = 32 }, @{ n = "Square44x44Logo.targetsize-36.png"; s = 36 },
        @{ n = "Square44x44Logo.targetsize-44.png"; s = 44 }, @{ n = "Square44x44Logo.targetsize-48.png"; s = 48 },
        @{ n = "Square44x44Logo.targetsize-60.png"; s = 60 }, @{ n = "Square44x44Logo.targetsize-64.png"; s = 64 },
        @{ n = "Square44x44Logo.targetsize-72.png"; s = 72 }, @{ n = "Square44x44Logo.targetsize-96.png"; s = 96 },
        @{ n = "Square44x44Logo.targetsize-256.png"; s = 256 },
        @{ n = "Square44x44Logo.targetsize-24_altform-unplated.png"; s = 24 },
        @{ n = "StoreLogo.scale-100.png"; s = 50 }, @{ n = "StoreLogo.scale-125.png"; s = 62 },
        @{ n = "StoreLogo.scale-150.png"; s = 75 }, @{ n = "StoreLogo.scale-200.png"; s = 100 },
        @{ n = "StoreLogo.scale-400.png"; s = 200 },
        @{ n = "app-icon-256.png"; s = 256 },
        @{ n = "quiet-1024.png"; s = 1024 }
    ) }
)

# wide tiles: name -> w x h
$wideSets = @(
    @{ dir = "packaging\assets"; names = @(
        @{ n = "Wide310x150Logo.scale-100.png"; w = 310; h = 150 }, @{ n = "Wide310x150Logo.scale-125.png"; w = 388; h = 188 },
        @{ n = "Wide310x150Logo.scale-150.png"; w = 465; h = 225 }, @{ n = "Wide310x150Logo.scale-200.png"; w = 620; h = 300 },
        @{ n = "Wide310x150Logo.scale-400.png"; w = 1240; h = 600 }
    ) }
)

$targets = @((Join-Path $Root "packaging\assets"), (Join-Path $Root "src\QuietScreen\Assets"))
$copiedToApp = @{ n = "Square44x44Logo.png"; s = 44 }, @{ n = "Square150x150Logo.png"; s = 150 }, @{ n = "StoreLogo.png"; s = 50 }, @{ n = "Wide310x150Logo.png"; w = 310; h = 150 }

# square assets
foreach ($set in $squareSets) {
    foreach ($item in $set.names) {
        $side = $item.s
        $fit = if ($side -ge 100) { 1.0 } else { 1.0 }
        $bmp = Resize $master $side $side ([System.Drawing.Color]::Empty) $fit
        foreach ($t in $targets) { Save-Png $bmp (Join-Path $t $item.n) }
        $bmp.Dispose()
    }
}
# extra plain-named copies for src app assets
foreach ($c in $copiedToApp) {
    if ($c.w) { $bmp = Resize $master $c.w $c.h ([System.Drawing.Color]::Empty) 1.0 }
    else { $bmp = Resize $master $c.s $c.s ([System.Drawing.Color]::Empty) 1.0 }
    Save-Png $bmp (Join-Path (Join-Path $Root "src\QuietScreen\Assets") $c.n)
    $bmp.Dispose()
}
# wide tiles (logo centered on sampled tile bg, 66% height)
foreach ($set in $wideSets) {
    foreach ($item in $set.names) {
        $bmp = Resize $master $item.w $item.h $corner 0.66
        foreach ($t in $targets) { Save-Png $bmp (Join-Path $t $item.n) }
        $bmp.Dispose()
    }
}

# build multi-size app.ico (PNG entries inside ICO container)
$icoSizes = @(16, 24, 32, 48, 64, 128, 256)
$payloads = @()
foreach ($s in $icoSizes) {
    $bmp = Resize $master $s $s ([System.Drawing.Color]::Empty) 1.0
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $payloads += ,@($s, $ms.ToArray())
    $ms.Dispose()
}
$icoPath = Join-Path (Join-Path $Root "src\QuietScreen\Assets") "app.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$payloads.Count)
$offset = 6 + 16 * $payloads.Count
foreach ($p in $payloads) {
    $side = if ($p[0] -ge 256) { 0 } else { $p[0] }
    $bw.Write([byte]$side); $bw.Write([byte]$side); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$p[1].Length); $bw.Write([uint32]$offset)
    $offset += $p[1].Length
}
foreach ($p in $payloads) { $bw.Write($p[1]) }
$bw.Flush(); $bw.Close(); $fs.Close()
Copy-Item $icoPath (Join-Path (Join-Path $Root "packaging\assets") "app.ico") -Force

# store logo 300x300 for Partner Center upload
$logo300 = Resize $master 300 300 ([System.Drawing.Color]::Empty) 1.0
Save-Png $logo300 (Join-Path $Root "store-kit\screenshots\store-logo-300x300.png")
$logo300.Dispose()
$master.Dispose()

Write-Output "asset generation done:"
Get-ChildItem (Join-Path $Root "packaging\assets") | Measure-Object | ForEach-Object { Write-Output ("packaging/assets files: " + $_.Count) }
Get-ChildItem (Join-Path $Root "src\QuietScreen\Assets") | Measure-Object | ForEach-Object { Write-Output ("src assets files: " + $_.Count) }
