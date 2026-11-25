# Battery Monitor 2.0 — compatibility and validation

This is a protocol coverage catalog, not a certification of every model.
Read-only battery/status requests only; no changes to DPI, RGB, key bindings,
pairing, firmware, drivers or Armoury Crate settings.

## Validation tiers

- **User hardware confirmed (1.0.3):** ROG Harpe Ace Mini over OMNI; Xbox Wireless
  Controller BLE+WGI+XInput; Logitech PRO X SUPERLIGHT 2, PRO X 2 LIGHTSPEED,
  and G735 (see user diagnostics). The expanded 2.0 code must be retested.
- **Protocol implemented, physical validation pending:** known ASUS direct
  mice, OMNI mice/keyboards, confirmed Harpe II SPEEDNOVA model strings,
  Strix Go 2.4 feature reports; supported Logitech HID++ 2.0 battery features;
  existing Centurion and G HUB paths; ASTRO A50 Gen 5 base station; direct ASUS Azoth/Falchion/Claymore II/Scope II 96 keyboards and Delta II/Pelta headsets; standard BLE Battery Service and
  Windows Gaming Input on compatible controllers.
- **Discovery only or pending:** any unknown VID/PID/firmware or shared receiver
  with no reliable model identity; older HID++ 1.0-only devices not handled by
  the inherited transport; other ASTRO-specific wireless protocols; ROG Cetra,
  unsupported direct keyboards/receivers and unverified shared
  SPEEDNOVA variants; model-specific Sony, Nintendo, 8BitDo and GameSir
  proprietary battery reports when standard Windows reports are unavailable.

The authoritative ASUS transport allowlist lives in
LGSTrayUI/Services/PeripheralCatalog.cs. Multiple USB IDs for the same model
are supported without assuming every identical-model unit is the same
physical unit. OMNI pairing identifies the mouse and keyboard separately;
the mouse and keyboard on a receiver are not merged. Old Harpe Mini keys
are preserved. Voltage-derived/stepped ASUS readings are labeled approximate.
A known HID device that cannot supply a valid battery report never becomes
a fabricated zero percent. No blind writes to unknown HID devices.

Logitech HID++ 2.0 uses the advertised feature table; direct percentage
features precede voltage/ADC estimation. Failed reads can rotate to another
advertised feature. G HUB remains available as an alternate path. Centurion now rejects invalid SOC >100. HID++ 1.0 register replies are matched by slot; full legacy device initialization and additional ASTRO protocols still need implementation. A50 Gen 5's 046D:0B1C protocol is implemented but not tested on this fork's hardware.

Controllers use known VID/PID naming plus Windows raw battery reports or BLE
where available, and XInput bands as fallback. The PID is not a serial number.
Do not merge conflicting physical IDs; model-based groups are reversible.
Simultaneously present GameSir units prevent inferred history grouping.
Source disagreements are reported rather than averaged.

## Diagnostic and performance workflow

Open Settings > Diagnostics. Scan hardware compatibility to enumerate
supported-brand HID collections without opening them or writing commands;
then export the complete TXT. The hardware inventory omits paths, serials,
and addresses, but the full diagnostic contains local logical source IDs:
review before sharing. Compatibility catalog lists implemented ASUS profiles.
Battery changes remain in memory only, at most 256 records per session. Explicit offline-history cleanup retains personalization. A bounded display-only JSON catalog can be imported in Diagnostics independently of the executable. It does not authorize new HID commands or device identity merges.
Stable ASUS/BLE reads may slow polling; reconnect events and manual rescan
remain available. A blocked native provider does not start overlapping
requests, and other providers can keep running.

Run MEASURE-BATTERY-MONITOR.ps1 on a real Windows system while Battery Monitor
is running to capture both UI and HID worker resources at intervals. The
default 168-hour run is optional and can be stopped with Ctrl+C. CSV is
local only. Record results with physical device modes, reboot, sleep/resume,
G HUB/Armoury Crate coexistence, plug/unplug, and taskbar DPI changes.
The script is instrumentation, not evidence that a 168-hour test has passed.
Automated CI tests and a package smoke test do not certify real devices.

## Protocol references and upstream credits

Original project: https://github.com/andyvorld/LGSTrayBattery
Community fork: https://github.com/strain08/LGSTrayEx
Modified by: https://github.com/fhzzzs
GPL-3.0. Original notices retained; this fork is not endorsed by upstream.

Protocol facts (IDs, report IDs, command echoes, offsets and voltage steps):
- HaloBattery, HeyOkay and contributors:
  https://github.com/HeyOkay/HaloBattery/blob/main/docs/protocols.md
  https://github.com/HeyOkay/HaloBattery/blob/main/providers/asus.py
- G-Helper, seerge and contributors:
  https://github.com/seerge/g-helper/discussions/4321
  https://github.com/seerge/g-helper/discussions/5710
- Solaar, pwr-Solaar contributors:
  https://github.com/pwr-Solaar/Solaar/blob/master/docs/features.md
- Sony/Nintendo/8BitDo USB identity references:
  https://github.com/nondebug/dualsense
  https://github.com/fabiscafe/game-devices-udev
  https://github.com/libsdl-org/SDL/blob/main/test/testevdev.c

The C# adaptations implement the documented protocol facts in the existing
Battery Monitor architecture. No external vendor app or executable is bundled.

## Additional protocol references (completion branch)
- https://github.com/Sapd/HeadsetControl/blob/master/lib/devices/logitech_astro_a50.hpp — A50 Gen 5
- https://github.com/HeyOkay/HaloBattery/blob/main/providers/astro.py — A50 base-station protocol facts
- https://github.com/seerge/g-helper/tree/main/app/Peripherals/Keyboard/Models — direct keyboard IDs and battery offsets
- https://github.com/seerge/g-helper/tree/main/app/Peripherals/Headset/Models — Delta II/Pelta
