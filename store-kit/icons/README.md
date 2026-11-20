# 图标提案库（含历轮备份）

> **最新总览**：`icon-overview-final.png`（二三家族 7 个·三轮打磨后，含 44px/24px 小尺寸验证）
> 源文件在 `src/*.svg`（1024 画布），成品同名 `.png`（1024×1024）。
> **历轮原图全部保留**在 `archive-v2-originals/`（含 round3 首稿与 round3-v1 备份），未删除任何文件。

## 三轮打磨记录（2026-11-09）

1. **P2 光影精修**：单层 bloom → 双层（紧致亮核 stdDev7 + 宽阔柔光 stdDev26）；月面渐变改为真球体明暗（#FFFDF2→#FCE3AC→#E2A050，高光偏左上）；玻璃卡加底部内阴影（厚度感）；环境光加宽至 r380
2. **P3 构图精修**：月牙放大并做光学下移对位（框内视觉居中）；投影减重（dy12/std15/0.32）；P3-D 修复镂空月与背景框穿帮——改为前屏实心 + 月牙直填底色渐变（userSpaceOnUse 无缝对齐）
3. **总验收**：24px 小尺寸全部可辨识；总览重制为 `icon-overview-final.png`

## 最终定稿（2026-11-09）

**用户选定 monogram-a（几何 Q + 屏幕芯片 + 月点）**，已从根目录 logo.png 全量应用：packaging/assets（41 文件）+ src/QuietScreen/Assets（42 文件，含多尺寸 app.ico）+ 商店 logo 300×300。旧资产备份于 archive-v2-originals/old-packaging-assets/。

## 当前主推 · 二三家族（2026-11-09 拓展打磨）

### P2 · 夜窗透月（3 个）— Fluent 亚克力 + Apple 分层光影

玻璃"屏卡"上开一弯月洞，月光自带 bloom 外溢到卡面；顶部落影 + 镜面边光 + 环境辉光。

| 文件 | 说明 |
| --- | --- |
| `p2-eclipse-a.png` | **暖金透月**（推荐主力）：金月洞 + 金色光晕，暗夜蓝卡 |
| `p2-eclipse-b.png` | **冰蓝透月**：冷色变体，冰蓝月光 |
| `p2-eclipse-c.png` | **双层窗**：后层幽灵卡 + 前层透月卡，景深版（Apple 分层） |

### P3 · 剪影双生（4 个）— Material 3 大胆几何，负空间月，极限缩放

| 文件 | 说明 |
| --- | --- |
| `p3-silhouette-a.png` | 靛蓝底 · 白色显示器框 + 白月 + 底座 |
| `p3-silhouette-b.png` | 翠绿底 · 同构 |
| `p3-silhouette-c.png` | **反白**：深夜蓝底 + 实心白屏，月牙为镂空负空间 |
| `p3-silhouette-d.png` | **双屏叙事**：后屏幽灵框（半透明）+ 前屏实心熄屏见月 —— 讲述"内屏熄灭、虚拟屏接管"的产品故事 |

## 此前轮次（全部保留）

- **精雕 v2**（`moonlit-a/b`、`monogram-a/b`）：真 squircle 月屏与几何 Q，总览见 `icon-overview.png`
- **第三轮探索稿**（`p1-aurora-*` 极光玻璃、`p4-spotlight-*` 暗夜聚光、`p5-clay-*` 软3D）：保留作参考，本轮评估后暂列二三之后
- **第一轮草稿**（`pulse-*`、`aegis-*`、`standby-*`）：仅存档

## 设计法则依据

- Apple：连续曲率 squircle、单光源（顶部镜面高光 + 落影 + 暗角）、分层景深、单一焦点 —— [Apple HIG](https://developer.apple.com/design/human-interface-guidelines/app-icons) / [WWDC25 新图标体系](https://developer.apple.com/videos/play/wwdc2025/220) / [WWDC17](https://developer.apple.com/videos/play/wwdc2017/301)
- Material 3：大胆几何剪影、单图形、拒绝细节堆砌 —— [m3.material.io](https://m3.material.io/styles/icons/designing-icons)
- Fluent 2：层次、光照、亚克力材质 —— [fluent2.microsoft.design](https://fluent2.microsoft.design)

## 采用为正式图标（4 步）

1. 选定后以对应 1024 PNG 为新主稿（或把 `src/*.svg` 移植进 `design/make_icon.py`）；
2. 跑 `packaging/make-assets.ps1` 重新生成全套 `packaging/assets`；
3. 同步替换 `src/QuietScreen/Assets/app.ico`；
4. 商店 logo 300×300 由主稿缩出。
