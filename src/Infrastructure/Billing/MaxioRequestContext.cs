using System;
using System.Net;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioRequestContext
{
    private readonly AsyncLocal<State?> _current = new();

    public Scope Begin(bool singleWrite)
    {
        var state = new State(singleWrite);
        var previous = _current.Value;
        _current.Value = state;
        return new Scope(this, state, previous);
    }

    public void BeforeSend(bool isPost)
    {
        var state = _current.Value;
        if (state is not null && state.SingleWrite && isPost && Interlocked.Increment(ref state.SendCount) > 1)
        {
            throw new MaxioRepeatWritePreventedException();
        }
    }

    public void RecordResponse(HttpStatusCode statusCode)
    {
        if (_current.Value is { } state)
        {
            state.StatusCode = statusCode;
        }
    }

    internal sealed class Scope : IDisposable
    {
        private readonly MaxioRequestContext _owner;
        private readonly State? _previous;
        private State? _state;

        internal Scope(MaxioRequestContext owner, State state, State? previous)
        {
            _owner = owner;
            _state = state;
            _previous = previous;
        }

        public HttpStatusCode? StatusCode => _state?.StatusCode;

        public void Dispose()
        {
            if (_state is not null)
            {
                _owner._current.Value = _previous;
                _state = null;
            }
        }
    }

    internal sealed class State
    {
        public State(bool singleWrite) => SingleWrite = singleWrite;
        public bool SingleWrite { get; }
        public int SendCount;
        public HttpStatusCode? StatusCode;
    }
}

internal sealed class MaxioRepeatWritePreventedException : Exception;

internal sealed class MaxioAmbiguousWriteException : Exception
{
    public MaxioAmbiguousWriteException(Exception innerException)
        : base("The Maxio write outcome is unknown.", innerException)
    {
    }
}
