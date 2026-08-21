using System;
using System.Net.Http;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal static class MaxioWriteGuard
{
    private static readonly AsyncLocal<WriteScope?> CurrentScope = new();

    public static IDisposable Begin()
    {
        if (CurrentScope.Value is not null)
        {
            throw new InvalidOperationException("A Maxio write scope is already active.");
        }

        CurrentScope.Value = new WriteScope();
        return CurrentScope.Value;
    }

    public static void OnSending(HttpMethod method)
    {
        var scope = CurrentScope.Value;
        if (scope is null || method != HttpMethod.Post)
        {
            return;
        }

        if (Interlocked.Increment(ref scope.SendCount) > 1)
        {
            throw new MaxioWriteReplayBlockedException();
        }
    }

    private sealed class WriteScope : IDisposable
    {
        public int SendCount;

        public void Dispose()
        {
            CurrentScope.Value = null;
        }
    }
}

internal sealed class MaxioWriteReplayBlockedException : Exception
{
    public MaxioWriteReplayBlockedException()
        : base("An automatic replay of a Maxio write was blocked.")
    {
    }
}
