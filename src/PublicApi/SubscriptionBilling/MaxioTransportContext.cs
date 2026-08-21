using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public sealed class MaxioTransportContext
{
    private readonly AsyncLocal<OperationState?> _current = new();

    public HttpStatusCode? LastStatusCode => _current.Value?.LastStatusCode;

    public IDisposable BeginOperation(bool singleSend)
    {
        var prior = _current.Value;
        _current.Value = new OperationState(singleSend);
        return new Scope(() => _current.Value = prior);
    }

    internal void BeforeSend(HttpRequestMessage request)
    {
        var state = _current.Value;
        if (state is null || !state.SingleSend || request.Method != HttpMethod.Post)
        {
            return;
        }

        if (state.SendCount > 0)
        {
            throw new MaxioDuplicateSendBlockedException();
        }

        state.SendCount++;
    }

    internal void RecordResponse(HttpStatusCode statusCode)
    {
        if (_current.Value is { } state)
        {
            state.LastStatusCode = statusCode;
        }
    }

    private sealed class OperationState
    {
        public OperationState(bool singleSend) => SingleSend = singleSend;
        public bool SingleSend { get; }
        public int SendCount { get; set; }
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

public sealed class MaxioTransportHandler : DelegatingHandler
{
    private readonly MaxioTransportContext _context;

    public MaxioTransportHandler(MaxioTransportContext context) => _context = context;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _context.BeforeSend(request);
        var response = await base.SendAsync(request, cancellationToken);
        _context.RecordResponse(response.StatusCode);
        return response;
    }
}

public sealed class MaxioDuplicateSendBlockedException : Exception
{
    public MaxioDuplicateSendBlockedException()
        : base("A repeated non-idempotent Maxio send was blocked.")
    {
    }
}
