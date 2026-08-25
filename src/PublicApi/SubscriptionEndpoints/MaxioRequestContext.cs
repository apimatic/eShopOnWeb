using System;
using System.Net;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal sealed class MaxioRequestContext
{
    private readonly AsyncLocal<State?> _current = new();

    public IDisposable Begin(bool writeOnce)
    {
        var previous = _current.Value;
        _current.Value = new State(writeOnce);
        return new Scope(this, previous);
    }

    public bool TryBeginWrite()
    {
        var state = _current.Value;
        if (state is null || !state.WriteOnce)
        {
            throw new InvalidOperationException("A Maxio POST must run inside a write-once scope.");
        }

        return Interlocked.Increment(ref state.WriteAttempts) == 1;
    }

    public void RecordStatus(HttpStatusCode statusCode)
    {
        var state = _current.Value;
        if (state is not null)
        {
            state.LastStatusCode = statusCode;
        }
    }

    public HttpStatusCode? LastStatusCode => _current.Value?.LastStatusCode;

    private sealed class State
    {
        public State(bool writeOnce) => WriteOnce = writeOnce;
        public bool WriteOnce { get; }
        public int WriteAttempts;
        public HttpStatusCode? LastStatusCode;
    }

    private sealed class Scope : IDisposable
    {
        private readonly MaxioRequestContext _owner;
        private readonly State? _previous;
        private bool _disposed;

        public Scope(MaxioRequestContext owner, State? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _owner._current.Value = _previous;
            _disposed = true;
        }
    }
}

internal sealed class MaxioWriteAlreadyAttemptedException : Exception
{
    public MaxioWriteAlreadyAttemptedException()
        : base("The Maxio write was not resent because its first outcome is unknown.") { }
}
