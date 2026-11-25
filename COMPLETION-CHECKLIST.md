# 2.0.0 completion checklist — development only

Version and packaging unchanged. Baseline main is b2ef07c. Development
commits are skipped by CI until the final integration is requested. Nothing
below claims a Windows build, attached hardware validation, or a soak test.
The existing main 2.0.0 CI success is for the baseline only.

| Original item | Work in this checkpoint | Still required |
|---|---|---|
| 1 | Managed hotplug snapshots; raw WGI events and provider controls | Common cross-transport inventory and device-change lifecycle |
| 3 | Added allowlisted native report decoders and legacy register fallback | Additional known protocols and safe detection; no blind writes |
| 5 | Scoped identities and reversible passive HID/WGI matching | Real USB/BLE/2.4G mode switching and serial evidence across transports |
| 6 | Container/slot identity; disable inferred controller pairing when multiple live units observed | Same-model real hardware tests and conflicting firmware identifiers |
| 7 | Explicit cleanup plus 30-day exact-evidence absent-source retention | Verify retention/migration and repeated reconnect behavior on actual hardware |
| 11 | Exact-slot 0x0D read and online-announcement fallback initialization | 0x07 band IPC, legacy pairing names, charging behavior and hardware validation |
| 13 | Invalid SOC rejection and freshness heartbeat | Other Centurion report families and physical validation |
| 14 | Scoped identities and older register fallback; independent display names | Actual office-device coverage and tests |
| 15 | A50 Gen 5 exact VID/PID and usage adapter retained | Other ASTRO protocols, standby and hardware tests |
| 17 | Added Gladius II Wireless and Strix Carry specific interfaces/offsets | Remaining mouse layouts and hardware verification |
| 18 | Direct Azoth/Falchion/Claymore II/Scope II 96 retained | More keyboard variants and hardware tests |
| 19 | Delta II/Pelta; Cetra SpeedNova L/R/case parsing and aggregate | Other models and real component/standby validation |
| 20 | Existing guarded SPEEDNOVA implementation retained | Shared receiver identity and real hardware tests |
| 21 | MD200 and ProArt MD301 plus existing TUF profiles | Additional TUF/ProArt models and physical tests |
| 22 | G7 Pro edition naming and WGI fallback; Cyclone 2 native frame parser, opt-in bounded query; per-PID history | Actual 10A9 on/off/dock/charge reports; validate Cyclone 2; other GameSir private modes and models |
| 23 | Passive Sony DS4/DualSense/Edge with CRC and approximate levels | Basic Bluetooth reports contain no battery; hardware tests |
| 24 | Passive Switch Pro/Joy-Con full input battery bands | Basic mode, Switch 2 protocols and hardware tests |
| 25 | Passive enhanced input reports for SF30/SN30 Pro, Pro 2/3 | Basic mode and Ultimate/new proprietary report families; hardware tests |
| 27 | Separate ASUS/ASTRO/controller-HID controls; no controller mode writes | Actual coexistence tests with G HUB, Armoury Crate, Steam and games |
| 30 | Suspend cancels provider work; stop UI timers; invalidate old level and stagger resume; stable poll backoff | Physical sleep/wake, low-power receiver states, S0/S3 and power use measurement |
| 31 | UIA cache-only properties, deduplication, stale rejection, capture metrics | Explorer/DPI/fullscreen/taskbar stress and capture latency measurement |
| 32 | Owned reader threads; actual completion; no blocking async continuation on pool; batched controller rescans | Actual UI and HID worker thread counts, reader error/replug tests |
| 33 | Timestamp-only no reflow, cached ring geometry/brushes, offline retention allocation fix; GC diagnostics | Allocation/heap profiling and before/after working/private memory measurement |
| 34 | Close partial receivers; defer native close until reader completion; dispose process wrappers; per-PID CSV analysis | Handle/GDI/USER trend testing on reconnect, sleep, UI and long run |
| 35 | Startup phases, stagger scan, defer efficiency throttling, create power HWND without show/hide | Cold/warm Windows startup benchmark and readiness/rollback smoke test |
| 39 | Explicit local import and fixed HTTPS online display-only catalog with size/schema checks | Online endpoint available after integration; independently trusted protocol updates |
| 40 | 168-hour sampler retained; not run | Actual 168-hour run on representative systems |

Local display catalog format:
```json
{"schema":1,"models":[{"name":"Example Mouse","type":"mouse"}]}
```
Types: mouse, keyboard, headset, gamepad. Names match exactly (case insensitive).
No firmware commands, report offsets, executable scripts, or serial mappings are
imported. No network request is made by the import. Hardware descriptors and
standard BLE service still govern actual battery availability.

Second checkpoint: additional C# parser and safety regression tests are authored but have not been compiled or executed. No new package, release, or version. Original 27 items remain open until their remaining acceptance criteria are met.

Third checkpoint prioritizes 22 and 30–35. Source changes and tests authored, no Windows build or hardware measurements. These seven remain open for their explicit validation gates; no version bump or release.
