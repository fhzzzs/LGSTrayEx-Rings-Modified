using System;
using System.Threading;
using System.Threading.Tasks;

namespace LGSTrayUI;

// Power transitions are delivered on the WPF dispatcher. A second sleep
// cancels the settle delay. A later wake waits for any older recovery to leave.
internal sealed class WakeRecovery(Func<CancellationToken, Task> recover, CancellationToken stopping)
{
    private bool suspended, requested;
    private Task? running;
    private CancellationTokenSource? current;

    internal bool Suspend()
    {
        if (suspended || stopping.IsCancellationRequested) return false;
        suspended = true;
        requested = false;
        current?.Cancel();
        return true;
    }

    internal Task ResumeAsync()
    {
        if (stopping.IsCancellationRequested) return Task.CompletedTask;
        if (suspended || running is not { IsCompleted: false }) requested = true;
        suspended = false;
        if (running is { IsCompleted: false }) return running;
        return running = RunAsync();
    }

    private async Task RunAsync()
    {
        while (requested && !stopping.IsCancellationRequested)
        {
            requested = false;
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(stopping);
            current = attempt;
            try { await recover(attempt.Token); }
            catch (OperationCanceledException) when (attempt.IsCancellationRequested) { }
            finally { current = null; }
        }
    }
}
