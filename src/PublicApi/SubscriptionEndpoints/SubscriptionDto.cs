namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription to a plan, as recorded in Maxio Advanced Billing.
/// </summary>
public class SubscriptionDto
{
    /// <summary>
    /// The Billing API subscription id.
    /// </summary>
    public int Id { get; set; }

    public string State { get; set; } = string.Empty;

    /// <summary>
    /// True while the subscription represents a live relationship (not canceled/expired).
    /// </summary>
    public bool IsLive { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>
    /// The recurring price actually billed for this subscription.
    /// </summary>
    public decimal? Price { get; set; }

    public long? PriceInCents { get; set; }

    /// <summary>
    /// When the current period ends and the next renewal will occur.
    /// </summary>
    public System.DateTimeOffset? NextBillingDate { get; set; }

    public System.DateTimeOffset? ActivatedAt { get; set; }

    public System.DateTimeOffset? CreatedAt { get; set; }

    public bool? CancelAtEndOfPeriod { get; set; }

    /// <summary>
    /// The Billing API customer id backing this subscription.
    /// </summary>
    public int MaxioCustomerId { get; set; }
}