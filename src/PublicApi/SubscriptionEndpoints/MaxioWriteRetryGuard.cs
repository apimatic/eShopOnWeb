using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Prevents the SDK transport retry pipeline from resending a POST. A scope surrounds
/// one logical write, while the AsyncLocal state survives the SDK's fresh request objects.
/// </summary>
public sealed class MaxioWriteRetryGuard
{
    private readonly AsyncLocal<WriteScopeState?> _state = new();

    public IDisposable BeginWrite()
    {
        if (_state.Value is not null)
        {
            throw new InvalidOperationException("A Maxio write scope is already active.");
        }

        _state.Value = new WriteScopeState();
        return new Scope(_state);
    }

    internal void CountPost()
    {
        var state = _state.Value;
        if (state is null)
        {
            return;
        }

        if (Interlocked.Increment(ref state.PostAttempts) > 1)
        {
            throw new MaxioWriteRetryBlockedException();
        }
    }

    private sealed class WriteScopeState
    {
        public int PostAttempts;
    }

    private sealed class Scope : IDisposable
    {
        private readonly AsyncLocal<WriteScopeState?> _state;
        private int _disposed;

        public Scope(AsyncLocal<WriteScopeState?> state) => _state = state;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _state.Value = null;
            }
        }
    }
}

public sealed class MaxioWriteRetryGuardHandler : DelegatingHandler
{
    private readonly MaxioWriteRetryGuard _guard;

    public MaxioWriteRetryGuardHandler(MaxioWriteRetryGuard guard) => _guard = guard;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post)
        {
            _guard.CountPost();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class MaxioWriteRetryBlockedException : Exception
{
    public MaxioWriteRetryBlockedException()
        : base("A Maxio write retry was blocked; its outcome must be reconciled.")
    {
    }
}
