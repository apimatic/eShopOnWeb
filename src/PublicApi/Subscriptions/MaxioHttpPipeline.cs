using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class MaxioHttpCallContext
{
    private readonly AsyncLocal<State?> _current = new();

    public IDisposable Begin(bool write)
    {
        var previous = _current.Value;
        _current.Value = new State(write);
        return new Scope(() => _current.Value = previous);
    }

    public HttpStatusCode? LastStatusCode => _current.Value?.LastStatusCode;

    public void Record(HttpStatusCode statusCode)
    {
        if (_current.Value is { } state) state.LastStatusCode = statusCode;
    }

    public bool TryClaimWriteSend()
    {
        var state = _current.Value;
        return state is null || !state.IsWrite || Interlocked.Increment(ref state.SendCount) == 1;
    }

    private sealed class State(bool isWrite)
    {
        public bool IsWrite { get; } = isWrite;
        public int SendCount;
        public HttpStatusCode? LastStatusCode;
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

internal sealed class MaxioHttpPipelineHandler(MaxioHttpCallContext context) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && !context.TryClaimWriteSend())
        {
            throw new MaxioWriteReplayBlockedException();
        }

        var response = await base.SendAsync(request, cancellationToken);
        context.Record(response.StatusCode);
        return response;
    }
}
