using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class MaxioRequestContext
{
    private readonly AsyncLocal<State?> _current = new();

    public HttpStatusCode? LastStatusCode => _current.Value?.LastStatusCode;

    public IDisposable Begin(bool singleWrite)
    {
        var previous = _current.Value;
        _current.Value = new State(singleWrite);
        return new Scope(() => _current.Value = previous);
    }

    public void BeforeSend(HttpMethod method)
    {
        var state = _current.Value;
        if (state == null || method != HttpMethod.Post)
        {
            return;
        }

        if (!state.SingleWrite)
        {
            throw new InvalidOperationException("A Maxio write was attempted outside a write-once scope.");
        }

        if (state.WriteSent)
        {
            throw new MaxioWriteRetryBlockedException();
        }

        state.WriteSent = true;
    }

    public void AfterSend(HttpStatusCode statusCode)
    {
        if (_current.Value != null)
        {
            _current.Value.LastStatusCode = statusCode;
        }
    }

    private sealed class State
    {
        public State(bool singleWrite) => SingleWrite = singleWrite;
        public bool SingleWrite { get; }
        public bool WriteSent { get; set; }
        public HttpStatusCode? LastStatusCode { get; set; }
    }

    private sealed class Scope : IDisposable
    {
        private readonly Action _dispose;
        private int _disposed;

        public Scope(Action dispose) => _dispose = dispose;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _dispose();
            }
        }
    }
}

internal sealed class MaxioRequestContextHandler : DelegatingHandler
{
    private readonly MaxioRequestContext _context;

    public MaxioRequestContextHandler(MaxioRequestContext context) => _context = context;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _context.BeforeSend(request.Method);
        var response = await base.SendAsync(request, cancellationToken);
        _context.AfterSend(response.StatusCode);
        return response;
    }
}

internal sealed class MaxioWriteRetryBlockedException : Exception
{
    public MaxioWriteRetryBlockedException()
        : base("An automatic retry of a Maxio write was blocked because its outcome is unknown.") { }
}
