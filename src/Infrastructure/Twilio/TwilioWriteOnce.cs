using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal static class TwilioWriteOnce
{
    private static readonly AsyncLocal<Counter?> Current = new();

    public static IDisposable Begin()
    {
        Current.Value = new Counter();
        return new Reset();
    }

    public static bool TryAcquirePost()
    {
        var counter = Current.Value;
        if (counter is null)
        {
            return true;
        }

        return Interlocked.Increment(ref counter.Count) == 1;
    }

    private sealed class Counter
    {
        public int Count;
    }

    private sealed class Reset : IDisposable
    {
        public void Dispose() => Current.Value = null;
    }
}
