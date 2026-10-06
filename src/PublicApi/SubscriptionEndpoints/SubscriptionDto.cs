namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A user's subscription as recorded in the billing system.
/// </summary>
public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string Price { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }

    /// <summary>
    /// Next billing date (current period end) in ISO 8601.
    /// </summary>
    public string? NextBillingDate { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CreatedAt { get; set; }
    public string? CanceledAt { get; set; }

    /// <summary>
    /// Id of the Maxio Advanced Billing customer representing this user.
    /// </summary>
    public int BillingCustomerId { get; set; }
}