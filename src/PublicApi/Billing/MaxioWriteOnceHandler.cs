using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public sealed class MaxioWriteOnceCoordinator
{
    private readonly AsyncLocal<WriteScope?> _current = new();

    public IDisposable Begin()
    {
        var previous = _current.Value;
        _current.Value = new WriteScope();
        return new ScopeLease(() => _current.Value = previous);
    }

    public bool TryClaimSend() => _current.Value is null || Interlocked.Increment(ref _current.Value.SendCount) == 1;

    private sealed class WriteScope { public int SendCount; }

    private sealed class ScopeLease(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

public sealed class MaxioWriteOnceHandler(MaxioWriteOnceCoordinator coordinator) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && !coordinator.TryClaimSend())
        {
            throw new MaxioWriteAlreadyAttemptedException();
        }
        return base.SendAsync(request, cancellationToken);
    }
}
