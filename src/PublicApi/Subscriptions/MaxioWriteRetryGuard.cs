using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Stops the SDK's transport retry policy from sending a non-idempotent POST twice.
/// State intentionally outlives an individual HttpRequestMessage, which the SDK recreates per retry.
/// </summary>
public sealed class MaxioWriteRetryGuard : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> CurrentScope = new();

    public static IDisposable BeginScope()
    {
        var priorScope = CurrentScope.Value;
        CurrentScope.Value = new WriteScope();
        return new ScopeLease(priorScope);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = CurrentScope.Value;
        if (scope is not null && request.Method == HttpMethod.Post && Interlocked.Exchange(ref scope.PostSent, 1) != 0)
        {
            throw new MaxioWriteRetryPreventedException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteScope
    {
        public int PostSent;
    }

    private sealed class ScopeLease : IDisposable
    {
        private readonly WriteScope? _priorScope;

        public ScopeLease(WriteScope? priorScope) => _priorScope = priorScope;

        public void Dispose() => CurrentScope.Value = _priorScope;
    }
}

public sealed class MaxioWriteRetryPreventedException : Exception
{
    public MaxioWriteRetryPreventedException()
        : base("A retry of a Maxio write was prevented.")
    {
    }
}
