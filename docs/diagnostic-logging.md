# Diagnostic logging (Battery Monitor 1.0.5)

The UI and HID worker retain the shared diagnostic.log path, logging switches,
and existing Log/Verbose/LogWarning/LogError call style. Debug/verbose,
info, warning and error records include category, caller and process ID.

- Each process owns one asynchronous writer. The producer does not wait for
  ordinary disk writes; an idle writer has no periodic polling timer.
- The queue is bounded to 2,048 entries and roughly two million retained
  characters, with a reserve for warning/error messages. The writer also holds
  at most one 128-entry batch. Normal bursts are batched for 35 ms.
- A named cross-process mutex protects each synchronous batch, trimming and
  rotation. The file stream is reused within a batch and closed before the
  mutex is released, so the other process can safely rotate the shared file.
- Disabled interpolated calls do not evaluate their interpolation expressions.
  Already-built strings still cost their caller to construct.
- Consecutive identical non-protocol debug/info records within a batch are
  summarized with a repetition count and last timestamp. Protocol, warning,
  and error records are not deduplicated.
- If the ordinary queue is full, low-priority records can be dropped and counted;
  a successful later batch writes the outstanding loss count. Warnings/errors
  that cannot enter the queue or reach the shared file use a separate
  diagnostic.log.emergency-PID file. This rare fallback may block the caller.
  If even that file cannot be written, an error is reported and counted.
- The main file rotates around 4 MiB, retains at most three numbered archives
  and removes archives older than seven days on startup/active daily cleanup.
  Single exceptionally large log entries are not truncated. The legacy MaxLines
  setting continues to control periodic active-file trimming; zero means no
  line-count trimming, not unlimited disk usage.
- Normal process exit drains with a 1.5-second deadline. Forced termination,
  power loss or persistent disk failure cannot guarantee final persistence.
- The existing diagnostics report exposes pending entries, batches, accepted
  records written, coalesced entries, dropped records, emergency writes,
  unwritten critical records and maintenance failures.

Regression tests exercise disabled logging allocations and argument evaluation,
batching and repetition, rotation and retention, the legacy line limit,
two writers sharing the same file, saturation and failure. Windows CI executes
these tests before packaging.
