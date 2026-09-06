using System.Threading;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of POST /api/subscriptions. The shopper is taken from the bearer token, never from the body.
/// </summary>
public class SubscribeRequestBody
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by GET /api/subscription-plans. Optional only
    /// when the deployment configures a default plan.
    /// </summary>
    public string? PlanHandle { get; set; }
}

/// <summary>
/// Everything POST /api/subscriptions needs: the body, the authenticated caller, and the optional
/// <c>Idempotency-Key</c> header. Assembled by the endpoint, never bound from the request body.
/// </summary>
public class SubscribeRequest : BaseRequest
{
    public SubscribeRequest(string? userName, string? planHandle, string? idempotencyKey, CancellationToken cancellationToken)
    {
        UserName = userName;
        PlanHandle = planHandle;
        IdempotencyKey = idempotencyKey;
        CancellationToken = cancellationToken;
    }

    public string? UserName { get; }

    public string? PlanHandle { get; }

    public string? IdempotencyKey { get; }

    public CancellationToken CancellationToken { get; }
}

/// <summary>Request for GET /api/subscription-plans.</summary>
public class ListSubscriptionPlansRequest : BaseRequest
{
    public ListSubscriptionPlansRequest(CancellationToken cancellationToken)
    {
        CancellationToken = cancellationToken;
    }

    public CancellationToken CancellationToken { get; }
}

/// <summary>Request for GET /api/my-subscriptions.</summary>
public class ListMySubscriptionsRequest : BaseRequest
{
    public ListMySubscriptionsRequest(string? userName, CancellationToken cancellationToken)
    {
        UserName = userName;
        CancellationToken = cancellationToken;
    }

    public string? UserName { get; }

    public CancellationToken CancellationToken { get; }
}
