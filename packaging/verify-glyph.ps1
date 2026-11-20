# verify-glyph.ps1 - Render the publisher display name built from char codes
# to a PNG so the glyphs can be VISUALLY verified (homophone code-point trap:
# 0x6D69 is a different character than 0x8C6A; encoding-safe != correct).
# ASCII-only script body.
param(
    [int[]]$Codes = @(0x4ED8, 0x5FD7, 0x8C6A),
    [string]$OutPng = "$env:TEMP\pubname-check.png"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$name = -join ($Codes | ForEach-Object { [char]$_ })

$bmp = New-Object System.Drawing.Bitmap 640, 220
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::White)
$font = New-Object System.Drawing.Font("Microsoft YaHei", 72, [System.Drawing.FontStyle]::Bold)
$brush = [System.Drawing.Brushes]::Black
$g.DrawString($name, $font, $brush, 40, 50)
$small = New-Object System.Drawing.Font("Consolas", 16)
$g.DrawString((($Codes | ForEach-Object { "U+{0:X4}" -f $_ }) -join " "), $small, $brush, 40, 170)
$g.Dispose(); $font.Dispose(); $small.Dispose()
$bmp.Save($OutPng, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ("rendered: " + $OutPng + "  codes: " + (($Codes | ForEach-Object { "0x{0:X4}" -f $_ }) -join ","))
