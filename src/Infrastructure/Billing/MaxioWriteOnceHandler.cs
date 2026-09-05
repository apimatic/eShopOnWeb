using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>Prevents the SDK transport-retry pipeline from issuing a second subscription POST.</summary>
public sealed class MaxioWriteOnceHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> CurrentScope = new();

    public static IDisposable Begin(string subscriptionReference)
    {
        var previous = CurrentScope.Value;
        CurrentScope.Value = new WriteScope(subscriptionReference, previous);
        return new ScopeReset(previous);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = CurrentScope.Value;
        if (scope is not null && request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath.EndsWith("/subscriptions.json", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (Interlocked.Exchange(ref scope.SendCount, 1) != 0)
            {
                throw new MaxioWriteReplayBlockedException(scope.SubscriptionReference);
            }
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteScope(string subscriptionReference, WriteScope? previous)
    {
        public string SubscriptionReference { get; } = subscriptionReference;
        public WriteScope? Previous { get; } = previous;
        public int SendCount;
    }

    private sealed class ScopeReset(WriteScope? previous) : IDisposable
    {
        public void Dispose() => CurrentScope.Value = previous;
    }
}

public sealed class MaxioWriteReplayBlockedException(string subscriptionReference) : Exception
{
    public string SubscriptionReference { get; } = subscriptionReference;
}
