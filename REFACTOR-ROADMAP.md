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
