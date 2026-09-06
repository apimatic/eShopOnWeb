namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by GET api/subscription-plans.
    /// Handles are used rather than numeric ids because ids are reassigned when a billing site is
    /// re-seeded, while handles are stable.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override of how the subscription is collected: <c>remittance</c> (invoice the
    /// customer, the default), <c>automatic</c> (charge the payment method on file) or
    /// <c>prepaid</c>. eShopOnWeb captures no card details, so the default is what works without one.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }
}
