# LGSTrayEx Rings – unofficial modification (2026-11-23)
Base: strain08/LGSTrayEx 3.2.6, a fork of andyvorld/LGSTrayBattery. Original notices and GPL-3.0 license retained. This package includes source. No code from ZGMFX01A/Wireless-Device-Battery-Monitor is included in the new changes. Original authors do not endorse this build.

Implemented in source, not Windows-compiled or hardware-verified: WPF custom settings window, shared preview/taskbar ring renderer, RGB/HEX/alpha settings, local JSON save/import/export, configurable layout, online/offline animations, staggered spring movement, device ordering via drag-and-drop or up/down buttons, hide/rename, multiple notification thresholds with intervals and per-device overrides, WGI battery reports with XInput fallback, BLE polling, full hit rectangle, global Ctrl+Alt+B settings shortcut. WGI percent is computed from reported capacities, not invented. Some providers may return unknown level. Notifications are WPF popup windows, not Windows Notification Center. Existing non-battery notifications remain controlled by upstream TOML.

Limitations: left/right is a safe taskbar overlay, not actual Explorer taskbar reservation. When native buttons overlap its area, it hides. UI Automation support varies by Windows release; primary display only. Undo/redo, separate automatic theme switching, native notifications, complete app update system and true taskbar reflow are not implemented. G7 Pro precise percent, cross-protocol identity matching, all animation timings and hit testing require testing on physical Windows. The preview uses simulated devices and the same renderer/animation class. XInput fallback is used only when no WGI gamepads are found, avoiding duplicates at the expense of possible missing XInput-only devices in mixed configurations. SDK install requires internet.

Build on Windows x64: double-click BUILD-RINGS.cmd; it detects the x64 .NET 10 SDK and installs a local copy from Microsoft's official site if missing. Self-contained portable output: bin/Release/Publish/win-x64/Standalone and bin/LGSTrayEx-Rings-Portable-x64.zip. Optional BUILD-INSTALLER.cmd builds the existing WiX MSI with a separate upgrade identity; untested. Close prior LGSTray processes before running. Settings persist under %LOCALAPPDATA%/LGSTray/rings-settings.json. For GPL redistribution, distribute corresponding source and license alongside binaries. No EXE is supplied in this source ZIP.

## rings.2 theme hotfix
Removed hard-coded light settings background and enabled window-local Fluent System theme; secondary text uses the Fluent theme resource. Preview buttons use a full-size BatteryRing control template matching the taskbar rendering approach. No changes to saved settings or device protocols. Windows visual verification is still required.

## rings.3 settings rework
Six visible navigation entries: General, Personalization (Appearance/Layout/Animation), Devices, Notifications, Themes, About. Appearance preview pinned above the scroll region, with simulated level/offline state and large/actual sizes; animation-only preview has playback, and other pages have no global preview. Theme library uses separate rings-themes.json, includes named snapshots of appearance/layout/animation and device colors, preserves device ordering and alert settings on application. Per-device colors moved to Appearance and per-device alert rules to Notifications. Device rows use drag handles with insert-before/after drops instead of arrows; ring strip reuses its spring layout on order changes. Persistent single application icon in Windows notification area regardless of device count (Windows decides its overflow/pinned location). Left click device opens a single-device card, right click opens Settings. Settings palette uses explicit matched light/dark window colors and WPF theme mode; Windows build/visual verification pending.

Still outstanding: pixel-perfect comparison on a Windows system, continuous dragged-card following/animated insertion inside Settings (drag/drop with target highlight is implemented), true native Explorer reflow/reservation, undo/redo, fully native notifications, multi-monitor support and G7 Pro hardware validation. Theme preview thumbnails use the shared ring renderer. No binary included; no Windows compilation or automated test execution performed in the authoring environment.

## rings.4 usability and feedback fixes
- Wrap-panel theme library and compact width-constrained settings content.
- Numeric entry beside sliders; grouped appearance advanced controls; fixed appearance preview retained.
- Light preview passes its contrast mode to the shared ring renderer; taskbar uses system theme with optional override.
- Independent device color fields, legacy ring color compatibility, deep-copy theme snapshots.
- Original synthesized notification sounds, local WAV import, per-threshold and per-device audio settings and audition.
- Bidirectional battery animation with animated numeric label.
- Safe taskbar gap search in either direction. This is not Explorer layout integration; native buttons cannot be moved by this implementation.
- Windows build and interactive acceptance remain required.

## Rings.5
Modified by fhzzzs (https://github.com/fhzzzs). Charging ring effects, drag reorder, notification cards, G735 G HUB/BLE recognition and experimental Harpe Ace Mini Omni receiver status reading. See RINGS5-CHECKLIST.md.

## Rings.6
- Provider identity grouping, manual aliases and split/ignore controls; fixed layout preview; removed left-click detail popup and added shared context menu for rings and app tray icon.

## Rings.7 — experimental native left reservation
Optional Windhawk companion in NativeLayout (separate manual installation; no automatic injection). Shared-memory width lease with heartbeat and native Start margin restoration, UI Automation collision validation, configurable native-left option, and target-layout preview. Acknowledgement does not guarantee usable space; safe positioning remains the fallback. x64 horizontal primary taskbar, single monitor only for the companion. The mod uses an internal XAML interface; Windows/Windhawk compilation and real layout tests remain outstanding. See NativeLayout/README.md.

## Rings.8 (source revision)
- Removed Windhawk-specific bridge and installation scripts; no native Explorer slot implementation is supplied in this revision. The original bridge reference and historical notes remain in the prior version and Docs/CHANGE-HISTORY.md. Actual positioning is still collision-safe.
- Corrected simulated left anchor independent of centered application icons; extracted layout geometry helper.
- Persisted automatic identity aliases separately from explicit user merges; optional Logitech serial field from native HID and G HUB (if reported) is propagated. Model-only matches remain ambiguous.
- Shared themed 220-DIP context menu with device count and Manage devices navigation.
- Single build entry, self-contained x64 portable packaging; removed duplicate publish scripts, legacy MSI and framework-dependent deployment profiles. Retained original license, notices, upstream README and tests. Windows runtime validation pending.

## Rings.9 — built-in native-layout experiment
- Adds NativeLayout.dll source and automatic source-build integration (MSVC x64 / Windows SDK, static C++ runtime). No Windhawk dependency.
- XAML Diagnostics provider targets the primary Explorer StartButton/TaskbarFrameRepeater, attempts actual trailing-margin reservation and left anchoring; expires its lease after 2.5 seconds and restores owned properties.
- Managed code validates live geometry, reports native status in Layout, and falls back to safe right positioning on incompatibility.
- Windows build and runtime validation have not been executed in the Linux authoring environment. Centered native mode, hit testing, other taskbar controls and recovery are release-test requirements, not verified features.
