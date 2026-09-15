using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Stops the SDK's transport retry policy from issuing a second external POST in one write scope.
/// </summary>
public sealed class MaxioSingleSendHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> CurrentScope = new();

    public static IDisposable BeginWriteScope()
    {
        var previous = CurrentScope.Value;
        CurrentScope.Value = new WriteScope();
        return new ScopeLease(previous);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = CurrentScope.Value;
        if (scope is not null && request.Method == HttpMethod.Post && !scope.TryMarkSent())
        {
            throw new MaxioWriteRetryBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteScope
    {
        private int _sent;
        public bool TryMarkSent() => Interlocked.Exchange(ref _sent, 1) == 0;
    }

    private sealed class ScopeLease : IDisposable
    {
        private readonly WriteScope? _previous;
        public ScopeLease(WriteScope? previous) => _previous = previous;
        public void Dispose() => CurrentScope.Value = _previous;
    }
}

public sealed class MaxioWriteRetryBlockedException : Exception
{
    public MaxioWriteRetryBlockedException()
        : base("A repeated Maxio write was blocked while its outcome is reconciled.")
    {
    }
}
