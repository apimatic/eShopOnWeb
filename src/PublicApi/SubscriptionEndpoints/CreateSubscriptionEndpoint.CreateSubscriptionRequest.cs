using System.Threading;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of POST /api/subscriptions. The account being subscribed is never taken from here: it comes
/// from the bearer token.
/// </summary>
public class SubscribeRequestBody
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by GET /api/subscription-plans. Optional only
    /// when the host configures a default plan.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional client-supplied key that scopes duplicate suppression. Leaving it out is safe: the
    /// endpoint still refuses to create a second subscription to the same plan for the same shopper.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Internal request for <see cref="CreateSubscriptionEndpoint"/>, combining the request body with
/// the identity resolved from the token.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    public CreateSubscriptionRequest(SubscriberIdentity subscriber, SubscribeRequestBody body, CancellationToken cancellationToken)
    {
        Subscriber = subscriber;
        Body = body;
        CancellationToken = cancellationToken;
    }

    public SubscriberIdentity Subscriber { get; }

    public SubscribeRequestBody Body { get; }

    public CancellationToken CancellationToken { get; }
}
