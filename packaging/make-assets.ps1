# 生成 QuietScreen 应用图标 (ico) 与 MSIX 全套图片资产
# 用法: powershell -NoProfile -ExecutionPolicy Bypass -File make-assets.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$ProjectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$AssetsDir   = Join-Path $ProjectRoot 'src\QuietScreen\Assets'
$PkgAssets   = Join-Path $ProjectRoot 'packaging\assets'
New-Item -ItemType Directory -Path $AssetsDir -Force | Out-Null
New-Item -ItemType Directory -Path $PkgAssets -Force | Out-Null

function New-RoundedPath {
    param([float]$X, [float]$Y, [float]$W, [float]$H, [float]$R)
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $R * 2
    $p.AddArc($X, $Y, $d, $d, 180, 90)
    $p.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $p.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $p.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# 在 size x size 画布上画主图标; margin 为画布边距(0~1)
function Draw-Icon {
    param([int]$Size, [string]$OutFile, [double]$Margin)
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'

    $m   = [double]$Size * $Margin
    $w   = $Size - 2 * $m
    $bgR = [Math]::Max(4.0, $w * 0.22)

    # 背景: 渐变圆角方块
    $rect = New-Object System.Drawing.RectangleF([float]$m, [float]$m, [float]$w, [float]$w)
    $c1 = [System.Drawing.Color]::FromArgb(255, 122, 92, 255)
    $c2 = [System.Drawing.Color]::FromArgb(255,  77,124, 255)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 45.0)
    $bgPath = New-RoundedPath -X $m -Y $m -W $w -H $w -R $bgR
    $g.FillPath($brush, $bgPath)

    $u = $w / 256.0   # 相对 256 设计稿的单位

    # 显示器边框 (白)
    $monX = $m + 44*$u; $monY = $m + 58*$u; $monW = 168*$u; $monH = 118*$u; $monR = 16*$u
    $monPath = New-RoundedPath -X $monX -Y $monY -W $monW -H $monH -R $monR
    $white = [System.Drawing.Brushes]::White
    $g.FillPath($white, $monPath)

    # 屏幕内容 (深色)
    $inset = 11*$u
    $scrX = $monX + $inset; $scrY = $monY + $inset
    $scrW = $monW - 2*$inset; $scrH = $monH - 2*$inset; $scrR = 10*$u
    $scrPath = New-RoundedPath -X $scrX -Y $scrY -W $scrW -H $scrH -R $scrR
    $dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 22, 24, 44))
    $g.FillPath($dark, $scrPath)

    # 月亮 (息屏/夜间): 黄色圆 + 背景色圆咬一口, 留下右下角厚月牙
    $moonR = 20*$u
    $mx = $scrX + $scrW - 34*$u; $my = $scrY + 34*$u
    $g.FillEllipse([System.Drawing.Brushes]::Wheat, ($mx-$moonR), ($my-$moonR), (2*$moonR), (2*$moonR))
    $bite = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 22, 24, 44))
    $bx = $mx - $moonR*0.75; $by = $my - $moonR*0.75; $br = $moonR*0.95
    $g.FillEllipse($bite, ($bx-$br), ($by-$br), (2*$br), (2*$br))

    # 星星两颗
    $star = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 255, 255, 255))
    $r1 = 4.5*$u; $r2 = 3*$u
    $g.FillEllipse($star, (($scrX+34*$u)-$r1), (($scrY+26*$u)-$r1), (2*$r1), (2*$r1))
    $g.FillEllipse($star, (($scrX+58*$u)-$r2), (($scrY+44*$u)-$r2), (2*$r2), (2*$r2))

    # 支架
    $neckW = 22*$u
    $g.FillRectangle($white, [float]($m+128*$u-$neckW/2), [float]($monY+$monH), [float]$neckW, [float](16*$u))
    $basePath = New-RoundedPath -X ($m+88*$u) -Y ($monY+$monH+16*$u) -W (80*$u) -H (12*$u) -R (6*$u)
    $g.FillPath($white, $basePath)

    $g.Dispose()
    $bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# ---------- PNG 资产 ----------
