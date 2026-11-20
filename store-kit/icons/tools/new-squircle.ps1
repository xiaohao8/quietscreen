# new-squircle.ps1 - Generate an n-exponent superellipse SVG path (Apple Big Sur tile).
# Usage: powershell -File new-squircle.ps1 [-Size 1024] [-Exponent 5] [-OutFile path.txt]
param(
    [int]$Size = 1024,
    [double]$Exponent = 5,
    [string]$OutFile = ""
)
$half = $Size / 2.0
$exp = 2.0 / $Exponent
$pts = New-Object System.Collections.Generic.List[string]
$steps = 96
for ($i = 0; $i -lt $steps; $i++) {
    $t = 2 * [math]::PI * $i / $steps
    $c = [math]::Cos($t); $s = [math]::Sin($t)
    $x = $half + $half * [math]::Pow([math]::Abs($c), $exp) * [math]::Sign($c)
    $y = $half + $half * [math]::Pow([math]::Abs($s), $exp) * [math]::Sign($s)
    $pts.Add(("{0} {1:F1} {2:F1}" -f $(if ($i -eq 0) { "M" } else { "L" }), $x, $y))
}
$path = ($pts -join " ") + " Z"
if ($OutFile) { [System.IO.File]::WriteAllText($OutFile, $path); Write-Host ("Written: " + $OutFile) }
else { $path }
