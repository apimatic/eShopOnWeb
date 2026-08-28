using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class TwilioRequestContext
{
    private readonly AsyncLocal<State?> _current = new();

    public State? Current => _current.Value;

    public IDisposable Begin(bool singleNetworkAttempt)
    {
        var previous = _current.Value;
        _current.Value = new State(singleNetworkAttempt);
        return new Scope(() => _current.Value = previous);
    }

    public sealed class State(bool singleNetworkAttempt)
    {
        private int _attempts;
        public bool SingleNetworkAttempt { get; } = singleNetworkAttempt;
        public HttpStatusCode? LastStatusCode { get; set; }
        public int RegisterAttempt() => Interlocked.Increment(ref _attempts);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

public sealed class TwilioDuplicateAttemptBlockedException : Exception
{
    public TwilioDuplicateAttemptBlockedException()
        : base("A provider write retry was blocked because the first attempt may have taken effect.") { }
}

public sealed class TwilioWriteGuardHandler(TwilioRequestContext context) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var state = context.Current;
        if (state is not null && state.SingleNetworkAttempt && state.RegisterAttempt() > 1)
            throw new TwilioDuplicateAttemptBlockedException();

        var response = await base.SendAsync(request, cancellationToken);
        if (state is not null) state.LastStatusCode = response.StatusCode;
        return response;
    }
}
