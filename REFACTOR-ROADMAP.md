# Rings architecture migration

Baseline: Rings.9 commit `70bebc5`. This roadmap documents repository changes, not hardware validation.

| Workstream | Status | Acceptance |
|---|---|---|
| Device identity | Unbounded evidence-based grouping plus distinct upstream G HUB routes for ambiguous same-model devices | Unlimited exact-ID/manual sources; unlimited unreported G HUB mode aliases only when family evidence is unambiguous; conflicting serials stay separate |
| Source selection | Phase 1 implemented | Stable usable source, explicit preference among usable fresh sources, fallback on loss or stale source |
| Device state center | Owns grouping, inventory migration, source selection and confirmed snapshot; views consume shared snapshot | Single lifecycle snapshot consumed by renderer, notifications and settings; preserve states across rescan, suspend and mode changes |
| Taskbar placement | Right-only safe overlay; obsolete native provider removed | Never overlap taskbar buttons; hide when trailing gap is insufficient |
| Animation | Generation-safe fade and placement-gated entry implemented; hardware validation pending | Retarget without collision, verify safe trailing space before entry and shrink after exit |
| Notifications | Shared lifecycle, configurable placement/monitor/size/duration/opacity, quiet/full-screen policy and optional full-charge alert | One logical event per device; suppress rescan/startup/wake; compact bounded popups |
| Lifecycle/performance | Dedicated cancellable provider scanner, USB/gamepad event hints, polling fallback, background reads and UI Automation probe, nonblocking shutdown | Nonblocking UI shutdown, owned child cleanup, event-driven polling with cancellation |
| Settings/themes | Versioned flat migration, atomic backups, independent theme fields; legacy files preserved | Versioned migration, theme application never mutates identity or connection preferences |
| Settings UI | Separate theme/device/notification/animation/about partial modules; navigation and controls retained | Existing UI features preserved |
| Updates | Release-only check, SHA-256 verification, side-by-side staging, restart and rollback UI; tagged release workflow prepared; end-to-end validation pending | Versioned release packages, integrity verification, rollback; no replacement of active executables |
| Tests | Ongoing | Automated regressions per stage, Windows CI; actual shell rendering requires real Windows testing |

No arbitrary same-name merge: source count is not evidence of physical identity. Known serial conflicts and ambiguous reporting devices remain separate. Manual links and splits retain user intent. Keep hardware protocol readers and current UI during migration. 

## Release gate

Require passing Windows CI and an interactive Windows 11 check of right-side placement, DPI, crowded taskbar, ring animation, reconnect/rescan, exit latency and update/rollback. Settings, inventory and themes retain their existing transaction boundaries.

## Rings 11.0 — first nine workstreams

| Item | Implementation | Remaining verification |
|---|---|---|
| 1 | Persistent logical GUID registry and preference migration; source/serial evidence conflict checks | Hardware without persistent identity (notably XInput slots) cannot be identified with certainty; manual association supported |
| 2 | Normalized observation with connection, charge, quality, per-provider TTL; expired reading not shown as live | Verify real polling intervals |
| 3 | Typed connection and charging state; legacy display labels normalized at input | Real reconnect/charging transitions |
| 4 | GIP and XInput independently scheduled, no global exclusion; exact evidence/manual links only | Mixed controllers and physical identity validation |
| 5 | Application-owned scheduler, separate provider intervals, cancellation, bounded backoff; existing Logitech hosted push providers retained | Native calls may only honor cancellation after returning |
| 6 | Shell WinEvent hooks, display-change invalidation, adaptive geometry polling | Explorer restart and DPI behavior |
| 7 | Preference fingerprints separate device regroup, provider and layout changes from appearance | Verify live settings responsiveness |
| 8 | Application-owned scanner, deduplicated resume, cancellation and bounded asynchronous shutdown | Measure real exit time |
| 9 | Notification event center, logical-ID deduplication and connection/low-alert priority | Verify actual hardware alerts |

Source phases are committed without moving the main branch until all nine are staged. The final branch update triggers Windows tests and a single portable build. No native left-layout code is reintroduced. Items 10–16 are not part of this change.

### Rings 11.0.1 controller compatibility

One live Xbox 360 Controller for Windows WGI record with valid battery data and one live USB XInput slot can be grouped provisionally. Keep the WGI logical ID and battery, retain both sources, and do not permanently assign the XInput slot to that device. Inactive history hints only attach offline placeholders. Multiple live candidates, separate-device choices and other models are not guessed. This is a reversible compatibility heuristic rather than hardware identity proof. Real multi-controller validation remains necessary.

### Rings 11.0.2 portable packaging

