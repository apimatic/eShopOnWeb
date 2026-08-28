using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class SingleProviderWriteHandler : DelegatingHandler
{
    private static readonly AsyncLocal<AttemptScope?> CurrentScope = new();

    public static IDisposable BeginScope()
    {
        var previous = CurrentScope.Value;
        CurrentScope.Value = new AttemptScope();
        return new ScopeLease(previous);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = CurrentScope.Value;
        if (scope != null && Interlocked.Increment(ref scope.Attempts) > 1)
        {
            throw new DuplicateProviderWriteBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class AttemptScope
    {
        public int Attempts;
    }

    private sealed class ScopeLease : IDisposable
    {
        private readonly AttemptScope? _previous;
        private bool _disposed;

        public ScopeLease(AttemptScope? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CurrentScope.Value = _previous;
        }
    }
}

public sealed class DuplicateProviderWriteBlockedException : Exception
{
    public DuplicateProviderWriteBlockedException()
        : base("A provider write retry was blocked because its outcome is unknown.") { }
}
