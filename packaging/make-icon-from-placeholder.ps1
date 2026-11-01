$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$PackDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Assets = Join-Path (Split-Path -Parent $PackDir) 'src\QuietScreen\Assets'
$src256Path = Join-Path $PackDir 'assets\placeholder-256.png'

if (-not (Test-Path $src256)) { throw "placeholder-256.png not found in packaging\assets" }

# ---- 1. 从占位图生成全套 manifest PNG (直接覆盖) ----
$sizes = @{
    'Square44x44Logo' = 44; 'Square150x150Logo' = 150; 'Wide310x150Logo' = 150
}
$src = [System.Drawing.Image]::FromFile($src256)
foreach ($k in $sizes.Keys) {
    $w = $sizes[$k]
    if ($k -eq 'Wide310x150Logo') {
        $bmp = New-Object System.Drawing.Bitmap([int](310 * 1), [int](150 * 1))
    } else {
        $bmp = New-Object System.Drawing.Bitmap($w, $w)
    }
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, $bmp.Width, $bmp.Height)
    $g.Dispose()
    $out = Join-Path $PackDir ("assets\{0}.png" -f $k)
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  $k.png <- placeholder"
}
# scale 变体也替换为占位图 (保持 MUI 结构)
foreach ($scale in @{ '100' = 1.0; '125' = 1.25; '150' = 1.5; '200' = 2.0; '400' = 4.0 }.GetEnumerator()) {
    foreach ($k in @('Square44x44Logo', 'Square150x150Logo', 'Wide310x150Logo', 'StoreLogo')) {
        $baseW = @{ 'Square44x44Logo' = 44; 'Square150x150Logo' = 150; 'Wide310x150Logo' = 310; 'StoreLogo' = 50 }[$k]
        $w = [int]($baseW * $scale.Value)
        if ($k -eq 'Wide310x150Logo') { $bmp = New-Object System.Drawing.Bitmap($w, [int]($w * 150 / 310)) } else { $bmp = New-Object System.Drawing.Bitmap($w, $w) }
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.DrawImage($src, 0, 0, $bmp.Width, $bmp.Height)
        $g.Dispose()
        $bmp.Save((Join-Path $PackDir ("assets\{0}.scale-{1}.png" -f $k, $scale.Key)), [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    }
}
$src.Dispose()
Write-Host "manifest PNGs replaced with placeholder"

# ---- 2. app.ico (多尺寸: 小尺寸 BMP, 256 PNG) ----
# ---- 2. app.ico: 多尺寸 (小尺寸 BMP, >=64 PNG) — 结构与 make-assets.ps1 验证版一致 ----
$src256 = New-Object System.Drawing.Bitmap($src256Path)

function ConvertTo-BmpIcoEntry {
    param([System.Drawing.Bitmap]$Bmp)
    $size = $Bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([UInt32]40); $bw.Write([Int32]$size); $bw.Write([Int32]($size * 2))
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
    $xorSize = $size * $size * 4
    $andRow = [Math]::Ceiling($size / 32.0) * 4
    $bw.Write([UInt32]($xorSize + $andRow * $size)); $bw.Write([UInt32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $c = $Bmp.GetPixel($x, $y)
            $bw.Write([Byte]$c.B); $bw.Write([Byte]$c.G); $bw.Write([Byte]$c.R); $bw.Write([Byte]$c.A)
        }
    }
    for ($y = 0; $y -lt $size; $y++) { for ($i = 0; $i -lt $andRow; $i++) { $bw.Write([Byte]0) } }
    $bw.Flush()
    return $ms.ToArray()
}

$icoSizes = @(16, 24, 32, 48, 64, 128, 256)
$entries = @()
foreach ($sz in $icoSizes) {
    $b = New-Object System.Drawing.Bitmap($src256, $sz, $sz)
    if ($sz -ge 64) {
        $ms = New-Object System.IO.MemoryStream
        $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $entries += ,@{ Size = $sz; Data = $ms.ToArray() }
        $ms.Dispose()
    } else {
        $entries += ,@{ Size = $sz; Data = (ConvertTo-BmpIcoEntry -Bmp $b) }
    }
    $b.Dispose()
}
$icoPath = Join-Path $Assets 'app.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $s2 = $e.Size
    $bw.Write([Byte]$(if ($s2 -ge 256) { 0 } else { $s2 }))
    $bw.Write([Byte]$(if ($s2 -ge 256) { 0 } else { $s2 }))
    $bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$e.Data.Length); $bw.Write([UInt32]$offset)
    $offset += $e.Data.Length
}
foreach ($e in $entries) { $bw.Write($e.Data) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()
$src256.Dispose()
Write-Host "app.ico rebuilt (placeholder)"
