using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Server-side subscribe request: the shopper's plan choice combined with the identity taken from
/// the bearer token. Never bound from the wire.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    public CreateSubscriptionRequest(SubscriberIdentity subscriber, string planHandle,
        string? idempotencyKey = null, CancellationToken cancellationToken = default)
    {
        Subscriber = subscriber;
        PlanHandle = planHandle;
        IdempotencyKey = idempotencyKey;
        CancellationToken = cancellationToken;
    }

    public SubscriberIdentity Subscriber { get; }

    public string PlanHandle { get; }

    public string? IdempotencyKey { get; }

    public CancellationToken CancellationToken { get; }
}
