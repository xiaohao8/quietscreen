# build-final.ps1 - Rebuild store package with the real Partner Center identity.
# ASCII-only script body; the Chinese publisher display name is built from char
# codes so no encoding layer can corrupt it on the way into the manifest.
# Partner Center publisher display name = U+4ED8 U+5FD7 U+8C6A.
# CAREFUL: the third char is 0x8C6A, NOT 0x8C6A (both are read "hao").
# 0x8C6A was used in v1 and the Store rejected the package with
# "PublisherDisplayName ... doesn't match your publisher display name".
$ErrorActionPreference = 'Stop'
$pubDisplayName = -join @([char]0x4ED8, [char]0x5FD7, [char]0x8C6A)

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here

# Version is read from the csproj so package and app can never drift apart.
$csprojText = Get-Content (Join-Path $root "src\QuietScreen\QuietScreen.csproj") -Raw
if ($csprojText -match '<Version>([^<]+)</Version>') { $version = $Matches[1] } else { throw "cannot parse <Version> from csproj" }
Write-Output ("Package version from csproj: " + $version)

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here "build-store.ps1") `
    -IdentityName "A135C2AE.QuietScreen" `
    -Publisher "CN=815AB3D3-C7A7-40CB-93F0-57D02D5FAAF0" `
    -PublisherDisplayName $pubDisplayName `
    -Version $version `
    -PublishDir (Join-Path $root "build\publish-final")
if ($LASTEXITCODE -ne 0 -and -not $?) { throw "build-store.ps1 failed" }

# ---- verify the generated manifest really carries the identity ----
# Compare against an INDEPENDENT codepoint list, not $pubDisplayName itself:
# the old check compared a value with itself and could never catch a typo.
$expectedCodes = @(0x4ED8, 0x5FD7, 0x8C6A)
# The reserved app name in Partner Center is "QuietScreen " + U+5C4F U+606F.
# An earlier package used "Quiet ..." (no "Screen") and Store rejected it with
# "uses a display name that you have not reserved".
$expectedAppName = "QuietScreen " + (-join @([char]0x5C4F, [char]0x606F))
$manifest = Join-Path $root "build\layout\AppxManifest.xml"
$xml = [xml](Get-Content $manifest -Raw -Encoding UTF8)
$okName = ($xml.Package.Identity.Name -eq "A135C2AE.QuietScreen")
$okPub  = ($xml.Package.Identity.Publisher -eq "CN=815AB3D3-C7A7-40CB-93F0-57D02D5FAAF0")
$actual = [string]$xml.Package.Properties.PublisherDisplayName
$okDisp = ($actual.Length -eq $expectedCodes.Length)
if ($okDisp) {
    for ($i = 0; $i -lt $expectedCodes.Length; $i++) {
        if ([int][char]$actual[$i] -ne $expectedCodes[$i]) { $okDisp = $false; break }
    }
}
$actualAppName = [string]$xml.Package.Properties.DisplayName
$okAppName = ($actualAppName -eq $expectedAppName)
$actualCodes = (($actual.ToCharArray() | ForEach-Object { '0x{0:X4}' -f [int]$_ }) -join ' ')
$bytes  = [System.IO.File]::ReadAllBytes($manifest)
$hasBom = ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
Write-Output ("MANIFEST PublisherDisplayName codepoints: " + $actualCodes)
Write-Output ("MANIFEST DisplayName codepoints: " + (($actualAppName.ToCharArray() | ForEach-Object { '0x{0:X4}' -f [int]$_ }) -join ' '))
Write-Output ("VERIFY Name=" + $okName + " Publisher=" + $okPub + " PublisherDisplayName=" + $okDisp + " DisplayName=" + $okAppName + " ManifestBOM=" + $hasBom)
if (-not ($okName -and $okPub -and $okDisp -and $okAppName)) { throw "manifest identity mismatch" }
Write-Output "IDENTITY-OK"
