# Quiet · 屏息 —— 图标设计说明（已实现）

> 最终设计：**Night Screen / 熄屏之夜**
> 主稿：`../../outputs/屏息-QuietScreen/icon/icon-1024.png`
> 规格与预览：`../../outputs/屏息-QuietScreen/icon/README.md`

## 产品

- 名称：Quiet · 屏息（Windows 桌面应用）
- 功能：让笔记本内置屏幕拓扑级断电，通过常驻虚拟显示器继续远程控制
- 气质：安静、夜晚、克制、可靠

## 设计

- **主体**：一台打开的笔记本，屏幕是真正的黑；屏幕右上角一弯暖月。
- **风格**：Apple / macOS Big Sur 语言 —— 连续曲率超椭圆（n=5）、单一光源、
  克制的午夜靛蓝渐变、白色机身只有极轻的纵向渐变，无描边、无文字、无拟物高光。
- **配色**：背景 `#2A3374 → #030408`；机身 `#FFFFFF → #DCE0F2`；
  屏幕 `#070913 → #0A0C1D`；月亮 `#FFE2A8`。
- **光学尺寸**：16–48 px 使用单独的主稿（更粗 bezel、更大月亮、更满的 squircle），
  不做 1024 直接缩。
- **禁忌**：不加星星、不加“zzZ”、不加文字、不堆高光。

## 文件

- 1024 主稿、512/256/128/64/48/32/16、`app.ico`、Store logos、宽磁贴：
  `packaging/assets/`
- 设计源与导出脚本：`design/make_icon.py`、`design/export_assets.py`
- 重新生成：`packaging/make-assets.ps1`（需要 Python 3 + Pillow + numpy）
