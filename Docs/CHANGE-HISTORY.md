# Historical development notes (not current install instructions; earlier status claims are not test results)

# Rings.3 Windows acceptance checklist

Build with BUILD-RINGS.cmd. Keep existing %LOCALAPPDATA%/LGSTray settings. Close old instances before launching LGSTray.exe. Back up rings-settings.json and rings-themes.json before testing import/reset.

| Check | Expected |
|---|---|
| Start settings in Windows light and dark | Readable foreground and consistent control backgrounds |
| Sidebar | Six visible entries; Personalization contains three sub-tabs |
| Appearance | Pinned large and actual-size previews remain visible when scrolling; adjust HEX/RGBA, sliders, simulated battery and offline; actual rings reflect settings |
| Appearance advanced | Device-specific ring colors only here; no duplicate controls in Devices |
| Layout | Left/right safe overlay, gaps/divider; no global preview |
| Animation | Playback of disconnect, connect, battery change, loop; advanced timing sliders; page change stops preview |
| Devices | Drag handle before/after a target to reorder; no arrows; taskbar order updates; reconnect preserves order |
| Notifications | Global and per-device rule editor, multiple thresholds, repeat minutes |
| Themes | Save name, apply, rename, delete, import/export; applying does not change order/hidden devices or notification rules |
| Tray | One application icon with zero or more online devices; no per-device tray icons; right-click menu opens settings; Windows controls overflow location |
| Rings | Left click opens clicked device details; right click opens settings; outside click closes details |

Source-only review; not a completed Windows acceptance test. Existing limitations are listed in MODIFICATIONS.md. Drag/drop target highlight is present; continuous animated card insertion is pending. Native taskbar reflow remains unimplemented.


---

# Rings.4 Windows acceptance checklist

Source changes only; Windows build/test not executed in the packaging environment.

- [ ] Compile using BUILD-RINGS.cmd, then launch the published EXE.
- [ ] Theme cards wrap at narrow/wide window sizes, no giant row or spread-out buttons. Save/apply/rename/export a named theme.
- [ ] Toggle preview background dark/light; icon, number and track should use contrasting colors when auto contrast is enabled. Turn it off to use manually set colors.
- [ ] Scroll below fixed preview. Expand advanced settings and type values in numeric boxes; Enter or focus loss commits clamped values. Drag sliders and compare.
- [ ] Appearance > independent device colors: expand, select device, choose preview device; enable individual overrides, adjust separate icon/text/normal/low/critical/charging/offline/track colors. Disable override and confirm inheritance. Save theme and reapply.
- [ ] Notifications: enable sound (older settings retain previous setting). Select different sound per threshold, audition, change volume, import WAV and audition again. For Windows Default system controls volume.
- [ ] Test real threshold crossing and repeat, and stop when charging. Verify global/device audio fallback.
- [ ] Animations: play 85 to 5, 5 to 85, and round trip. Number and ring should animate. Verify existing offline and reconnect demos.
- [ ] Layout: test left/right with different app counts; safe gap selection should avoid occupied buttons. Left-aligned Windows apps are NOT shifted; if no gap fits the strip is hidden. Native space reservation remains outstanding.
- [ ] Restart and verify stored settings and old named themes are retained. Imported sound assets stay in local app data; JSON export does not bundle audio files.


---

# Rings.5 — Modified by fhzzzs (https://github.com/fhzzzs)

Source changes, not a verified Windows release. Run BUILD-RINGS.cmd and test on Windows 11.

