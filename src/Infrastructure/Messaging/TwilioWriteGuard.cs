using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioWriteGuard
{
    private readonly AsyncLocal<State?> _current = new();

    public IDisposable Begin()
    {
        var previous = _current.Value;
        _current.Value = new State();
        return new Scope(() => _current.Value = previous);
    }

    public void BeforeSend()
    {
        var state = _current.Value;
        if (state is not null && Interlocked.Increment(ref state.Attempts) > 1)
        {
            throw new TwilioDuplicateWriteBlockedException();
        }
    }

    private sealed class State { public int Attempts; }
    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

public sealed class TwilioWriteGuardHandler(TwilioWriteGuard guard) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            guard.BeforeSend();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class TwilioDuplicateWriteBlockedException : Exception
{
    public TwilioDuplicateWriteBlockedException()
        : base("A repeated provider write was blocked because the outcome of the first attempt is unknown.") { }
}
