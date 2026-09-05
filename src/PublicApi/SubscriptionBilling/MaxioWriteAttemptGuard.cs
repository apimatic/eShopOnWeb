using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

/// <summary>Prevents the SDK transport-retry policy from sending an enrollment write twice.</summary>
public sealed class MaxioWriteAttemptGuard : DelegatingHandler
{
    private static readonly AsyncLocal<AttemptScope?> CurrentScope = new();

    public static IDisposable BeginScope()
    {
        var previous = CurrentScope.Value;
        CurrentScope.Value = new AttemptScope(previous);
        return CurrentScope.Value;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = CurrentScope.Value;
        if (scope is not null && request.Method == HttpMethod.Post && !scope.TryAllow())
        {
            throw new MaxioWriteRetryBlockedException();
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class AttemptScope : IDisposable
    {
        private readonly AttemptScope? _previous;
        private int _writes;

        public AttemptScope(AttemptScope? previous) => _previous = previous;

        public bool TryAllow() => Interlocked.Increment(ref _writes) == 1;

        public void Dispose() => CurrentScope.Value = _previous;
    }
}
