<div align="center">

# LGSTrayEx Rings

**A customizable wireless-device battery monitor for Windows 11**

Apple-inspired battery rings · Taskbar display · Multi-device monitoring

[![Windows build](https://github.com/fhzzzs/LGSTrayEx-Rings-Modified/actions/workflows/rings-build.yml/badge.svg?branch=main)](https://github.com/fhzzzs/LGSTrayEx-Rings-Modified/actions/workflows/rings-build.yml)
[![GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue.svg)](LICENSE)
[![Windows 11 x64](https://img.shields.io/badge/platform-Windows%2011%20x64-0078D4.svg)](#download-and-run)

[Download / 下载](#download-and-run) · [Features](#features) · [Compatibility](#device-compatibility) · [中文说明](#中文说明)

</div>

## Overview

LGSTrayEx Rings is an unofficial modification of LGSTrayEx. It brings wireless mice, headsets, keyboards and game controllers into one compact battery display, with customizable rings, smooth animations and device management.

**Status:** Rings.9 is a development build. The CI badge reflects automated Windows build results, not physical-device compatibility or live Explorer stability. The current settings interface uses Chinese and English text.

## Features

| Area | What's included |
| --- | --- |
| Battery rings | Device icons, numeric battery percentages, transparent background and customizable colors |
| Status | Green at 20% and above, yellow at 10–19%, red below 10%, blue for charging and gray for offline by default |
| Animations | Battery transitions, charging highlights, lightning indicator, offline fade-out and spring-style repositioning |
| Layout | Left/right placement, adjustable gaps, DPI-aware sizing and live preview |
| Devices | Automatic discovery, drag-to-reorder, rename, hide, ignore and manual merge/separate |
| Identity | Confirmed hardware identity and reversible unique Logitech HID / G HUB model matching, preferred data source and fallback |
| Notifications | Multiple battery thresholds, repeat intervals, per-device rules, sound selection, volume and WAV import |
| Themes | Light/dark appearance, presets, custom themes, advanced color controls and JSON import/export |
| Quick access | Shared context menu with Settings, Rescan, Manage devices and Exit; optional hover details |
| Packaging | Self-contained .NET portable build; no Windhawk dependency |

## Download and run

1. Open the [Windows build workflow](https://github.com/fhzzzs/LGSTrayEx-Rings-Modified/actions/workflows/rings-build.yml).
2. Select a successful build from the main branch.
3. Under **Artifacts**, download **rings-portable-x64** (GitHub sign-in may be required).
4. Extract the entire archive, including the inner portable ZIP if present.
5. Run `LGSTray.exe`.

Keep the companion executables, DLLs and configuration files together. Exit previous LGSTray instances first.

**Requirements:** Windows 11 x64, a writable application folder and supported hardware. No separate .NET runtime, SDK, Visual Studio Build Tools or Windhawk installation is intended for users of the prebuilt package.

If no successful build or downloadable artifact exists, the portable package is not yet available. Artifacts can expire. Check [Releases](https://github.com/fhzzzs/LGSTrayEx-Rings-Modified/releases) for published versions when available. The GitHub Code → Download ZIP archive contains source code, not a ready-to-run EXE.

## Using the application

Right-click a battery ring or the app's notification-area icon to open the shared menu. Select Settings to personalize the display.

| Page | Purpose |
| --- | --- |
| General | Startup, offline visibility and hover details |
| Personalization | Appearance, layout and animation previews |
| Devices | Order, visibility, names, identity and data sources |
| Notifications | Thresholds, repeat intervals and sounds |
| Themes | Presets, custom themes and configuration import/export |
| About | Version and project attribution |

## Device compatibility

These are implemented reading paths, not a list of devices certified by physical testing in Rings.9.

| Device family | Data source | Notes |
| --- | --- | --- |
| Logitech mice, keyboards and headsets | HID / HID++ and G HUB | Inherited support; depends on model and connection mode |
| Logitech G735 | G HUB / Bluetooth LE | Depends on the exposed battery service |
| ASUS ROG Harpe Ace Mini | OMNI receiver HID | Experimental; automatic discovery, no dedicated settings switch |
| Bluetooth LE peripherals | Standard Battery Service | Requires readable battery information |
| Compatible controllers | Windows Gaming Input / GIP | Reported battery capacity where available |
| XInput controllers | XInput fallback | May report coarse levels instead of exact percentages |

GameSir G7 Pro and other specific controllers still need hardware validation. Battery accuracy, charging status and connection-mode support depend on the hardware and drivers.

Automatic device merging is enabled by default. Confirmed hardware identifiers take priority. A unique Logitech HID / G HUB pair with the same canonical model may also be grouped reversibly when there are no conflicting IDs or manual separation rules. Multiple records from either provider prevent inferred matching. Inferred links are not saved as confirmed hardware identity. Remembered offline records preserve grouping during rescan. Manual merge and separate remain available.

## Experimental native taskbar layout

Rings.9 includes a bundled native provider, `NativeLayout.dll`, intended to reserve space after the Start button when Left is selected. It uses Windows XAML Diagnostics, not Windhawk. Internal Explorer layouts can change between Windows releases.

The provider uses a heartbeat-based reservation lease, attempts to restore modified layout properties, and checks actual screen geometry before showing rings. Incompatible layouts fall back to a safe right-side gap or hide the strip when no safe space exists.

The initial target is a single-monitor Windows 11 x64 system with a horizontal primary taskbar. Centered mode, DPI, crowded taskbars, Explorer recovery and native placement still require live testing. Test in a disposable VM before using the experimental feature on your main desktop.

See [NativeLayout documentation](NativeLayout/README.md) for limitations and recovery details.

## Building from source

Only developers need build tools. On Windows x64, run:

```bat
BUILD-RINGS.cmd -Test
```

The build detects .NET 10 SDK, MSVC x64 and Windows SDK/C++/WinRT headers. Missing tools may require installation and network access. The same build runs in GitHub Actions.

Output: `bin/LGSTrayEx-Rings-Portable-x64.zip`

See [installation and build notes](INSTALL.md).

## Feedback and troubleshooting

Open an [issue](https://github.com/fhzzzs/LGSTrayEx-Rings-Modified/issues) with your Windows version, device model, connection mode, build commit, steps to reproduce and relevant screenshots or logs. For layout issues, include taskbar alignment, DPI and the native-layout status shown in Settings. Remove personal information and device identifiers from logs before posting.

Settings and diagnostic logs are stored under `%LOCALAPPDATA%\LGSTray`. Windows 10, ARM64 and multi-monitor taskbar support are not established.

## Credits and license

Modified by [fhzzzs](https://github.com/fhzzzs).

Based on [strain08/LGSTrayEx](https://github.com/strain08/LGSTrayEx), originally derived from [andyvorld/LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery). Thanks to the original authors and contributors.

This is an unofficial modification, not endorsed by Logitech, ASUS, Microsoft, Apple or the upstream authors. Distributed under [GPL-3.0](LICENSE). Original notices are retained. See [modification history](MODIFICATIONS.md) and [upstream documentation](Docs/UPSTREAM-README.md).

---

## 中文说明

LGSTrayEx Rings 是一款 Windows 11 无线设备电量监控工具，以圆环形式在任务栏展示鼠标、耳机、键盘和手柄的电量。

- 支持自定义圆环颜色、大小、间距、充电流光和上下线动画。
- 支持自动发现设备、可靠身份及唯一罗技跨来源型号自动合并、拖拽排序、隐藏及重命名。
- 支持多档低电量提醒、独立提示音与主题保存。
- ROG Harpe Ace Mini OMNI 读取在后台自动执行，无须专用设置开关。
- 内置原生任务栏占位仍为实验性功能；不兼容时回退至安全位置。

**下载方式：** 前往 [GitHub Actions](https://github.com/fhzzzs/LGSTrayEx-Rings-Modified/actions/workflows/rings-build.yml)，选择 main 分支成功的构建，下载 Artifacts 中的 rings-portable-x64，完整解压后运行 LGSTray.exe。普通用户不需要安装 .NET SDK、Visual Studio Build Tools 或 Windhawk。

当前是开发测试版本，具体设备兼容性及原生布局需实机验证。欢迎通过 Issues 反馈问题。

**作者：** [fhzzzs](https://github.com/fhzzzs) · GPL-3.0 · 保留原项目版权与署名。
