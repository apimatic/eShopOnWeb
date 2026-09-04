using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioWriteRetryRefusedException : Exception
{
    public MaxioWriteRetryRefusedException()
        : base("A provider write retry was refused after the first attempt had an unknown outcome.") { }
}

public sealed class MaxioWriteOnceHandler : DelegatingHandler
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
        if (scope is not null && request.Method == HttpMethod.Post && Interlocked.Increment(ref scope.Sends) > 1)
        {
            throw new MaxioWriteRetryRefusedException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteScope
    {
        public int Sends;
    }

    private sealed class ScopeLease : IDisposable
    {
        private readonly WriteScope? _previous;
        private bool _disposed;

        public ScopeLease(WriteScope? previous) => _previous = previous;

        public void Dispose()
        {
            if (!_disposed)
            {
                CurrentScope.Value = _previous;
                _disposed = true;
            }
        }
    }
}
