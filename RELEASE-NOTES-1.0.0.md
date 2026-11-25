# Battery Monitor — v1.0.0

First public release, based on the Rings 15 development milestone.

## Highlights
- Windows 11 x64 portable battery rings for mice, headsets, keyboards and controllers.
- Transparent taskbar overlay, configurable battery colors, charging indicators and offline animations.
- Device discovery, persistent identity, source fallback, manual merge/split and category ordering.
- Adaptive polling, Explorer recovery, DPI-aware placement, diagnostics and configuration recovery.
- One outer LGSTray.exe; bundled runtime, worker and documentation in app/.

## Installation
Download LGSTrayEx-Rings-Portable-x64.zip, extract the entire ZIP and run the outer LGSTray.exe. Keep app/ next to it. Exit older versions before launching. Rings 15 development users must upgrade manually once because the public version numbering has restarted at 1.0.0. Existing user settings are retained.

## Known limits
Windows CI verifies compilation, regression tests and the portable launcher. Actual GameSir/controller identity, USB/Bluetooth/2.4 GHz transitions, mixed-DPI displays, Explorer recovery and long-running resource usage still need hardware validation. The overlay targets the primary taskbar and hides when no safe space is available.

Unofficial modification by fhzzzs. Based on LGSTrayEx (strain08) and LGSTrayBattery (andyvorld). GPL-3.0; original attribution retained.
