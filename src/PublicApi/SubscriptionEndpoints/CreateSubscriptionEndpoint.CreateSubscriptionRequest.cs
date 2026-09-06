using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of POST /api/subscriptions. Deliberately carries no identity: the shopper is taken from
/// the bearer token so a caller cannot subscribe on somebody else's behalf.
/// </summary>
public class SubscribeToPlanRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by GET /api/subscription-plans.
    /// Optional when the deployment configures Maxio:DefaultPlanHandle.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>Optional non-default price point handle for the plan.</summary>
    public string? PricePointHandle { get; set; }

    /// <summary>
    /// Optional key making the call safely repeatable. The Idempotency-Key request header is
    /// accepted as an alternative and takes precedence.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Endpoint input: the request body combined with the identity resolved from the bearer token.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    public CreateSubscriptionRequest(
        Subscriber subscriber,
        string planHandle,
        string? pricePointHandle = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        Subscriber = subscriber;
        PlanHandle = planHandle;
        PricePointHandle = pricePointHandle;
        IdempotencyKey = idempotencyKey;
        CancellationToken = cancellationToken;
    }

    public Subscriber Subscriber { get; }

    public string PlanHandle { get; }

    public string? PricePointHandle { get; }

    public string? IdempotencyKey { get; }

    public CancellationToken CancellationToken { get; }
}
