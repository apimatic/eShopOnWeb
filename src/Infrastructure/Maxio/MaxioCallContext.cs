using System;
using System.Net;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioCallContext
{
    private readonly AsyncLocal<State?> _current = new();

    internal State? Current => _current.Value;

    public Scope Begin(bool writeOnce = false)
    {
        if (_current.Value is not null)
        {
            throw new InvalidOperationException("A Maxio call scope is already active.");
        }

        var state = new State(writeOnce);
        _current.Value = state;
        return new Scope(this, state);
    }

    internal sealed class State
    {
        public State(bool writeOnce) => WriteOnce = writeOnce;

        public bool WriteOnce { get; }
        public int PostSendCount;
        public HttpStatusCode? LastStatusCode { get; set; }
    }

    public sealed class Scope : IDisposable
    {
        private readonly MaxioCallContext _owner;
        private bool _disposed;

        internal Scope(MaxioCallContext owner, State state)
        {
            _owner = owner;
            State = state;
        }

        internal State State { get; }
        public HttpStatusCode? LastStatusCode => State.LastStatusCode;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (ReferenceEquals(_owner._current.Value, State))
            {
                _owner._current.Value = null;
            }
        }
    }
}

public sealed class MaxioWriteReplayPreventedException : Exception
{
    public MaxioWriteReplayPreventedException()
        : base("A replay of a non-idempotent Maxio request was prevented.") { }
}