Root launcher plus private app/ folder; no shipped PDBs. Keep WPF and HID libraries together, route startup/update/rollback through the outer entry, and validate the published launcher from a different working directory in Windows CI. Existing user JSON paths are unchanged. Older flat-only updaters require manual upgrade once.

## Rings 12.0 — remaining workstreams
- 10: Shared compositor frame clock for custom ring movement, fading, color, level and charging animation; inactive participants unregister. Offline grace and demo timers remain low-frequency state timers.
- 11: Coalesced serialized background atomic writes for ring settings, themes and inventory; bounded final flush.
- 12: Generic validated field view models and two-way bindings for shared controls; complex device/theme pages still retain programmatic view code.
- 13: Managed updates stage side-by-side, preserve local TOML overrides and prior main TOML, supervise startup with a readiness marker, attempt previous-version rollback on failure, and offer explicit guarded cleanup. This requires the new launcher on the target package and update-aware current version. Real release-to-release and failure tests remain required.
- 14: Local diagnostics page reports source selection, device grouping, provider latency/errors and current frame participants.
- 15: Retire old model-only ReadAll aggregation, unused freshness helper and obsolete notification DI registration. Legacy upstream notification implementation/tests remain until separately migrated.
- 16: Add deterministic source churn, 50-source grouping, rapid disconnect/reconnect, simulated 24-hour wake, writer coalescing and managed update cleanup tests. These are simulated tests; actual 24-hour running, hardware and shell interaction are not proven by CI.

The final build retains the root launcher plus app directory. No native left placement code.


## Rings 13–15 — consolidated hardening (15.0.0)
Source-level work builds on Rings 12 and the 12 compilation repair. Existing behavior and conservative identity rules are retained.
| Item | Implementation / evidence | Remaining validation |
|---|---|---|
| 17 | Evidence-based identity grouping and provisional GIP/XInput link; invalid readings excluded from best-source priority | Real GameSir and simultaneous identical controllers |
| 18 | Persistent logical identity across provider changes; mode-change regression | USB/BLE/2.4 GHz switching where hardware exposes stable evidence |
| 19 | Existing deduplicated resume and suppression, coalesced gamepad event rescans | S3/S0 resume on hardware |
| 20 | TaskbarCreated invalidation and generation-safe shell geometry | Restart Explorer on Windows 11 |
| 21 | Existing 2.5 s grouped connection debounce; churn regression | Real short disconnects |
| 22 | Per-provider exceptions isolated; GIP global enumeration failure no longer appears as successful empty scan | Native APIs that ignore cancellation |
| 23 | Bounded adaptive polling and failure backoff, tests | Reconnect latency |
| 24 | Exact, band, stale and unknown preserved; invalid GIP capacity no longer clamped to 100; idle/full distinguished | Device-specific capacity reporting |
| 25 | Bounded per-ring text cache; cleanup of watchers and animation timer; memory diagnostics | 168-hour physical soak, memory trend |
| 26 | Icon handle disposal, GDI/USER/handle diagnostics | Long running GDI/USER trend |
| 27 | No visual invalidation for timestamp-only changes; avoid forced snapshot updates | Profiling |
| 28 | Frozen shared glyphs and single-entry text/DPI/color cache | Rendering profiling |
| 29 | Adaptive idle polling and timer cadence | Hardware reconnect timing |
| 30 | Existing background startup scans; measure host-ready startup; avoid redundant snapshot update | Cold-start benchmarks |
| 31 | Bounded log trim, mutex timeout, one-segment size rotation when line limit enabled | Large log and crash recovery |
| 32 | Owned HID process tree cleanup; existing parent monitor | Shutdown/resume with actual HID worker |
| 33 | DPI-aware text cache, finite placement guards and DPI geometry regression | Live 100–200% DPI |
| 34 | Monitor bounds and negative-coordinate tests; primary taskbar overlay remains single-instance | Mixed DPI, primary monitor changes |
| 35 | Detect Windows system theme changes without writing preferences | Live dark/light switch |
| 36 | Fixed category order, persisted drag order inside each category | Interactive drag and restart |
| 37 | Existing generation-safe gray/fade animation; shared frame clock and fewer redundant renders | Live offline/reconnect animation |
| 38 | Existing charging animation and charge-state display; full/invalid GIP reading correction | Real charging/full state |
| 39 | Semantic backup preservation, invalid inventory recovery and malformed preference sanitization | Corrupted settings regression in CI |
| 40 | Keep one root launcher and private app/ runtime/docs, no PDBs; version defaults unified | Portable smoke test in Windows CI |

No new native placement or guessed cross-protocol serial mapping. Hardware without common identity evidence cannot be automatically identified with certainty; manual links/splits remain available. The simulated tests do not certify a 168-hour soak or resource target. Publish only after Windows tests and real shell/hardware verification.
