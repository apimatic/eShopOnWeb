using System;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.Messaging;

internal sealed class TwilioWriteOnceScope : IDisposable
{
    private static readonly AsyncLocal<TwilioWriteOnceScope?> CurrentScope = new();
    private readonly TwilioWriteOnceScope? _previous;

    public int AttemptedPosts;

    private TwilioWriteOnceScope()
    {
        _previous = CurrentScope.Value;
        CurrentScope.Value = this;
    }

    public static TwilioWriteOnceScope? Current => CurrentScope.Value;

    public static TwilioWriteOnceScope Begin() => new();

    public void Dispose()
    {
        CurrentScope.Value = _previous;
    }
}

internal sealed class TwilioDuplicateWriteException : Exception
{
    public TwilioDuplicateWriteException() : base("A duplicate provider write was blocked.")
    {
    }
}
