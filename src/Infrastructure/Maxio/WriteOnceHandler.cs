using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class WriteOnceHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> CurrentScope = new();

    public static IDisposable BeginScope()
    {
        var previous = CurrentScope.Value;
        CurrentScope.Value = new WriteScope();
        return new ScopeLease(previous);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = CurrentScope.Value;
        if (request.Method == HttpMethod.Post && scope is not null && Interlocked.Increment(ref scope.SendCount) > 1)
        {
            throw new MaxioWriteReplayBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteScope
    {
        public int SendCount;
    }

    private sealed class ScopeLease : IDisposable
    {
        private readonly WriteScope? _previous;
        private bool _disposed;

        public ScopeLease(WriteScope? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            CurrentScope.Value = _previous;
            _disposed = true;
        }
    }
}

public sealed class MaxioWriteReplayBlockedException : Exception
{
    public MaxioWriteReplayBlockedException()
        : base("A replay of a Maxio write was blocked because its outcome is unknown.") { }
}
