using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioWriteRetryBlockedException : Exception
{
    public MaxioWriteRetryBlockedException()
        : base("A repeated Maxio write was blocked; the outcome must be reconciled by reference.") { }
}

public sealed class MaxioWriteGuard
{
    private readonly AsyncLocal<WriteScope?> _current = new();

    public IDisposable Begin()
    {
        var prior = _current.Value;
        _current.Value = new WriteScope();
        return new ScopeLease(() => _current.Value = prior);
    }

    internal bool IsRepeatedWrite()
    {
        var scope = _current.Value;
        return scope != null && Interlocked.Increment(ref scope.SendCount) > 1;
    }

    private sealed class WriteScope
    {
        public int SendCount;
    }

    private sealed class ScopeLease : IDisposable
    {
        private Action? _dispose;

        public ScopeLease(Action dispose) => _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

public sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    private readonly MaxioWriteGuard _guard;

    public MaxioWriteOnceHandler(MaxioWriteGuard guard) => _guard = guard;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && _guard.IsRepeatedWrite())
        {
            throw new MaxioWriteRetryBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
