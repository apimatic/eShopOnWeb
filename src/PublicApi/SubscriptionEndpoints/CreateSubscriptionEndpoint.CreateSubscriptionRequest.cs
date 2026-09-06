namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, e.g. "eshop-pro". Optional: when omitted the
    /// configured default plan is used, or the only plan on offer if there is exactly one.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional key that makes a client-side retry safe: repeating a request with the same key
    /// will not create a second subscription. May also be sent as an "Idempotency-Key" header.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}
