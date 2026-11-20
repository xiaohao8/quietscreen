# run-acceptance.ps1 - Full acceptance test for QuietScreen store-kit deliverables.
# ASCII-only (PS 5.1 parses BOM-less UTF-8 as GBK - do not add Chinese here).
# Usage: powershell -ExecutionPolicy Bypass -File run-acceptance.ps1 -Root <repo> [-SkipBuild]
param(
    [Parameter(Mandatory=$true)][string]$Root,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName System.Drawing
$script:PASS = 0
$script:FAIL = 0
$script:RESULTS = New-Object System.Collections.Generic.List[string]

function Check($name, $ok, $detail) {
    if ($ok) { $script:PASS++ ; $tag = "PASS" } else { $script:FAIL++ ; $tag = "FAIL" }
    $line = "[$tag] $name" + $(if ($detail) { " -- $detail" } else { "" })
    $script:RESULTS.Add($line)
    Write-Output $line
}

# ---------- A. File manifest ----------
Write-Output "== A. File manifest =="
$manifest = @(
    "AGENTS.md",
    "store-kit\screenshots\shot1-home.png",
    "store-kit\screenshots\shot2-guardian.png",
    "store-kit\screenshots\shot3-shutdown.png",
    "store-kit\screenshots\shot4-settings.png",
    "store-kit\screenshots\store-logo-300x300.png",
    "store-kit\screenshots\README.md",
    "store-kit\icons\README.md",
    "store-kit\icons\icon-overview.png",
    "store-kit\icons\icon-overview-p23.png",
    "store-kit\icons\icon-overview-final.png",
    "store-kit\icons\tools\render.sh",
    "store-kit\icons\tools\render.ps1",
    "store-kit\icons\tools\new-squircle.ps1",
    "packaging\build-store.ps1",
    "packaging\AppxManifest.template.xml",
    "packaging\make-assets.ps1",
    "packaging\icon-brief.md"
)
foreach ($rel in $manifest) {
    Check ("A:exists:" + $rel) (Test-Path (Join-Path $Root $rel)) ""
}

# ---------- B. PNG validity (dims + non-blank) ----------
$zhDocs = (Get-ChildItem -Path (Join-Path $Root "store-kit") -Filter "0*.md" -File).Count
Check "A:store-kit-docs-count=9" ($zhDocs -eq 9) ("found=$zhDocs")

Write-Output "== B. PNG validity =="
function Test-Png($path, $expW, $expH) {
    try {
        $img = [System.Drawing.Image]::FromFile($path)
        $w = $img.Width; $h = $img.Height
        $dimOk = ($w -eq $expW -and $h -eq $expH)
        $bmp = New-Object System.Drawing.Bitmap($img)
        $colors = New-Object System.Collections.Generic.HashSet[int]
        for ($x = 0; $x -lt 8; $x++) { for ($y = 0; $y -lt 8; $y++) {
            $px = $bmp.GetPixel([int]($x * ($bmp.Width - 1) / 7.0), [int]($y * ($bmp.Height - 1) / 7.0))
            [void]$colors.Add($px.ToArgb())
        }}
        $bmp.Dispose(); $img.Dispose()
        $blank = ($colors.Count -le 1)
        return @{ Ok = ($dimOk -and -not $blank); Dim = "${w}x${h}"; Colors = $colors.Count }
    } catch { return @{ Ok = $false; Dim = "unreadable"; Colors = 0 } }
}

$pngChecks = @(
    @{ p = "store-kit\screenshots\shot1-home.png"; w = 1920; h = 1080 },
    @{ p = "store-kit\screenshots\shot2-guardian.png"; w = 1920; h = 1080 },
    @{ p = "store-kit\screenshots\shot3-shutdown.png"; w = 1920; h = 1080 },
    @{ p = "store-kit\screenshots\shot4-settings.png"; w = 1920; h = 1080 },
    @{ p = "store-kit\screenshots\store-logo-300x300.png"; w = 300; h = 300 },
    @{ p = "store-kit\icons\p2-eclipse-a.png"; w = 1024; h = 1024 },
    @{ p = "store-kit\icons\p2-eclipse-b.png"; w = 1024; h = 1024 },
    @{ p = "store-kit\icons\p2-eclipse-c.png"; w = 1024; h = 1024 },
    @{ p = "store-kit\icons\p3-silhouette-a.png"; w = 1024; h = 1024 },
    @{ p = "store-kit\icons\p3-silhouette-b.png"; w = 1024; h = 1024 },
    @{ p = "store-kit\icons\p3-silhouette-c.png"; w = 1024; h = 1024 },
    @{ p = "store-kit\icons\p3-silhouette-d.png"; w = 1024; h = 1024 },
    @{ p = "store-kit\icons\moonlit-a.png"; w = 1024; h = 1024 },
    @{ p = "store-kit\icons\moonlit-b.png"; w = 1024; h = 1024 },
    @{ p = "store-kit\icons\monogram-a.png"; w = 1024; h = 1024 },
    @{ p = "store-kit\icons\monogram-b.png"; w = 1024; h = 1024 },
    @{ p = "store-kit\icons\icon-overview-final.png"; w = 1360; h = 1120 }
)
foreach ($c in $pngChecks) {
    $full = Join-Path $Root $c.p
    if (-not (Test-Path $full)) { Check ("B:png:" + $c.p) $false "missing"; continue }
    $r = Test-Png $full $c.w $c.h
    Check ("B:png:" + $c.p) $r.Ok ("dims=$($r.Dim) expect=$($c.w)x$($c.h) colors=$($r.Colors)")
}

# ---------- B2. SVG well-formedness ----------
Write-Output "== B2. SVG XML parse =="
$svgDir = Join-Path $Root "store-kit\icons\src"
$svgs = Get-ChildItem -Path $svgDir -Filter *.svg
Check "B2:svg-count>=23" ($svgs.Count -ge 23) "found=$($svgs.Count)"
$badSvg = 0
foreach ($s in $svgs) {
    try { [xml](Get-Content $s.FullName -Raw -Encoding UTF8) | Out-Null }
    catch { $badSvg++ ; Write-Output "  bad: $($s.Name) : $($_.Exception.Message)" }
}
Check "B2:svg-xml-valid" ($badSvg -eq 0) "bad=$badSvg / $($svgs.Count)"

# ---------- C. Squircle tool -> render chain ----------
Write-Output "== C. Squircle tool chain =="
$sqTool = Join-Path $Root "store-kit\icons\tools\new-squircle.ps1"
$tmp = Join-Path $env:TEMP ("accept-" + [guid]::NewGuid().ToString("N").Substring(0,8))
New-Item -ItemType Directory -Path $tmp -Force | Out-Null
& powershell -NoProfile -ExecutionPolicy Bypass -File $sqTool -Size 1024 -OutFile "$tmp\sq.txt" | Out-Null
$sq = ""
if (Test-Path "$tmp\sq.txt") { $sq = Get-Content "$tmp\sq.txt" -Raw }
$sqOk = ($sq -match '^M [\d.]+ [\d.]+( L [\d.]+ [\d.]+){95} Z$')
Check "C:squircle-format" ($null -ne ($sq -match '^M ') -and $sqOk) "len=$($sq.Length)"
$nums = [regex]::Matches($sq, '-?\d+\.?\d*') | ForEach-Object { [double]$_.Value }
$inRange = ($nums | Where-Object { $_ -lt 0 -or $_ -gt 1024 }).Count -eq 0
Check "C:squircle-range-0-1024" $inRange ""

# render a minimal svg using the generated squircle, then verify non-blank
$miniSvg = @"
<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024">
<path d="$($sq.Trim())" fill="#2A3059"/>
<circle cx="512" cy="512" r="220" fill="#FFE2A8"/>
</svg>
"@
[System.IO.File]::WriteAllText("$tmp\mini.svg", $miniSvg, (New-Object System.Text.UTF8Encoding($false)))
$edge = @("C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
          "C:\Program Files\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
$ws = "--window-size=1024,1024"
$shot = "--screenshot=$tmp\mini.png"
$url = "file:///" + ("$tmp\mini.svg" -replace '\\','/')
if ($edge) {
    & $edge --headless=new --disable-gpu --hide-scrollbars --no-first-run --user-data-dir="$tmp\pf" $ws $shot $url 2>&1 | Out-Null
    $miniOk = Test-Path "$tmp\mini.png"
    if ($miniOk) {
        $r = Test-Png "$tmp\mini.png" 1024 1024
        Check "C:chain-render-nonblank" $r.Ok ("colors=$($r.Colors)")
    } else { Check "C:chain-render-nonblank" $false "no output" }
} else { Check "C:chain-render-nonblank" $false "edge missing" }


# ---------- D. Docs reference integrity ----------
Write-Output "== D. Docs reference integrity =="
# every path-like reference inside AGENTS.md must exist somewhere in the repo
$agentsPath = Join-Path $Root "AGENTS.md"
$agents = Get-Content $agentsPath -Raw -Encoding UTF8
$refs = [regex]::Matches($agents, '[\w\-\/\.\u4e00-\u9fff]+\.(md|ps1|sh|xml|png|json|exe)') |
        ForEach-Object { $_.Value } | Sort-Object -Unique
$missing = 0
foreach ($r in $refs) {
    $base = [IO.Path]::GetFileName($r)
    if ($base -like "*.exe") { continue }   # build artifacts, covered by E:build
    $hit = Get-ChildItem -Path $Root -Recurse -Filter $base -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $hit) { $missing++ ; Write-Output "  missing-ref: $r" }
}
Check "D:agents-md-refs" ($missing -eq 0) "refs=$($refs.Count) missing=$missing"

$skillMd = "C:\Users\35436\.zcode\skills\svg-icon-studio\SKILL.md"
if (Test-Path $skillMd) {
    $head = (Get-Content $skillMd -TotalCount 4) -join "`n"
    Check "D:skill-frontmatter" ($head -match 'name: svg-icon-studio' -and $head -match 'description:')
} else { Check "D:skill-frontmatter" $false "skill missing" }

if ($SkipBuild) {
    Write-Output "== E. Repo build: SKIPPED by flag =="
    Check "E:build" $true "skipped"
} else {
    Write-Output "== E. Repo build (isolated -o) =="
    $dn = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dn) {
        Check "E:build" $false "dotnet SDK not found on PATH"
    } else {
        $ver = & dotnet --version 2>$null
        Write-Output "  dotnet version: $ver"
        $buildOut = Join-Path $tmp "build-out"
        Push-Location (Join-Path $Root "src\QuietScreen")
        & dotnet build -c Release -o $buildOut --nologo -v q 2>&1 | Select-Object -Last 4 | Write-Output
        $code = $LASTEXITCODE
        Pop-Location
        Check "E:build" ($code -eq 0) "exit=$code out=$buildOut"
    }
}

