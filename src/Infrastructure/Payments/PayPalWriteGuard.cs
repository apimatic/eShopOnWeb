using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal static class PayPalWriteGuard
{
    private static readonly AsyncLocal<WriteScope?> Current = new();

    public static IDisposable Begin()
    {
        var scope = new WriteScope();
        Current.Value = scope;
        return scope;
    }

    public static bool TryMarkSent()
    {
        var scope = Current.Value;
        if (scope is null)
        {
            return true;
        }

        if (scope.Sent)
        {
            return false;
        }

        scope.Sent = true;
        return true;
    }

    private sealed class WriteScope : IDisposable
    {
        public bool Sent { get; set; }

        public void Dispose()
        {
            if (Current.Value == this)
            {
                Current.Value = null;
            }
        }
    }
}

internal sealed class PayPalDuplicateSendException : Exception
{
    public PayPalDuplicateSendException()
        : base("A duplicate PayPal write was blocked after the original request may already have reached the processor.")
    {
    }
}

internal static class PayPalLastStatus
{
    public static readonly AsyncLocal<int?> Code = new();
}
