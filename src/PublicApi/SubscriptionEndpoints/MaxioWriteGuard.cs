using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioWriteGuard
{
    private readonly AsyncLocal<WriteScope?> _current = new();

    public IDisposable BeginSinglePostScope()
    {
        var previous = _current.Value;
        _current.Value = new WriteScope();
        return new ScopeLease(() => _current.Value = previous);
    }

    internal bool TryClaimPost() =>
        _current.Value is null || Interlocked.Increment(ref _current.Value.PostCount) == 1;

    private sealed class WriteScope
    {
        public int PostCount;
    }

    private sealed class ScopeLease : IDisposable
    {
        private Action? _dispose;
        public ScopeLease(Action dispose) => _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

public sealed class MaxioSingleWriteHandler : DelegatingHandler
{
    private readonly MaxioWriteGuard _guard;

    public MaxioSingleWriteHandler(MaxioWriteGuard guard) => _guard = guard;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && !_guard.TryClaimPost())
        {
            throw new MaxioWriteReplayPreventedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class MaxioWriteReplayPreventedException : Exception
{
    public MaxioWriteReplayPreventedException()
        : base("A replay of a Maxio write was prevented; its outcome must be reconciled.")
    {
    }
}
