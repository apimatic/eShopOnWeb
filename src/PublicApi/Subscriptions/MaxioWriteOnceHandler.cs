using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioWriteRetryBlockedException : Exception
{
    public MaxioWriteRetryBlockedException()
        : base("A repeated provider write was blocked because the first attempt may have succeeded.")
    {
    }
}

public sealed class MaxioWriteOnceScope
{
    private readonly AsyncLocal<ScopeState?> _current = new();

    public IDisposable Begin()
    {
        if (_current.Value is not null)
        {
            throw new InvalidOperationException("A Maxio write-once scope is already active.");
        }

        _current.Value = new ScopeState();
        return new Release(this);
    }

    internal bool TryClaimSend()
    {
        var state = _current.Value;
        return state is null || Interlocked.Increment(ref state.SendCount) == 1;
    }

    private sealed class ScopeState
    {
        public int SendCount;
    }

    private sealed class Release(MaxioWriteOnceScope owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            owner._current.Value = null;
            _disposed = true;
        }
    }
}

public sealed class MaxioWriteOnceHandler(MaxioWriteOnceScope scope) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && !scope.TryClaimSend())
        {
            throw new MaxioWriteRetryBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
