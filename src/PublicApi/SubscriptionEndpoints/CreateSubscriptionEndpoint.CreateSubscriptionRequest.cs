namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET api/subscription-plans</c>. Required.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional key that scopes the enrollment. Repeating a request with the same plan and key
    /// always returns the same subscription rather than creating another. Leave it unset for the
    /// usual case of one subscription per shopper and plan; set it to subscribe to a plan a shopper
    /// has already held and finished.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}
