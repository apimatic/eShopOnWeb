using System;
using System.Net;
using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public sealed class MaxioResponseContext : IMaxioResponseContext
{
    private readonly AsyncLocal<State?> _state = new();

    public HttpStatusCode? LastStatusCode => _state.Value?.LastStatusCode;

    public IDisposable BeginScope()
    {
        var previous = _state.Value;
        _state.Value = new State();
        return new Scope(() => _state.Value = previous);
    }

    public void Record(HttpStatusCode statusCode)
    {
        if (_state.Value is not null)
        {
            _state.Value.LastStatusCode = statusCode;
        }
    }

    private sealed class State
    {
        public HttpStatusCode? LastStatusCode { get; set; }
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

public sealed class MaxioWriteGuard : IMaxioWriteGuard
{
    private readonly AsyncLocal<State?> _state = new();

    public IDisposable BeginScope()
    {
        var previous = _state.Value;
        _state.Value = new State();
        return new Scope(() => _state.Value = previous);
    }

    public bool TryMarkPost()
    {
        var state = _state.Value;
        return state is null || Interlocked.Increment(ref state.PostCount) == 1;
    }

    private sealed class State
    {
        public int PostCount;
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

public sealed class MaxioWriteReplayBlockedException : Exception
{
    public MaxioWriteReplayBlockedException()
        : base("A provider write retry was blocked because the first attempt may have succeeded.")
    {
    }
}
