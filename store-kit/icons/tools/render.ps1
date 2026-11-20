# render.ps1 - Batch render SVG -> PNG via headless Edge (no window).
# Works around the Chinese-path silent failure by staging in an ASCII temp dir.
# Usage: powershell -File render.ps1 -Src .\src -Out .\ -Sizes 1024,44,24
param(
    [Parameter(Mandatory=$true)][string]$Src,
    [Parameter(Mandatory=$true)][string]$Out,
    [string]$Sizes = "1024"
)
$ErrorActionPreference = 'Stop'

# 1. Locate Edge
$edge = @(
    "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    "C:\Program Files\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw "Edge not found. Install Edge or edit the paths in this script." }

# 2. Stage into an ASCII temp dir (non-ASCII paths make --screenshot fail silently)
$tmp = Join-Path $env:TEMP ("iconrender-" + [guid]::NewGuid().ToString("N").Substring(0,8))
New-Item -ItemType Directory -Path $tmp -Force | Out-Null

$svgs = Get-ChildItem -Path $Src -Filter *.svg
if (-not $svgs) { throw "No SVG files found in: $Src" }
$sizeList = $Sizes.Split(',') | ForEach-Object { [int]$_.Trim() }

$done = 0
$failed = 0
foreach ($svg in $svgs) {
    $name = $svg.BaseName
    foreach ($s in $sizeList) {
        # Rewrite width/height attributes (viewBox keeps aspect scaling)
        $content = (Get-Content $svg.FullName -Raw -Encoding UTF8) `
            -replace 'width="1024"', ("width=`"$s`"") `
            -replace 'height="1024"', ("height=`"$s`"")
        $tmpSvg = Join-Path $tmp ("{0}-{1}.svg" -f $name, $s)
        [System.IO.File]::WriteAllText($tmpSvg, $content, (New-Object System.Text.UTF8Encoding($false)))

        $tmpPng = Join-Path $tmp ("{0}-{1}.png" -f $name, $s)
        $url = "file:///" + ($tmpSvg -replace '\\','/')
        # NOTE: pass Edge flags as pre-built strings - a bare --window-size=$s,$s
        # makes PowerShell split the comma into two arguments.
        # NOTE: unique --user-data-dir per render - otherwise rapid sequential
        # launches hand off to a running instance and drop the screenshot request.
        $ws = "--window-size={0},{1}" -f $s, $s
        $shot = "--screenshot=$tmpPng"
        $profile = Join-Path $tmp ("profile-{0}-{1}" -f $name, $s)
        & $edge --headless=new --disable-gpu --hide-scrollbars --no-first-run `
            --user-data-dir=$profile $ws $shot $url 2>$null
        if (Test-Path $tmpPng) {
            $suffix = if ($s -eq 1024) { "" } else { "-$s" }
            Copy-Item $tmpPng (Join-Path $Out ("{0}{1}.png" -f $name, $suffix)) -Force
            $done++
        } else {
            Write-Warning "Render failed: $name at ${s}px"
            $failed++
        }
    }
}
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ("Done: {0} rendered, {1} failed -> {2}" -f $done, $failed, $Out)
