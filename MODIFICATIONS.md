# Battery Monitor – unofficial modification (2026-11-23)
Base: strain08/LGSTrayEx 3.2.6, a fork of andyvorld/LGSTrayBattery. Original notices and GPL-3.0 license retained. This package includes source. No code from ZGMFX01A/Wireless-Device-Battery-Monitor is included in the new changes. Original authors do not endorse this build.

Implemented in source, not Windows-compiled or hardware-verified: WPF custom settings window, shared preview/taskbar ring renderer, RGB/HEX/alpha settings, local JSON save/import/export, configurable layout, online/offline animations, staggered spring movement, device ordering via drag-and-drop or up/down buttons, hide/rename, multiple notification thresholds with intervals and per-device overrides, WGI battery reports with XInput fallback, BLE polling, full hit rectangle, global Ctrl+Alt+B settings shortcut. WGI percent is computed from reported capacities, not invented. Some providers may return unknown level. Notifications are WPF popup windows, not Windows Notification Center. Existing non-battery notifications remain controlled by upstream TOML.

Limitations: the current right-side strip is a safe taskbar overlay. When native buttons overlap its area, it hides. UI Automation support varies by Windows release; primary display only. Undo/redo, separate automatic theme switching, native notifications, complete app update system and true taskbar reflow are not implemented. G7 Pro precise percent, cross-protocol identity matching, all animation timings and hit testing require testing on physical Windows. The preview uses simulated devices and the same renderer/animation class. XInput fallback is used only when no WGI gamepads are found, avoiding duplicates at the expense of possible missing XInput-only devices in mixed configurations. SDK install requires internet.

Build on Windows x64: double-click BUILD-RINGS.cmd; it detects the x64 .NET 10 SDK and installs a local copy from Microsoft's official site if missing. Self-contained portable output: bin/Release/Publish/win-x64/Standalone and bin/Battery-Monitor-Windows-x64.zip. Optional BUILD-INSTALLER.cmd builds the existing WiX MSI with a separate upgrade identity; untested. Close prior LGSTray processes before running. Settings persist under %LOCALAPPDATA%/LGSTray/rings-settings.json. For GPL redistribution, distribute corresponding source and license alongside binaries. No EXE is supplied in this source ZIP.

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
- Safe taskbar gap search. This is not Explorer layout integration; native buttons cannot be moved by this implementation.
- Windows build and interactive acceptance remain required.

## Rings.5
Modified by fhzzzs (https://github.com/fhzzzs). Charging ring effects, drag reorder, notification cards, G735 G HUB/BLE recognition and experimental Harpe Ace Mini Omni receiver status reading. See RINGS5-CHECKLIST.md.

## Rings.6
- Provider identity grouping, manual aliases and split/ignore controls; fixed layout preview; removed left-click detail popup and added shared context menu for rings and app tray icon.

## Rings 10 — right-only layout

- Single right-side strip, asynchronous UI Automation collision checks and safe hiding when the trailing gap is insufficient.
- Removed obsolete alignment preferences, native layout code, build requirements and related tests.
- Device identity grouping, source selection, lifecycle, notifications, versioned settings and update staging are retained.
- Prior experimental versions remain in Git history; see the current README and INSTALL.md for supported behavior.

## v1.0.3 — public branding and direct ZIP
Public product and executable names are Battery Monitor. Original upstream credits, license, internal assembly identities and the existing settings directory remain intact.

## v1.0.4 — device identity and performance
Normalize embedded NUL characters in controller IDs and stored identity keys; verify complete clipboard text. For observed GameSir G7 Pro 3537:10A9, use the raw Windows Gaming Input battery report, with XInput fallback. Reversible single-model history grouping is disabled after simultaneous same-model devices are observed; no serial identity is fabricated. Cache animation-frame snapshots and drawing pens, reduce unnecessary device subscriptions and grouping comparisons, defer initial shell placement until after dispatcher startup. Real hardware readings and long-running performance remain to be measured.

## v2.0.0 — integrated compatibility expansion
Stages 1.1–1.5 developed without intermediate branch builds: protocol allowlist, expanded ASUS status readers, safer Logitech HID++ 2.0 fallback, controller naming/Windows battery paths, read quality and disagreement diagnostics, bounded session history and adaptive scheduling. Included source references, local hardware discovery and a 168-hour sampler. Real-device coverage and resource targets remain unverified. See DEVICE-COMPATIBILITY.md.

## 2.0.0 completion checkpoint (unbuilt)
Additional ASTRO A50 Gen 5 and ASUS direct keyboard/headset adapters; scoped HID fallback identity, managed hotplug queue, exact register matching, Centurion SOC validation, battery freshness, shell probe backoff, offline cleanup, local display catalog. See COMPLETION-CHECKLIST.md for open items. Version remains 2.0.0.

## 2.0.0 completion checkpoint 2 (unbuilt)
Passive native Sony/Nintendo/8BitDo report readers; scoped legacy 0x0D fallback; expanded ASUS profiles and Cetra components; WGI placeholder validation; confirmed-history retention; data-only online catalog and independent provider switches. Refer to COMPLETION-CHECKLIST.md. No version bump or Windows validation.

## 2.0.0 completion checkpoint 3 (unbuilt)
GameSir G7 edition IDs and safe WGI fallback, Cyclone 2 live report parser and opt-in status request; sleep/resume scan control, UIA caching/metrics, actual HID reader completion, partial handle cleanup, stable ring geometry caching, timestamp-only rendering path, startup phase instrumentation, and CSV resource delta analysis. 22 and 30–35 advanced; no build or hardware acceptance claimed.
