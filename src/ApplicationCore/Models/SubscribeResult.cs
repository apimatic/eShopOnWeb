namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// Outcome of a subscribe request. Created is false when the subscription already
/// existed in the billing system (idempotent re-subscribe).
/// </summary>
public class SubscribeResult
{
    public SubscriptionInfo Subscription { get; set; } = new SubscriptionInfo();
    public bool Created { get; set; }
    public int BillingCustomerId { get; set; }
}
