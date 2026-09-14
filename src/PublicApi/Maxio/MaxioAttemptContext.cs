using System;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public interface IMaxioAttemptContext
{
    IDisposable Begin(bool allowSingleWrite);
    HttpStatusCode? ResponseStatusCode { get; }
    bool TryBeginSend(HttpMethod method);
    void RecordResponse(HttpStatusCode statusCode);
}

public sealed class MaxioAttemptContext : IMaxioAttemptContext
{
    private readonly AsyncLocal<AttemptState?> _current = new();

    public HttpStatusCode? ResponseStatusCode => _current.Value?.ResponseStatusCode;

    public IDisposable Begin(bool allowSingleWrite)
    {
        var prior = _current.Value;
        _current.Value = new AttemptState(allowSingleWrite);
        return new Scope(() => _current.Value = prior);
    }

    public bool TryBeginSend(HttpMethod method)
    {
        var state = _current.Value;
        if (state is null || method != HttpMethod.Post || !state.AllowSingleWrite)
        {
            return true;
        }

        return Interlocked.Increment(ref state.WriteSendCount) == 1;
    }

    public void RecordResponse(HttpStatusCode statusCode)
    {
        if (_current.Value is { } state)
        {
            state.ResponseStatusCode = statusCode;
        }
    }

    private sealed class AttemptState
    {
        public AttemptState(bool allowSingleWrite) => AllowSingleWrite = allowSingleWrite;
        public bool AllowSingleWrite { get; }
        public int WriteSendCount;
        public HttpStatusCode? ResponseStatusCode;
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

public sealed class MaxioWriteReplayBlockedException : Exception
{
    public MaxioWriteReplayBlockedException()
        : base("A retry of a Maxio write was blocked because its outcome is unknown.") { }
}
