namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by GET /api/subscription-plans. Optional when the
    /// deployment configures a default plan or the catalog offers exactly one.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional replay key. Repeating a request with the same key returns the subscription the first call
    /// created rather than creating another. May also be supplied as an <c>Idempotency-Key</c> header.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}
