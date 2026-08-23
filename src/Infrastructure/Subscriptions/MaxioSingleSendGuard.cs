using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public sealed class MaxioSingleSendGuard
{
    private readonly AsyncLocal<ScopeState?> _current = new();

    public IDisposable BeginSubscriptionCreate()
    {
        if (_current.Value is not null)
        {
            throw new InvalidOperationException("A Maxio subscription-create send scope is already active.");
        }

        var state = new ScopeState();
        _current.Value = state;
        return new Scope(this, state);
    }

    internal void BeforeSend(HttpRequestMessage request)
    {
        var state = _current.Value;
        if (state is null || request.Method != HttpMethod.Post)
        {
            return;
        }

        if (Interlocked.Increment(ref state.SendCount) > 1)
        {
            throw new MaxioDuplicateSendBlockedException();
        }
    }

    private void End(ScopeState state)
    {
        if (ReferenceEquals(_current.Value, state))
        {
            _current.Value = null;
        }
    }

    private sealed class ScopeState
    {
        public int SendCount;
    }

    private sealed class Scope : IDisposable
    {
        private readonly MaxioSingleSendGuard _guard;
        private readonly ScopeState _state;
        private int _disposed;

        public Scope(MaxioSingleSendGuard guard, ScopeState state)
        {
            _guard = guard;
            _state = state;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _guard.End(_state);
            }
        }
    }
}

internal sealed class MaxioDuplicateSendBlockedException : Exception
{
    public MaxioDuplicateSendBlockedException()
        : base("An automatic retry of a non-idempotent Maxio write was blocked.") { }
}

public sealed class MaxioSingleSendHandler : DelegatingHandler
{
    private readonly MaxioSingleSendGuard _guard;

    public MaxioSingleSendHandler(MaxioSingleSendGuard guard) => _guard = guard;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _guard.BeforeSend(request);
        return base.SendAsync(request, cancellationToken);
    }
}
