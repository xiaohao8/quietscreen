# 07 · Edge 提交操作单（手把手版）

> 用法：在**你自己的 Edge**（已登录 Partner Center）里按顺序执行。每一步写明：点什么、填什么、贴什么。
> 需要我离线做的都标了【我做】；需要你提供的标了【你给】。遇到和描述不符的界面随时停下来问我。
>
> **当前状态（2026-11-11 23:41 · v1.0.3.0 · 启动恢复启发式修正）**
> ✅ 正式包已重建：`packaging/out/QuietScreen_1.0.3.0_x64.msix`（付志豪 ✓ / DisplayName "QuietScreen 屏息" ✓ / 三重校验通过）——软件包页**删旧包、传这个新包**
> ✅ 图标全套已应用；⚠️ 设备系列只勾 Windows 桌面；⚠️ 隐私政策 URL：https://quiet.202271.xyz/privacy.html（待域名 CNAME 生效后即为可访问）
> ❌ 被拒教训已修：①PublisherDisplayName 必须与账户"发布者显示名"一致 ②清单 DisplayName 必须匹配预留名"QuietScreen 屏息"
> ✅ 联系邮箱已定：3543601929@qq.com；隐私政策已随官网部署（见第 3 步）

---

## 第 0 步 · 账号状态确认【你答】

进入 Partner Center 后告诉我你看到哪种：
- A. 能正常进仪表盘（说明开发者账户已注册好）→ 直接第 1 步
- B. 要求你注册/付费（一次性 $19 或 $99）→ **付款和身份验证只能你本人完成**，完成后告诉我
- C. 卡在别的提示 → 截图发我

## 第 1 步 · 创建应用并预留名称【你点】

1. 左侧 **应用和游戏 → 概述**（Apps and Games → Overview）→ **新建应用**（New app）
2. 保留名称输入：`QuietScreen 屏息`
3. 创建后进入应用页

## 第 2 步 · 把应用身份发给我【你给 → 我做】

1. 左侧 **应用管理 → 应用标识**（App management → App identity）
2. 把这两个值原样复制发我：
   - **包/标识名称**（Package/Identity Name，形如 `12345xxxx.QuietScreen`）
   - **包/标识发布者**（Package/Identity Publisher，形如 `CN=XXXXXXXX-...`）

【我做】收到后立即执行（约 2 分钟）：

```powershell
powershell -File packaging/build-final.ps1   （身份/版本已内置，直接产出正式包）
```

产出 `packaging/out/QuietScreen_1.0.3.0_x64.msixupload` —— 这就是上传文件。**（已用真实身份重建完成（build-final.ps1，三重校验通过），直接上传新的 .msix 即可）**

## 第 3 步 · 隐私政策 URL【你给 → 我做】

【已完成】隐私政策随官网部署（site/privacy.html，联系邮箱 3543601929@qq.com）。
正式 URL：https://quiet.202271.xyz/privacy.html（域名 CNAME 生效后即为正式）；过渡 URL：Netlify 控制台里的站点域名 + /privacy.html。将任一 URL 填入下方表格。

## 第 4 步 · 开始"提交"并上传软件包【你点 + 你选文件】

1. 左侧 **提交 → 1 软件包**（Packages）→ **上传** → 选 `packaging\out\QuietScreen_1.0.3.0_x64.msixupload`
2. 等验证通过（绿勾）。若报身份不符 → 把错误原文发我

## 第 5 步 · 商店列表【你点 + 贴文本】

进入 **商店列表（Store listing）**，语言选 **中文(简体)**：

| 字段 | 贴这段 |
| --- | --- |
| 产品名称 | QuietScreen 屏息 |
| 说明 | （贴 03-商店文案.md 第 2 节"应用说明"整段，从 **屏幕熄了，活照干。** 到最后一行） |
| 新功能说明 | `1.0.3.0 首发：虚拟屏常驻保活、本机/夜间双模式秒切、守护巡检与代理拉起、远程桌面尺寸自动校正、每日定时关机、防睡眠与睡眠唤醒保护、驱动一键安装。` |
| 版权 | `© 2026 QuietScreen` |
| 隐私策略 URL | （第 3 步产出的 URL） |
| 网站等信息 | 可填 GitHub 仓库地址 |

**截图**：点上传，依次选（本 kit 内）：
- `screenshots/shot1-home.png`
- `screenshots/shot2-guardian.png`
- `screenshots/shot3-shutdown.png`
- `screenshots/shot4-settings.png`

## 第 6 步 · 属性与分级【你点】

- 类别：**实用工具**（Utilities）；子类别：**工具**（Tools）
- 年龄分级问卷：全部答"否"→ IARC 3+/PEGI 3

## 第 7 步 · 定价【你点】

- 价格：**免费**；市场：全部（默认）；发布日期：认证通过后立即发布

## 第 8 步 · 认证备注【你贴】

提交流程最后一页 **Notes for certification**：整段粘贴 `05-认证审核备注.md` 全文（从"本应用（QuietScreen 屏息）…"到英文摘要结束）。

## 第 9 步 · 提交【你点，点之前告诉我】

全部绿勾后，**提交审核按钮按下前说一声**，我最后帮你核对一遍清单再点。

---

## 提交后

- 审核 1~7 天；被拒看错误码：`10.2.x` → 走 02 文档方案 B（去内嵌驱动、改跳转）
- 通过后商店页几小时内全球生效

## 提交前 60 秒核对清单（全绿再按提交）

- [ ] 软件包页：旧包已删，新包 `QuietScreen_1.0.3.0_x64.msix`（2026-11-12 01:27 版）验证绿勾
- [ ] 设备系列：**只勾 Windows 桌面**（Xbox/Holographic/Team 全不勾）
- [ ] 商店列表：说明/新功能/截图 4 张/版权/隐私 URL 全部非空
- [ ] 语言里加了 English (United States) 并填了英文描述（审核友好，可选但强烈建议）
- [ ] 属性：实用工具/工具；分级问卷已提交
- [ ] 定价：免费；市场全部
- [ ] 认证备注：05 文档全文已贴
- [ ] 点"提交审核"
