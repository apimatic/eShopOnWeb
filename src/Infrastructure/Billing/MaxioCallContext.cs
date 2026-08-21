using System;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioCallContext
{
    private readonly AsyncLocal<CallState?> _current = new();

    public HttpStatusCode? LastStatusCode => _current.Value?.LastStatusCode;

    public IDisposable Begin(bool writeOnce)
    {
        var previous = _current.Value;
        _current.Value = new CallState(writeOnce);
        return new Scope(() => _current.Value = previous);
    }

    public void BeforeSend(HttpRequestMessage request)
    {
        var state = _current.Value;
        if (state is null || !state.WriteOnce || request.Method != HttpMethod.Post)
        {
            return;
        }

        state.SendCount++;
        if (state.SendCount > 1)
        {
            throw new MaxioWriteRetryBlockedException();
        }
    }

    public void RecordResponse(HttpStatusCode statusCode)
    {
        if (_current.Value is { } state)
        {
            state.LastStatusCode = statusCode;
        }
    }

    private sealed class CallState(bool writeOnce)
    {
        public bool WriteOnce { get; } = writeOnce;
        public int SendCount { get; set; }
        public HttpStatusCode? LastStatusCode { get; set; }
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

public sealed class MaxioWriteRetryBlockedException : Exception
{
    public MaxioWriteRetryBlockedException()
        : base("A retry of a non-idempotent Maxio write was blocked.")
    {
    }
}
