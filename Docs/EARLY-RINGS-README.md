# Historical notes only; use ../INSTALL.md for current instructions

# LGSTrayEx 3.2.6 – Apple-style taskbar rings (personal customization)

Based on the uploaded LGSTrayEx-taskbar-rings-source archive. Original LICENSE retained. This is source, not a tested Windows binary.

## Build on Windows 11 x64
1. Install the .NET 10 SDK (x64): https://dotnet.microsoft.com/download/dotnet/10.0
2. Extract the entire ZIP. Double-click `BUILD-RINGS.cmd`. It calls the repository's existing `publish.ps1` to publish both the HID backend and GUI, including the self-contained variant.
3. Close the older LGSTray instance (right-click its ring or tray icon > Exit) before starting the new `LGSTray.exe` from `bin\Release\Publish\win-x64\Standalone`. Keep the other published files alongside it. If the backend needs elevation, follow the original LGSTray instructions. Do not delete the original installation until testing is complete.
4. To run added tests: `dotnet test LGSTrayUI.Tests\LGSTrayUI.Tests.csproj -c Release` in PowerShell.

## Changes
- Transparent ring center; existing mouse and headset vector icons, font, normal percentage sizes, and layout retained. Apple-style green #34C759 (20–100), yellow #FFCC00 (10–19), red #FF3B30 (0–9), neutral unknown. 0% has a red dot. No percent is invented when battery data is unavailable.
- WPF animation interpolates the visible arc for 450 ms; offline collapses to a neutral track with `Off`. Online devices with unknown battery show a dash (or a qualitative XInput reading).
- Legacy main and per-device system tray icons disabled. Right-click rings for exit/autostart/rescan. Left-click rings for detail cards.
- Mouse, headset, keyboard, gamepad order, then stable provider key. Multiple recognized devices are displayed. Remembered inventory (names, keys, types only) in `%LOCALAPPDATA%\LGSTray\taskbar-devices.json`; previous percentages are not stored. Original mouse/headset placeholders are replaced when actual devices appear. Clear removed offline devices via the context menu.
- Original Logitech HID and G HUB support retained. BLE Battery Service (0x180F) reads standard percentage where exposed and permission allows; polls paired BLE peripherals. XInput controller detection polls every 2 seconds and exposes only the discrete Empty/Low/Med/Full values reported by the driver, or USB/unknown. A controller that presents as USB may be wireless behind a receiver; that is not proof of charging or remaining power.
- Exact GameSir G7 Pro percentage over its 2.4 GHz receiver is NOT verified or implemented as a vendor-specific HID protocol. No assumption that it exposes standard BLE Battery Service or an exact percentage through XInput. Generic XInput names are used because XInput does not supply model identity. XInput and BLE may show the same controller twice, and slot numbers may change when Windows reassigns a controller. Proprietary non-Logitech receivers and classic-only Bluetooth devices are not automatically supported.
- Same-model Logitech sources are merged when each source supplies one matching name. Different physical units with identical names across providers cannot always be distinguished. On very crowded/short taskbars, the overlay may not fit. Taskbar overlay is a WPF tool window, not a native Windows shell extension.

## Verification status
Added xUnit cases for color boundaries, offline/unknown/zero, category ordering, multiple mice, duplicate-source selection, controller classification and 4-slot taskbar placement. XML and archive checks performed in the source-generation environment. Windows/.NET SDK not available there: build, WPF rendering, WinRT APIs, real device detection and G7 Pro must be validated on Windows. No executable or passed Windows tests are claimed.
