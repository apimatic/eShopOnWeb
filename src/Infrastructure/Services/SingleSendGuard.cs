using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The SDK retries transport failures on every verb, including non-idempotent POSTs, so a
/// connection reset after the bytes reached the provider would send a customer-facing SMS
/// twice. Code that sends opens <see cref="SingleSendScope.Enter"/>; this handler counts the
/// send before it goes out and refuses any attempt it did not authorise. The refusal is a
/// plain Exception derivative — never HttpRequestException, which the retry pipeline would
/// retry — and the caller surfaces the send as an unknown outcome to be settled by re-reading
/// provider state.
/// </summary>
public sealed class SingleSendGuardHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (SingleSendScope.TryMarkSentAndCheckBlocked())
        {
            throw new DuplicateSendBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class SingleSendScope : IDisposable
{
    private static readonly AsyncLocal<SingleSendScope?> _current = new();

    private int _sentCount;

    private SingleSendScope() {}

    /// <summary>Opens a scope around exactly one logical send. Dispose when the send call returns.</summary>
    public static SingleSendScope Enter()
    {
        var scope = new SingleSendScope();
        _current.Value = scope;
        return scope;
    }

    /// <summary>Returns true when this scope has already let one attempt through.</summary>
    public static bool TryMarkSentAndCheckBlocked()
    {
        var scope = _current.Value;
        if (scope is null)
        {
            return false;
        }

        if (scope._sentCount > 0)
        {
            return true;
        }

        scope._sentCount++;
        return false;
    }

    public void Dispose()
    {
        if (ReferenceEquals(_current.Value, this))
        {
            _current.Value = null;
        }
    }
}

public sealed class DuplicateSendBlockedException : Exception
{
    public DuplicateSendBlockedException()
        : base("A duplicate send attempt was blocked; the first attempt's outcome is unknown.")
    {
    }
}
