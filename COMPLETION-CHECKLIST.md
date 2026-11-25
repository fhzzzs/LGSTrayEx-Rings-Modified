# 2.0.0 completion checklist — development only

Version and packaging unchanged. Baseline main is b2ef07c. Development
commits are skipped by CI until the final integration is requested. Nothing
below claims a Windows build, attached hardware validation, or a soak test.
The existing main 2.0.0 CI success is for the baseline only.

| Original item | Work in this checkpoint | Still required |
|---|---|---|
| 1 | Managed hotplug snapshots, exact container/slot source identity | Common cross-transport inventory and device-change lifecycle |
| 3 | New explicit allowlisted ASUS and ASTRO status protocols | Safe discovery of additional protocol families |
| 5 | Receiver-scoped fallback identifiers, preserve serial evidence | Real USB/BLE/2.4G mode-switch validation |
| 6 | Scope fallback names by container and slot, lock receiver snapshots | Same-model physical tests, firmware ID conflicts |
| 7 | Explicit absent offline-history cleanup, ambiguous XInput history guard | Long-term source retention policy |
| 11 | Validate register replies against exact device slot; robust feature enumeration | Native 1.0-only initialization and battery status/mileage reading |
| 13 | Centurion invalid SOC rejection and fresh-read heartbeat | Additional report families and model validation |
| 14 | Better HID lifecycle and unique receiver source IDs | More office-device physical testing |
| 15 | ASTRO A50 Gen 5 exact VID/PID and usage, bounded read, echo validation | Other ASTRO families and base-station/standby validation |
| 17 | Existing direct and OMNI support retained | More mouse report layouts |
| 18 | Direct Azoth, Falchion, Claymore II, Scope II 96 profiles | Hardware tests and remaining variants |
| 19 | Delta II / Pelta direct battery+charge status | Cetra multi-component, other earphones and standby validation |
| 20 | Existing SPEEDNOVA path retained | Reliable shared receiver identification and physical tests |
| 21 | Existing TUF support retained | ProArt / more TUF models |
| 22 | Clear ambiguous live-slot history | GameSir native reports and physical tests |
| 23 | Existing WGI/BLE support retained | Sony native reports and physical tests |
| 24 | Existing WGI/BLE support retained | Nintendo native reports and physical tests |
| 25 | Existing WGI/BLE support retained | 8BitDo native reports and physical tests |
| 27 | Same-container access remains bounded; no coexistence claims | G HUB / Armoury Crate simultaneous hardware tests |
| 30 | Increasing retry delay for failing HID++ polls | Device-specific standby and wake behavior |
| 31 | UIA failure backoff, less frequent capture | Explorer/DPI/taskbar stress measurements |
| 32 | Async managed hotplug channel, remove blocking async-void thread | End-to-end thread measurement |
| 33 | Previous allocation work retained; reduce idle state timer frequency | Allocation/heap profiling |
| 34 | Native queue lifetime and invalid handle guard | Handle/GDI/USER soak measurements |
| 35 | Previous startup work retained | Startup profiling |
| 39 | Independent bounded local display catalog import | Trusted protocol database updating without executable change |
| 40 | Existing 168-hour sampler retained | Actual 168 hours on representative systems |

Local display catalog format:
```json
{"schema":1,"models":[{"name":"Example Mouse","type":"mouse"}]}
```
Types: mouse, keyboard, headset, gamepad. Names match exactly (case insensitive).
No firmware commands, report offsets, executable scripts, or serial mappings are
imported. No network request is made by the import. Hardware descriptors and
standard BLE service still govern actual battery availability.