$scaleSizes = @{ 100 = 1.0; 125 = 1.25; 150 = 1.5; 200 = 2.0; 400 = 4.0 }
foreach ($scale in $scaleSizes.Keys) {
    $f = $scaleSizes[$scale]
    # Square44x44 (含 unplated 用途)
    Draw-Icon -Size ([int](44*$f))  -OutFile (Join-Path $PkgAssets "Square44x44Logo.scale-$scale.png") -Margin 0.08
    # Square150x150
    Draw-Icon -Size ([int](150*$f)) -OutFile (Join-Path $PkgAssets "Square150x150Logo.scale-$scale.png") -Margin 0.12
    # Wide310x150
    $bmp = New-Object System.Drawing.Bitmap([int](310*$f), [int](150*$f))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $W = 310*$f; $H = 150*$f
    $rect = New-Object System.Drawing.RectangleF(0, 0, [float]$W, [float]$H)
    $c1 = [System.Drawing.Color]::FromArgb(255, 122, 92, 255)
    $c2 = [System.Drawing.Color]::FromArgb(255,  77,124, 255)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 45.0)
    $path = New-RoundedPath -X 0 -Y 0 -W $W -H $H -R ([Math]::Max(4.0, $H*0.12))
    $g.FillPath($brush, $path)
    # 居中画主图标 (占高度的 72%)
    $iconSize = [int]($H * 0.72)
    $tmpFile = [System.IO.Path]::GetTempFileName() + '.png'
    Draw-Icon -Size $iconSize -OutFile $tmpFile -Margin 0.08
    $iconBmp = New-Object System.Drawing.Bitmap($tmpFile)
    $g.DrawImage($iconBmp, [float]((($W-$iconSize)/2)), [float]((($H-$iconSize)/2)), [float]$iconSize, [float]$iconSize)
    $iconBmp.Dispose(); Remove-Item $tmpFile -Force
    $g.Dispose()
    $bmp.Save((Join-Path $PkgAssets "Wide310x150Logo.scale-$scale.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}
# StoreLogo (50 基准)
$storeSizes = @{ 100 = 50; 125 = 62; 150 = 75; 200 = 100 }
foreach ($k in $storeSizes.Keys) {
    Draw-Icon -Size $storeSizes[$k] -OutFile (Join-Path $PkgAssets "StoreLogo.scale-$k.png") -Margin 0.10
}
# targetsize 系列 (任务栏/资源管理器, 无边距铺满)
$targetSizes = @(16, 20, 24, 30, 32, 36, 44, 48, 60, 64, 72, 96, 256)
foreach ($t in $targetSizes) {
    Draw-Icon -Size $t -OutFile (Join-Path $PkgAssets "Square44x44Logo.targetsize-$t.png") -Margin 0.0
}
Draw-Icon -Size 24 -OutFile (Join-Path $PkgAssets 'Square44x44Logo.targetsize-24_altform-unplated.png') -Margin 0.0
# 应用窗口图标 (ico 源)
Draw-Icon -Size 256 -OutFile (Join-Path $PkgAssets 'app-icon-256.png') -Margin 0.06

# ---------- ICO (多尺寸: 小尺寸 BMP, >=64 用 PNG 压缩) ----------
function ConvertTo-BmpIcoEntry {
    param([System.Drawing.Bitmap]$Bmp)
    $size = $Bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    # BITMAPINFOHEADER (biHeight = 2x, 含 AND mask)
    $bw.Write([UInt32]40); $bw.Write([Int32]$size); $bw.Write([Int32]($size*2))
    $bw.Write([UInt16]1);  $bw.Write([UInt16]32);  $bw.Write([UInt32]0)
    $xorSize = $size * $size * 4
    $andRow  = [Math]::Ceiling($size / 32.0) * 4
    $bw.Write([UInt32]($xorSize + $andRow * $size)); $bw.Write([UInt32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
    # 像素: 自下而上 BGRA
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $c = $Bmp.GetPixel($x, $y)
            $bw.Write([Byte]$c.B); $bw.Write([Byte]$c.G); $bw.Write([Byte]$c.R); $bw.Write([Byte]$c.A)
        }
    }
    # AND mask (全 0 = 用 alpha)
    for ($y = 0; $y -lt $size; $y++) { for ($i = 0; $i -lt $andRow; $i++) { $bw.Write([Byte]0) } }
    $bw.Flush()
    $data = $ms.ToArray(); $bw.Dispose(); $ms.Dispose()
    return ,$data
}

$src256 = New-Object System.Drawing.Bitmap (Join-Path $PkgAssets 'app-icon-256.png')
$icoSizes  = @(16, 24, 32, 48, 64, 128, 256)
$entries   = @()
foreach ($s in $icoSizes) {
    $b = New-Object System.Drawing.Bitmap($src256, $s, $s)
    if ($s -ge 64) {
        $ms = New-Object System.IO.MemoryStream
        $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $entries += ,@{ Size = $s; Data = $ms.ToArray() }
        $ms.Dispose()
    } else {
        $entries += @{ Size = $s; Data = (ConvertTo-BmpIcoEntry -Bmp $b) }
    }
    $b.Dispose()
}
$icoPath = Join-Path $AssetsDir 'app.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $s = $e.Size
    $bw.Write([Byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([Byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$e.Data.Length); $bw.Write([UInt32]$offset)
    $offset += $e.Data.Length
}
foreach ($e in $entries) { $bw.Write($e.Data) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()
$src256.Dispose()

Write-Host "OK assets generated:"
Write-Host "  $icoPath"
Get-ChildItem $PkgAssets -File | ForEach-Object { Write-Host ("  {0}  ({1} bytes)" -f $_.Name, $_.Length) }
