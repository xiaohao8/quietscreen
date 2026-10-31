# =====================================================================
# QuietScreen MSIX 打包脚本 (微软商店上架用)
#
# 用法:
#   # 本地测试包 (自签名, 可直接安装):
#   powershell -File build-store.ps1
#
#   # 上架微软商店 (身份信息来自 Partner Center > 应用标识):
#   powershell -File build-store.ps1 -IdentityName "你在PC的IdentityName" `
#       -Publisher "CN=XXXX..." -Version "1.0.1.0"
#
#   # 打包完直接安装到本机 (需要管理员确认一次证书导入):
#   powershell -File build-store.ps1 -Install
#
# 产物: packaging\out\QuietScreen_<版本>_x64.msix
#   - 已签名 (自签) -> 用于本机安装测试
#   - 商店提交: Partner Center 会重新签名, 直接上传此 .msix 即可
# =====================================================================
param(
    [string]$Version = "1.0.0.0",
    [string]$IdentityName = "QuietScreenApp",
    [string]$Publisher = "CN=QuietScreen",
    [string]$PublisherDisplayName = "QuietScreen",
    [switch]$NoSign,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$Root     = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Src      = Join-Path $Root 'src\QuietScreen'
$PackDir  = Join-Path $Root 'packaging'
$BuildDir = Join-Path $Root 'build'
$Layout   = Join-Path $BuildDir 'layout'
$OutDir   = Join-Path $PackDir 'out'

Write-Host "== QuietScreen MSIX 打包 ==" -ForegroundColor Cyan
Write-Host "Version=$Version Identity=$IdentityName Publisher=$Publisher"

# ---------- 1. 发布 (自包含 x64) ----------
Write-Host "[1/5] dotnet publish (self-contained x64)..." -ForegroundColor Yellow
$PublishDir = Join-Path $BuildDir 'publish'
dotnet publish $Src -c Release -r win-x64 --self-contained true -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "publish 失败" }

# ---------- 2. 组装 MSIX 布局 ----------
Write-Host "[2/5] 组装布局..." -ForegroundColor Yellow
if (Test-Path $Layout) { Remove-Item $Layout -Recurse -Force }
New-Item -ItemType Directory -Path $Layout -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $Layout 'Assets') -Force | Out-Null
Copy-Item "$PublishDir\*" $Layout -Recurse -Force
Copy-Item (Join-Path $PackDir 'assets\*.png') (Join-Path $Layout 'Assets') -Force

# 生成清单 (替换占位符)
$manifest = Get-Content (Join-Path $PackDir 'AppxManifest.template.xml') -Raw -Encoding UTF8
$manifest = $manifest.Replace('__IDENTITY_NAME__', $IdentityName)
$manifest = $manifest.Replace('__PUBLISHER__', $Publisher)
$manifest = $manifest.Replace('__VERSION__', $Version)
$manifest = $manifest.Replace('__PUBLISHER_DISPLAY_NAME__', $PublisherDisplayName)
$manifestPath = Join-Path $Layout 'AppxManifest.xml'
[System.IO.File]::WriteAllText($manifestPath, $manifest, (New-Object System.Text.UTF8Encoding($false)))

# ---------- 3. 打包 ----------
Write-Host "[3/5] makeappx..." -ForegroundColor Yellow
$sdkBin = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Directory |
    Where-Object { $_.Name -match '^\d' } | Sort-Object Name -Descending | Select-Object -First 1
$makeappx = Join-Path $sdkBin.FullName 'x64\makeappx.exe'
$signtool = Join-Path $sdkBin.FullName 'x64\signtool.exe'
if (-not (Test-Path $makeappx)) { throw "找不到 makeappx: $makeappx" }

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$msix = Join-Path $OutDir ("QuietScreen_{0}_x64.msix" -f $Version)
if (Test-Path $msix) { Remove-Item $msix -Force }
& $makeappx pack /o /d $Layout /p $msix
if ($LASTEXITCODE -ne 0) { throw "makeappx 失败" }

# ---------- 4. 签名 ----------
if (-not $NoSign) {
    Write-Host "[4/5] 自签名 (仅本机测试用; 商店会重新签名)..." -ForegroundColor Yellow
    # 每次生成全新测试证书 (避免旧证书密码混乱)
    $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher -KeyUsage DigitalSignature -FriendlyName "QuietScreen test cert" -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher `
            -KeyUsage DigitalSignature -FriendlyName "QuietScreen Test" `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        Write-Host "  已创建自签证书: $($cert.Thumbprint)"
    }
    $pfx = Join-Path $BuildDir 'quietscreen-test.pfx'
    $pwdBytes = [System.Text.Encoding]::Unicode.GetBytes('vddkeeper')
    [System.IO.File]::WriteAllBytes($pfx, $cert.Export('Pfx', 'vddkeeper'))
    & $signtool sign /fd SHA256 /a /f $pfx /p vddkeeper $msix
    if ($LASTEXITCODE -ne 0) { throw "签名失败" }
    # 导出 .cer 供安装时信任
    $cerPath = Join-Path $OutDir 'QuietScreen-Test.cer'
    [System.IO.File]::WriteAllBytes($cerPath, $cert.Export('Cert'))
    Write-Host "  证书已导出: $cerPath"
} else {
    Write-Host "[4/5] 跳过签名 (-NoSign)" -ForegroundColor Yellow
}

# ---------- 5. 可选: 本机安装 ----------
if ($Install) {
    Write-Host "[5/5] 安装到本机..." -ForegroundColor Yellow
    if (-not $NoSign) {
        $cerPath = Join-Path $OutDir 'QuietScreen-Test.cer'
        $cmd = "Import-Certificate -FilePath '$cerPath' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
        $proc = Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -Command $cmd" -Wait -PassThru
        if ($proc.ExitCode -ne 0) { throw "证书导入失败 (需要管理员)" }
    }
    Add-AppxPackage -Path $msix
    Write-Host "  已安装: 开始菜单搜索 'QuietScreen'"
} else {
    Write-Host "[5/5] 跳过安装 (用 -Install 参数可自动安装)"
}

Write-Host ""
Write-Host "== 完成 ==" -ForegroundColor Green
Write-Host "安装包: $msix"
Write-Host "  本机安装 : powershell -File build-store.ps1 -Install"
Write-Host "  商店上传 : Partner Center > 应用 > 软件包, 上传此 .msix (身份需与 PC 一致)"
