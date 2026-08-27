using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal sealed class SingleProviderWriteScope
{
    private readonly AsyncLocal<State?> _current = new();

    public IDisposable Begin()
    {
        var prior = _current.Value;
        _current.Value = new State();
        return new Scope(() => _current.Value = prior);
    }

    public void Claim()
    {
        var state = _current.Value;
        if (state is null)
        {
            return;
        }

        if (Interlocked.Increment(ref state.Attempts) > 1)
        {
            throw new DuplicateProviderWriteAttemptException();
        }
    }

    private sealed class State
    {
        public int Attempts;
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

internal sealed class SingleProviderWriteHandler(SingleProviderWriteScope scope) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post || request.Method == HttpMethod.Patch || request.Method == HttpMethod.Delete)
        {
            scope.Claim();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

internal sealed class DuplicateProviderWriteAttemptException : Exception
{
    public DuplicateProviderWriteAttemptException()
        : base("A provider write may already have taken effect; an automatic duplicate attempt was blocked.") { }
}
