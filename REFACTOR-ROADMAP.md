# Rings architecture migration

Baseline: Rings.9 commit `70bebc5`. This roadmap documents repository changes, not hardware validation.

| Workstream | Status | Acceptance |
|---|---|---|
| Device identity | Phase 1 implemented | Unlimited exact-ID/manual sources; unlimited unreported G HUB mode aliases only when family evidence is unambiguous; conflicting serials stay separate |
| Source selection | Phase 1 implemented | Stable usable source, explicit preference among usable fresh sources, fallback on loss or stale source |
| Device state center | Pending | Single lifecycle snapshot consumed by renderer, notifications and settings; preserve states across rescan, suspend and mode changes |
| Native taskbar | Pending hardware validation | Actual native slot verified on Win11 multi-monitor; no fallback presented as native |
| Animation | Pending | Retarget without collision, reserve native space before entry and release after exit |
| Notifications | Partial (Rings.9) | One logical event per device; suppress rescan/startup/wake; compact bounded popups |
| Lifecycle/performance | Partial (Rings.9) | Nonblocking UI shutdown, owned child cleanup, event-driven polling with cancellation |
| Settings/themes | Pending | Versioned migration, theme application never mutates identity or connection preferences |
| Updates | Pending | Versioned release packages, integrity verification, rollback; no replacement of active executables |
| Tests | Ongoing | Automated regressions per stage, Windows CI; actual shell rendering requires real Windows testing |

No arbitrary same-name merge: source count is not evidence of physical identity. Known serial conflicts and ambiguous reporting devices remain separate. Manual links and splits retain user intent. Keep hardware protocol readers and current UI during migration. 