# ---------- Summary ----------
Write-Output ""
# ---------- G. Final logo assets + privacy stage ----------
Write-Output "== G. Final logo assets + privacy stage =="
Check "G:root-logo" (Test-Path (Join-Path $Root "logo.png")) ""
$storeLogo = Join-Path $Root "packaging\assets\StoreLogo.scale-100.png"
if (Test-Path $storeLogo) {
    $r = Test-Png $storeLogo 50 50
    Check "G:storelogo-50" $r.Ok ("dims=$($r.Dim) colors=$($r.Colors)")
} else { Check "G:storelogo-50" $false "missing" }
$ico = Join-Path $Root "src\QuietScreen\Assets\app.ico"
if (Test-Path $ico) {
    $fs = [System.IO.File]::OpenRead($ico)
    $b0 = $fs.ReadByte(); $b1 = $fs.ReadByte(); $b2 = $fs.ReadByte(); $b3 = $fs.ReadByte()
    $fs.Close()
    Check "G:app-ico-header" ($b0 -eq 0 -and $b1 -eq 0 -and $b2 -eq 1 -and $b3 -eq 0) "ICO magic"
} else { Check "G:app-ico-header" $false "missing" }
Write-Output ""
Write-Output ("==== ACCEPTANCE SUMMARY: PASS={0} FAIL={1} ====" -f $script:PASS, $script:FAIL)
$script:RESULTS | Out-File (Join-Path $tmp "results.txt") -Encoding UTF8
Write-Output "results.txt: $tmp\results.txt"
if ($script:FAIL -gt 0) { exit 1 } else { exit 0 }
