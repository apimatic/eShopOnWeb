using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioWriteReplayBlockedException : Exception
{
    public MaxioWriteReplayBlockedException()
        : base("A Maxio write retry was blocked because its outcome may already exist.") { }
}

public sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> CurrentScope = new();

    public static IDisposable BeginScope()
    {
        var prior = CurrentScope.Value;
        var scope = new WriteScope(prior);
        CurrentScope.Value = scope;
        return scope;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post &&
            CurrentScope.Value is { } scope &&
            Interlocked.Increment(ref scope.SendCount) > 1)
        {
            throw new MaxioWriteReplayBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteScope : IDisposable
    {
        private readonly WriteScope? _prior;
        private int _disposed;

        public WriteScope(WriteScope? prior) => _prior = prior;

        public int SendCount;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                CurrentScope.Value = _prior;
            }
        }
    }
}
