using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal sealed class DuplicateWriteRefusedException : Exception
{
    public DuplicateWriteRefusedException()
        : base("A duplicate write was refused before it reached the provider.")
    {
    }
}

internal static class TwilioWriteScope
{
    private static readonly AsyncLocal<WriteCounter?> Current = new();

    public static IDisposable Begin()
    {
        Current.Value = new WriteCounter();
        return new Reset();
    }

    public static bool TryConsumeWrite()
    {
        var counter = Current.Value;
        if (counter is null)
        {
            return true;
        }

        if (counter.Count >= 1)
        {
            return false;
        }

        counter.Count++;
        return true;
    }

    private sealed class WriteCounter
    {
        public int Count;
    }

    private sealed class Reset : IDisposable
    {
        public void Dispose() => Current.Value = null;
    }
}
