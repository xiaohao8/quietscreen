# 截图与图标资产

## 上传 Partner Center 的文件

| 文件 | 用途 | 规格 |
| --- | --- | --- |
| `shot1-home.png` | 截图 1 · 主页（驱动状态/显示器/一键息屏） | 1920x1080 PNG |
| `shot2-guardian.png` | 截图 2 · 守护（巡检选项/代理守护） | 1920x1080 PNG |
| `shot3-shutdown.png` | 截图 3 · 定时关机 | 1920x1080 PNG |
| `shot4-settings.png` | 截图 4 · 设置（热键/防睡眠/自启） | 1920x1080 PNG |
| `store-logo-300x300.png` | 商店 logo | 300x300 PNG |

## 重要说明

这 4 张截图是按真实 UI **1:1 复刻的预览图**（配色、卡片、布局、文案全部取自
`App.xaml` 与各 View 的实际样式，数值为典型运行状态），可直接用于首次提交。

商店政策要求截图真实反映应用。若想换成 100% 实机截图：
1. 正常打开 QuietScreen 主窗口（不影响已运行的守护进程）；
2. 每页依次按 `Win+Shift+S` 全屏截图（显示器 2560x1600 时先按 `Win+↓` 还原窗口，或直接抓 1920x1080 区域）；
3. 裁到 1920x1080 后覆盖同名文件即可。

## 重新渲染

源文件在 `src/`（ui.css + 4 个 html）。改完后用 Edge 无头渲染（不弹窗口、不影响显示设置）：

```powershell
& "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --headless=new `
  --disable-gpu --hide-scrollbars --window-size=1920,1080 `
  --screenshot="输出.png" "file:///路径/src/shot1-home.html"
```
