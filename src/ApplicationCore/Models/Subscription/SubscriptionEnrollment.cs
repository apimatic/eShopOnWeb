namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

/// <summary>
/// The outcome of enrolling a shopper in a plan.
/// </summary>
public class SubscriptionEnrollment
{
    /// <summary>True when the enrollment created a new subscription; false when an equivalent live subscription already existed.</summary>
    public bool CreatedNew { get; set; }

    public int BillingCustomerId { get; set; }
    public string BillingCustomerReference { get; set; } = string.Empty;
    public SubscriptionSummary Subscription { get; set; } = new();
}
