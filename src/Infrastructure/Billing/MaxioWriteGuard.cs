using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioWriteGuard
{
    private readonly AsyncLocal<WriteState?> _current = new();

    public WriteScope BeginWrite()
    {
        if (_current.Value is not null)
        {
            throw new InvalidOperationException("A Maxio write scope is already active.");
        }

        var state = new WriteState();
        _current.Value = state;
        return new WriteScope(this, state);
    }

    public bool TryBeginSend()
    {
        var state = _current.Value;
        return state is null || Interlocked.Increment(ref state.SendCount) == 1;
    }

    public void RecordStatus(HttpStatusCode statusCode)
    {
        if (_current.Value is { } state)
        {
            state.LastStatusCode = statusCode;
        }
    }

    private void End(WriteState state)
    {
        if (ReferenceEquals(_current.Value, state))
        {
            _current.Value = null;
        }
    }

    internal sealed class WriteState
    {
        public int SendCount;
        public HttpStatusCode? LastStatusCode;
    }

    public sealed class WriteScope : IDisposable
    {
        private readonly MaxioWriteGuard _owner;
        private readonly WriteState _state;
        private bool _disposed;

        internal WriteScope(MaxioWriteGuard owner, WriteState state)
        {
            _owner = owner;
            _state = state;
        }

        public HttpStatusCode? LastStatusCode => _state.LastStatusCode;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner.End(_state);
        }
    }
}

internal sealed class MaxioWriteGuardHandler : DelegatingHandler
{
    private readonly MaxioWriteGuard _guard;

    public MaxioWriteGuardHandler(MaxioWriteGuard guard)
    {
        _guard = guard;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post && !_guard.TryBeginSend())
        {
            throw new MaxioWriteResendBlockedException();
        }

        var response = await base.SendAsync(request, cancellationToken);
        _guard.RecordStatus(response.StatusCode);
        return response;
    }
}

internal sealed class MaxioWriteResendBlockedException : Exception
{
    public MaxioWriteResendBlockedException()
        : base("A retry attempted to resend a Maxio write.") { }
}
