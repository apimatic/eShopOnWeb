using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class ProviderWriteGuard
{
    private readonly AsyncLocal<WriteState?> _current = new();

    public IDisposable BeginScope()
    {
        var previous = _current.Value;
        _current.Value = new WriteState();
        return new Scope(() => _current.Value = previous);
    }

    internal void AssertMaySend()
    {
        var state = _current.Value;
        if (state is not null && Interlocked.Increment(ref state.Attempts) > 1)
        {
            throw new ProviderWriteRetryBlockedException();
        }
    }

    private sealed class WriteState
    {
        public int Attempts;
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

public sealed class ProviderWriteGuardHandler(ProviderWriteGuard guard) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post)
        {
            guard.AssertMaySend();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class ProviderWriteRetryBlockedException : Exception
{
    public ProviderWriteRetryBlockedException()
        : base("A provider write retry was blocked because its outcome is unknown.") { }
}
