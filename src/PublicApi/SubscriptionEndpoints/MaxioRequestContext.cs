using System;
using System.Net;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioRequestContext
{
    private readonly AsyncLocal<State?> _current = new();

    public Scope Begin(bool guardPostResends = false)
    {
        var previous = _current.Value;
        var state = new State(guardPostResends);
        _current.Value = state;
        return new Scope(this, state, previous);
    }

    internal bool TryBeginSend(bool isPost)
    {
        var state = _current.Value;
        if (state is null || !state.GuardPostResends || !isPost)
        {
            return true;
        }

        return Interlocked.Increment(ref state.PostSendCount) == 1;
    }

    internal void Record(HttpStatusCode statusCode)
    {
        if (_current.Value is { } state)
        {
            state.LastStatusCode = statusCode;
        }
    }

    private void End(State state, State? previous)
    {
        if (ReferenceEquals(_current.Value, state))
        {
            _current.Value = previous;
        }
    }

    internal sealed class State(bool guardPostResends)
    {
        public bool GuardPostResends { get; } = guardPostResends;
        public int PostSendCount;
        public HttpStatusCode? LastStatusCode;
    }

    public sealed class Scope : IDisposable
    {
        private readonly MaxioRequestContext _owner;
        private readonly State _state;
        private readonly State? _previous;
        private bool _disposed;

        internal Scope(MaxioRequestContext owner, State state, State? previous)
        {
            _owner = owner;
            _state = state;
            _previous = previous;
        }

        public HttpStatusCode? LastStatusCode => _state.LastStatusCode;

        public void Dispose()
        {
            if (_disposed) return;
            _owner.End(_state, _previous);
            _disposed = true;
        }
    }
}

public sealed class MaxioWriteResendBlockedException : Exception
{
    public MaxioWriteResendBlockedException()
        : base("A retry of a guarded Maxio write was blocked.")
    {
    }
}
