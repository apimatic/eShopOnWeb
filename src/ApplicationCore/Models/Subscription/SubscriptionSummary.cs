namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

/// <summary>
/// A subscription held by the external billing system (Maxio Advanced Billing) for an eShopOnWeb user.
/// </summary>
public class SubscriptionSummary
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string? NextBillingDate { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CanceledAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
    public int BillingCustomerId { get; set; }

    /// <summary>
    /// True when an enroll request returned a subscription the user already held (idempotent replay)
    /// instead of creating a new one. Only meaningful for the result of SubscribeAsync.
    /// </summary>
    public bool ExistingReturned { get; set; }
}
