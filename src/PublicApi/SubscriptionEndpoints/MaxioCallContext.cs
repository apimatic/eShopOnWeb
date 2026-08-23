using System;
using System.Net;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioCallContext
{
    private readonly AsyncLocal<State?> _state = new();

    public HttpStatusCode? LastStatusCode
    {
        get => _state.Value?.LastStatusCode;
        set
        {
            if (_state.Value is not null)
            {
                _state.Value.LastStatusCode = value;
            }
        }
    }

    public IDisposable Begin(bool atMostOneWrite = false)
    {
        var prior = _state.Value;
        _state.Value = new State(atMostOneWrite);
        return new Scope(() => _state.Value = prior);
    }

    public void RegisterWrite()
    {
        var state = _state.Value;
        if (state?.AtMostOneWrite != true)
        {
            return;
        }

        if (Interlocked.Increment(ref state.WriteCount) > 1)
        {
            throw new MaxioWriteRetryBlockedException();
        }
    }

    private sealed class State(bool atMostOneWrite)
    {
        public bool AtMostOneWrite { get; } = atMostOneWrite;
        public int WriteCount;
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
