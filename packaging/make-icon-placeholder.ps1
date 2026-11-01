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
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $m = [double]$Size * $Margin
    $w = $Size - 2 * $m
    $bgR = [Math]::Max(4.0, $w * 0.22)

    # 极简占位: 深色圆角方块 (等待正式图标替换)
    $rect = New-Object System.Drawing.RectangleF([float]$m, [float]$m, [float]$w, [float]$w)
    $c1 = [System.Drawing.Color]::FromArgb(255, 45, 45, 52)
    $c2 = [System.Drawing.Color]::FromArgb(255, 30, 30, 36)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 45.0)
    $bgPath = New-RoundedPath -X $m -Y $m -W $w -H $w -R $bgR
    $g.FillPath($brush, $bgPath)

    $g.Dispose()
    $bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

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
