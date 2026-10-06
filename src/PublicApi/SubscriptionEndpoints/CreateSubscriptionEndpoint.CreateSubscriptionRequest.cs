namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated shopper to a plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, e.g. "eshop-pro".</summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional caller-supplied key. Repeating a request with the same key cannot create
    /// a second subscription (it is forwarded to the billing system as a uniqueness token).
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Optional given name used when the shopper's billing customer is first provisioned.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional family name used when the shopper's billing customer is first provisioned.</summary>
    public string? LastName { get; set; }
}
