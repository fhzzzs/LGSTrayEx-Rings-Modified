using LGSTrayUI;

namespace LGSTrayUI.Tests;

public class HidDiscoveryTests
{
    [Fact]
    public async Task AllProvidersWaitForAnActiveDiscoveryCall()
    {
        using var enumerating = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var waiting = new CountdownEvent(4);
        using var otherCallEntered = new ManualResetEventSlim();
        int initializations = 0;
        int completed = 0;
        var timeout = TimeSpan.FromSeconds(10);

        void RecordOtherCall()
        {
            Interlocked.Increment(ref completed);
            otherCallEntered.Set();
        }

        var hid = new HidDiscovery(
            () => { Interlocked.Increment(ref initializations); return 0; },
            (vendor, _) =>
            {
                if (vendor == 1)
                {
                    enumerating.Set();
                    if (!release.Wait(timeout)) throw new TimeoutException();
                }
                else RecordOtherCall();
                return 10;
            },
            _ => RecordOtherCall(),
            _ => { RecordOtherCall(); return 20; },
            _ => RecordOtherCall());

        static Task OnWorker(Action call) => Task.Factory.StartNew(
            call, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        var first = OnWorker(() => hid.Enumerate(1, 0));
        var others = new List<Task>();
        try
        {
            Assert.True(enumerating.Wait(timeout));
            Action[] operations = [
                () => hid.Enumerate(2, 0), () => hid.OpenPath("test-device"),
                () => hid.FreeEnumeration(10), () => hid.Close(20)
            ];
            foreach (var operation in operations)
                others.Add(OnWorker(() => { waiting.Signal(); operation(); }));

            Assert.True(waiting.Wait(timeout));
            Assert.False(otherCallEntered.Wait(TimeSpan.FromMilliseconds(200)));
        }
        finally
        {
            release.Set();
            await Task.WhenAll(others.Append(first)).WaitAsync(timeout);
        }

        Assert.Equal(1, initializations);
        Assert.Equal(4, completed);
    }

    [Fact]
    public void InitializationFailureLeavesTheNextProviderAbleToRetry()
    {
        int attempts = 0;
        int opens = 0;
        var hid = new HidDiscovery(
            () => ++attempts == 1 ? -1 : 0,
            (vendor, product) => vendor + product,
            _ => { },
            _ => { opens++; return 20; },
            _ => { });

        Assert.Equal((nint)0, hid.OpenPath("test-device"));
        Assert.Equal(0, opens);
        Assert.Equal((nint)7, hid.Enumerate(3, 4));
        Assert.Equal((nint)20, hid.OpenPath("test-device"));
        Assert.Equal(1, opens);
        Assert.Equal(2, attempts);
    }
}
