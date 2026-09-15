using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class SingleAttemptWriteGuard
{
    private readonly AsyncLocal<AttemptScope?> _current = new();

    public IDisposable Begin()
    {
        if (_current.Value is not null)
        {
            throw new InvalidOperationException("A provider write scope is already active.");
        }

        var scope = new AttemptScope(this);
        _current.Value = scope;
        return scope;
    }

    public void CountAttempt()
    {
        var scope = _current.Value;
        if (scope is not null && Interlocked.Increment(ref scope.Attempts) > 1)
        {
            throw new DuplicateProviderWriteAttemptException();
        }
    }

    private sealed class AttemptScope : IDisposable
    {
        private readonly SingleAttemptWriteGuard _owner;
        private bool _disposed;

        public AttemptScope(SingleAttemptWriteGuard owner) => _owner = owner;
        public int Attempts;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner._current.Value = null;
        }
    }
}

public sealed class SingleAttemptWriteHandler(SingleAttemptWriteGuard guard) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head && request.Method != HttpMethod.Options)
        {
            guard.CountAttempt();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class DuplicateProviderWriteAttemptException : Exception
{
    public DuplicateProviderWriteAttemptException()
        : base("A repeated provider write was blocked because the outcome of the first attempt is unknown.") { }
}
