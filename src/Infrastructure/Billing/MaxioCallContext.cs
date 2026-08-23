using System;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioCallContext
{
    private readonly AsyncLocal<State?> _current = new();

    public HttpStatusCode? LastStatusCode => _current.Value?.LastStatusCode;

    public IDisposable Begin(bool blockRepeatedWrites)
    {
        var previous = _current.Value;
        _current.Value = new State(blockRepeatedWrites);
        return new Scope(() => _current.Value = previous);
    }

    public void BeforeSend(HttpMethod method)
    {
        var state = _current.Value;
        if (state is null || !state.BlockRepeatedWrites || method != HttpMethod.Post)
        {
            return;
        }

        if (Interlocked.Increment(ref state.WriteSendCount) > 1)
        {
            throw new MaxioRepeatedWriteBlockedException();
        }
    }

    public void RecordStatus(HttpStatusCode statusCode)
    {
        var state = _current.Value;
        if (state is not null)
        {
            state.LastStatusCode = statusCode;
        }
    }

    private sealed class State
    {
        public State(bool blockRepeatedWrites) => BlockRepeatedWrites = blockRepeatedWrites;

        public bool BlockRepeatedWrites { get; }
        public int WriteSendCount;
        public HttpStatusCode? LastStatusCode;
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

internal sealed class MaxioRepeatedWriteBlockedException : Exception
{
    public MaxioRepeatedWriteBlockedException()
        : base("A repeated Maxio write attempt was blocked pending reconciliation.")
    {
    }
}