- Device grip dragging: live slot exchange on midpoint crossing, spring movement of neighboring cards, mouse capture and auto-scroll; test first-to-second and reverse.
- Notifications: compact rule cards, per-rule sound, volume, WAV import/preview, validation and autosave.
- About: modification credit with GitHub link; original project credit and GPL retained.
- Charging: centered glyph and percentage; blue default ring, clipped moving highlight, separate bolt; no animation at 100%; custom colors, size, position, opacity, speed and length. Confirm charging state is reported by device.
- Battery demo: one round-trip control, separate charge start/stop/full controls.
- G735: G HUB provider enabled in bundled appsettings.toml; requires installed/running G HUB for LIGHTSPEED when native HID is unavailable. If retaining older appsettings.toml, set [GHub] enabled = true and restart. BLE Battery Service is a fallback only if exposed. Native G735 USB protocol not verified or newly implemented. No guessed percentage.
- ROG Harpe Ace Mini: experimental direct OMNI receiver 0B05:1ACE reader; verifies paired PID 1B65, queries mouse channel only, reads percentage and charging. Background polling, rejects malformed/standby responses. Other receiver models/cable firmware are not verified. BLE Battery Service fallback if exposed. If conflict with vendor utilities, disable ROG OMNI reader in Devices > Advanced.
- References for interoperable protocol facts (no third-party source code copied): https://github.com/HeyOkay/HaloBattery/blob/main/docs/protocols.md and https://github.com/seerge/g-helper/discussions/4321
- Existing limitation: overlay placement finds safe free space; it cannot reserve native Windows taskbar slots or push native icons.
- Run regression tests on Windows: dotnet test LGSTrayUI.Tests/LGSTrayUI.Tests.csproj. Tests provided but not executed here.


---

# Rings.6 — source changes and Windows acceptance checklist

Modified by fhzzzs. Original project copyright and GPL-3.0 retained.

- Device identity: provider records, explicit aliases, confirmed physical IDs (where available), preferred source with valid-reading fallback, ignore/restore, split and manual merge. Same model name alone does not prove identity. Current providers do not always expose a common hardware identifier; manual merge remains necessary in ambiguous cases. XInput remains a fallback slot; stale offline XInput entries are excluded from the ordinary list while GIP is active but remain available in identity management.
- Layout: fixed preview header only on Layout page, shared safe-gap positioning and BatteryRing renderer, simulated native apps, taskbar theme, and device count. Preview is a simulation, not an Explorer reservation. No native application-icon displacement implemented.
- Taskbar interaction: removed detail popup and left-click action; optional hover tooltip retained. Native WPF context menu on device rings and the persistent app tray icon: Settings, Rescan Devices, Exit App. Extra providers are also rescheduled by the rescan action.
- Old user settings remain in the same data directory. Legacy Logitech model-based settings migrate only when a unique provider record can be identified; ambiguous records are not merged by name.

## Windows checks (not executed here)
1. Build with BUILD-RINGS.cmd, verify app starts and old settings survive.
2. Right-click ring at top/bottom/center: menu appears above taskbar; Settings opens; Rescan and Exit work. Left-click does not open a popup. Check tray menu with no connected devices.
3. Open Personalization > Layout; scroll down, switch alignment, change gaps and offsets and divider; preview remains visible and changes. Try 0 and 4 simulated devices and both themes.
4. With two provider records for a single device, merge using the primary record. Check display/order/palette/notification behavior; disconnect preferred source, check fallback; split and restore ignored records. Verify two identical physical devices are not merged by name.
5. Run existing and Rings6 tests on Windows. Check hardware-specific behavior of G735, Harpe Ace Mini, and GameSir G7 Pro separately.

Static archive/XML checks are not a Windows build or runtime test.


---

# Rings.7 acceptance (not yet run on Windows)
- [ ] Build .NET solution and run LGSTrayUI.Tests, including Rings7Tests.
- [ ] Compile the companion with Windhawk on Windows 11 x64 (record exact OS build).
- [ ] Start with left aligned taskbar; verify Start stays in place and applications move right when the native slot grows.
- [ ] Verify centered mode maintains order and native centering; test 100%, 125%, 150% DPI.
- [ ] Add/remove devices; verify strip width follows end of fade/reflow and no stale reservation when all devices are hidden.
- [ ] Adjust both margins, X/Y, size and gap; compare layout preview vs actual taskbar.
- [ ] Switch Right; exit app; terminate app; disable mod; verify margin restored (lease timeout on app crash).
- [ ] Explorer restart; reconnect app; verify no persistent layout changes.
- [ ] Full-screen apps, autohide, crowded taskbar, incompatible mods, and multi-monitor: verify fail-safe/no overlap.

## Rings 10 right-only layout

Historical entries above describe superseded implementations; the current build supports only a collision-checked right-side overlay. See README.md.
