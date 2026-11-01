# 生成 QuietScreen 全套图标资产 (1024 主稿 / MSIX logos / app.ico)
# 设计稿与渲染器: design/make_icon.py
# 需要 Python 3 + Pillow + numpy (仅生成图标时需要, 构建应用不需要)
$ErrorActionPreference = 'Stop'

$script = Join-Path (Split-Path -Parent $PSScriptRoot) 'design\export_assets.py'
if (-not (Test-Path $script)) { throw "找不到 $script" }
if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
    throw "需要 Python 3 (Pillow + numpy) 才能重新生成图标; 仓库里已包含生成好的 PNG/ICO"
}

python $script
if ($LASTEXITCODE -ne 0) { throw "图标生成失败 (exit $LASTEXITCODE)" }
Write-Host "图标资产已更新: packaging\assets, src\QuietScreen\Assets, design\out\release"
