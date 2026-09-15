using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Stops the generated SDK's transport retry pipeline from issuing a second subscription POST.
/// A deterministic Maxio reference is used to reconcile an indeterminate first attempt.
/// </summary>
public sealed class SubscriptionWriteOnceHandler : DelegatingHandler
{
    private static readonly AsyncLocal<WriteScope?> CurrentScope = new();

    public static IDisposable Begin(string subscriptionReference)
    {
        var previous = CurrentScope.Value;
        CurrentScope.Value = new WriteScope(subscriptionReference);
        return new Scope(previous);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = CurrentScope.Value;
        if (scope is not null && request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath.EndsWith("/subscriptions.json", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (scope.Sent)
            {
                throw new DuplicateSubscriptionWriteAttemptException(scope.Reference);
            }

            scope.Sent = true;
        }

        return base.SendAsync(request, cancellationToken);
    }

    private sealed class WriteScope
    {
        public WriteScope(string reference) => Reference = reference;
        public string Reference { get; }
        public bool Sent { get; set; }
    }

    private sealed class Scope : IDisposable
    {
        private readonly WriteScope? _previous;
        public Scope(WriteScope? previous) => _previous = previous;
        public void Dispose() => CurrentScope.Value = _previous;
    }
}

public sealed class DuplicateSubscriptionWriteAttemptException : Exception
{
    public DuplicateSubscriptionWriteAttemptException(string reference) : base($"A subscription request for '{reference}' may already have reached Maxio.")
    {
    }
}
