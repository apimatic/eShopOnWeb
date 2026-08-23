using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioHttpCallContext
{
    private readonly AsyncLocal<CallState?> _current = new();

    public CallState? Current => _current.Value;

    public IDisposable Begin(bool writeOnce)
    {
        var previous = _current.Value;
        var state = new CallState(writeOnce);
        _current.Value = state;
        return new Scope(() => _current.Value = previous);
    }

    public sealed class CallState
    {
        private int _postSendCount;

        internal CallState(bool writeOnce)
        {
            WriteOnce = writeOnce;
        }

        public bool WriteOnce { get; }
        public HttpStatusCode? LastStatusCode { get; internal set; }
        public bool IsRepeatedPost => Interlocked.Increment(ref _postSendCount) > 1;
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

public sealed class MaxioHttpHandler(MaxioHttpCallContext callContext) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var state = callContext.Current;
        if (state is { WriteOnce: true } &&
            request.Method == HttpMethod.Post &&
            state.IsRepeatedPost)
        {
            throw new MaxioWriteResendBlockedException();
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (state is not null)
        {
            state.LastStatusCode = response.StatusCode;
        }

        return response;
    }
}
