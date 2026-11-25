# Battery Monitor 1.0.5 — compatibility and validation

This is a protocol coverage catalog, not a certification of every model.
Read-only battery/status requests only; no changes to DPI, RGB, key bindings,
pairing, firmware, drivers or Armoury Crate settings.

## Validation tiers

- **User hardware confirmed (1.0.3):** ROG Harpe Ace Mini over OMNI; Xbox Wireless
  Controller BLE+WGI+XInput; Logitech PRO X SUPERLIGHT 2, PRO X 2 LIGHTSPEED,
  and G735 (see user diagnostics). The expanded 1.0.5 code must be retested.
- **Protocol implemented, physical validation pending:** known ASUS direct
  mice, OMNI mice/keyboards, confirmed Harpe II SPEEDNOVA model strings,
  Strix Go 2.4 feature reports; supported Logitech HID++ 2.0 battery features;
  existing Centurion and G HUB paths; ASTRO A50 Gen 5 base station; direct ASUS Azoth/Falchion/Claymore II/Scope II 96 keyboards and Delta II/Pelta headsets; standard BLE Battery Service and
  Windows Gaming Input on compatible controllers.
- **Discovery only or pending:** any unknown VID/PID/firmware or shared receiver
  with no reliable model identity; older HID++ 1.0-only devices not handled by
  the inherited transport; other ASTRO-specific wireless protocols; unsupported direct keyboards/receivers and unverified shared
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

## Second 1.0.5 completion checkpoint — unbuilt

- Legacy Logitech HID++ 1.0: for a recognized online receiver announcement with mouse/keyboard type and WPID, a valid 0x0D register read permits a scoped fallback device to initialize. Capacity is accepted only for documented discharging/full states. The 0x07 coarse status register and older receiver metadata discovery still need implementation; this is not complete HID++ 1.0 coverage.
- Passive Sony DualSense/Edge/DualShock 4, Nintendo Switch Pro/Joy-Con, and selected 8BitDo D-input full reports: no feature-report trigger, USB handshake, output report, or mode change is sent. Bluetooth basic mode may have no battery reading. Sony Bluetooth CRC is validated; Sony/Nintendo values are estimates; 8BitDo enhanced reports carry percent and charging. Windows zero-padded reports are not interpreted as zero percent. The Switch 2-specific controllers and proprietary GameSir/8BitDo variants are not established by these adapters.
- WGI 1000/1000 mWh without a charge rate is treated as an ambiguous placeholder, not proof of 100%. Existing GameSir G7 Pro WGI support remains.
- Direct ASUS MD200, ProArt MD301, Gladius II Wireless and Strix Carry have explicit interface/offset profiles. Cetra SpeedNova reports both earbuds and optional case in diagnostics; the aggregate is the lower earbud, not the case.
- A confirmed older source can be removed from inventory after 30 days only when another currently live source has the same exact physical evidence, and the old source is absent and has no explicit customization. Ambiguous and manually linked records remain.
- Separate ASUS, ASTRO and passive controller provider toggles assist coexistence testing. Raw controller hotplug events request a coalesced scan.
- The independent display catalog can be fetched explicitly from the project's fixed HTTPS URL after the file reaches main, with a size limit, schema validation and confirmation before install. It is data-only and cannot add protocol commands or merge physical identities. Independent protocol updates are still pending.

Protocol facts consulted (implementations not hardware certified in this fork):
- https://github.com/torvalds/linux/blob/master/drivers/hid/hid-playstation.c
- https://github.com/torvalds/linux/blob/master/drivers/hid/hid-nintendo.c
- https://github.com/torvalds/linux/blob/master/drivers/hid/hid-logitech-hidpp.c
- https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp10.py
- https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/receiver.py
- https://github.com/HeyOkay/HaloBattery/blob/main/providers/eightbitdo.py
- https://github.com/HeyOkay/HaloBattery/blob/main/providers/playstation.py
- https://github.com/HeyOkay/HaloBattery/blob/main/providers/nintendo.py
- https://github.com/HeyOkay/HaloBattery/blob/main/providers/wgi.py

## Third 1.0.5 checkpoint: GameSir and resource lifecycle (unbuilt)

- G7 Pro known edition IDs, including Wuchang 3537:10A9 and ZZZ 105E, use WGI; the shared 1004 ID is not assumed to be G7. The 10A9 high-rate mode is not treated as a private configuration channel. XInput is a band fallback; unique inferred links are reversible, with G7 history restricted per PID.
- Cyclone 2 (100B/102A/101D/1053) vendor page FFF0: passive live 0x12 input battery field, explicit opt-in documented status query only when the descriptor confirms a 64-byte output report. Never send this command to G7 Pro. Invalid levels, charge states and all-zero idle receiver frames are rejected. Exact device validation remains required.
- Sleep: coalesced suspend notifications cancel UI-side provider work, stop placement/state timers; resume invalidates pre-sleep values and staggers reads. Existing HID supervisor resume paths retained. Native blocked calls are not overlapped.
- UIA: cache-only element properties, deduplicated bounds, stale generation rejection and capture metrics. No unsafe placement reuse across Explorer/layout changes.
- HID reader loops have explicit thread completion; native handle closure is deferred until actual loop completion and disposal. Partial receiver handles closed. Efficiency-mode process wrapper and restarted process wrapper disposed. No forced GC or working-set trimming.
- Timestamp-only UI refresh avoids reflow, while tooltip freshness is kept; fixed ring geometry and brushes cached. Startup throttling deferred until host ready, hidden power HWND created without show/hide, provider scans staggered. GC/heap/pool/UIA/phase diagnostics added. The performance sampler's -AnalyzeCsv option summarizes per-process resource deltas after warmup. These are instrumentation and source changes, not measured improvements.

GameSir references: https://github.com/broroeror/gamesir-linux-tools/blob/main/vendors/gamesir/models/g7pro/protocol.py ; https://github.com/nmkha16/battery-deck/blob/main/src/providers/gamesir.cpp ; https://github.com/HeyOkay/HaloBattery/blob/main/providers/wgi.py . Original project credits retained.

## 1.0.5 runtime feedback correction

The previous optional Cyclone 2 active status query is withdrawn after a user reported UI disappearance and flicker. Its legacy saved setting is ignored. A separate opt-in passive-only Cyclone 2 provider does not send any GameSir output report, and is independent of the G7 Pro WGI path. Passive reading still requires physical validation. GIP now attempts the older Gamepad API as well as Raw for known controllers and logs both readings when they change; synthetic 1000/1000 mWh without a charge rate remains ambiguous, not a confirmed full battery. Connection-only rings use ON with a dashed outline, stale values use N/A, offline stays Off. Device cards refresh their existing controls and source status labels; structure changes defer while editing. Full-row toggle headers replace arrow-only advanced and device-source expanders. Shell UIA refresh starts earlier, retains only still-valid snapshots, and retries a superseded capture promptly. Flicker and G7 battery availability must be retested on the user's system. The provided diagnostic log is an older Rings 9 session, not evidence of the current failure.
