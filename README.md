# Quiet · 屏息

**Screen off. Work on.** —— 屏幕熄了，活照干。

Quiet keeps one virtual display permanently alive on your Windows laptop, so you can switch the built-in screen off and walk away while everything keeps running — then reconnect remotely whenever you like. Built for the dorm-room "screen-off remote control" workflow.

Quiet 让 Windows 笔记本常驻一块虚拟显示器：离开时一键息屏（内屏真断电），任务照跑、可选代理守护、定时关机；回到电脑前一键点亮。为宿舍息屏远控场景而生。

---

## Features · 特性

- **常驻虚拟屏** — one virtual display kept alive by a 100 ms driver heartbeat; mode switching is pure topology change (instant)
- **双模式状态机** — Local（本机：内屏点亮，程序不改拓扑）/ Night（夜间远控：内屏断电）+ 手动切换（无自动切换）
- **秒级切换** — `Ctrl+Alt+Esc` 息屏远控 / `Ctrl+Esc` 点亮内屏
- **远程桌面尺寸自动校正** — 夜间模式把常驻虚拟屏固定为 `2560x1600@60`，并校正到 150% 缩放，避免只剩 `1280x800` 导致下半屏内容显示不出来
- **可选远控代理守护** — 默认关闭；用户自行指定本机远控程序路径，掉线后自动拉起
- **每日定时关机** — daily scheduled shutdown with system countdown and one-click cancel
- **防睡眠** — blocks sleep/hibernate/display-timeout while running (restores on exit)
- **睡眠唤醒保护** — displays are cleanly unplugged before sleep and restored after wake
- **开机自启（可选，默认关闭）** — 打包版由系统“启动应用”管理
- **驱动一键安装** — bundles the official Parsec VDD driver installer (silent install)
- **托盘常驻 + Windows 通知 + 日志**

## 热键 · Hotkeys

| 热键 | 作用 |
| --- | --- |
| `Ctrl+Alt+Esc` | 息屏远控：切换到虚拟屏，内屏断电 |
| `Ctrl+Esc` | 点亮内屏：回到本机模式 |

## 工作原理 · How it works

```
                Ctrl+Alt+Esc / 一键息屏
   ┌────────┐ ─────────────────────────────────→ ┌──────────┐
   │ Local  │                                     │  Night   │
   │ 本机模式 │ ←───────────────────────────────── │ 夜间远控  │
   └────────┘              Ctrl+Esc                └──────────┘
```

- **Local**: internal screen on; the app does not touch your display topology; agent keepalive only.
- **Night**: virtual display active + internal screen powered off; input anti-touch ready; auto-repair if the display drops. The app also repairs the virtual display mode and DPI scale so the remote desktop never falls back to `1280x800`; the minimum target is about `1707x1067` at 150% scale.

The virtual display is kept alive by a heartbeat (`IOCTL_UPDATE` every 100 ms). If the heartbeat stops, the driver watchdog removes it — so on app exit the system returns to a clean state automatically.

## 依赖 · Requirements

- Windows 10 (build 19041+) / Windows 11, x64
- [Parsec Virtual Display Driver](https://github.com/nomi-san/parsec-vdd) — 首次使用点应用内"一键安装"（内置官方安装包）

## 构建 · Build

```
dotnet build src/QuietScreen/QuietScreen.csproj -c Release -r win-x64 --self-contained
```

产物在 `src/QuietScreen/bin/Release/.../win-x64/`，免安装直接运行。

当前远程画面尺寸异常时，也可以手动执行一次修复：

```
QuietScreen.exe --fix-display
```

## 致谢 · Credits

- [nomi-san/parsec-vdd](https://github.com/nomi-san/parsec-vdd) (MIT) — driver control IOCTLs ported from this project
- [blankrun](https://github.com/hongfaqiu/blankrun) (MIT) — anti-touch input-interception design
- Parsec Cloud Inc. — official virtual display driver package

## License · 许可

[MIT](LICENSE)
