using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

// The generated SDK retries transport failures, including for POST. This guard ensures
// that a retry cannot send a second provider write during one logical create operation.
public sealed class MaxioWriteGuardHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> CurrentScope = new();

    public static IDisposable BeginScope() => new ScopeLease();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && CurrentScope.Value is { } scope && Interlocked.Exchange(ref scope.HasSent, 1) != 0)
        {
            throw new MaxioWriteResendException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteScope
    {
        public int HasSent;
    }

    private sealed class ScopeLease : IDisposable
    {
        private readonly WriteScope? _previous;
        private bool _disposed;

        public ScopeLease()
        {
            _previous = CurrentScope.Value;
            CurrentScope.Value = new WriteScope();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                CurrentScope.Value = _previous;
                _disposed = true;
            }
        }
    }
}

public sealed class MaxioWriteResendException : Exception
{
    public MaxioWriteResendException()
        : base("A provider write retry was blocked and must be reconciled.")
    {
    }
}
