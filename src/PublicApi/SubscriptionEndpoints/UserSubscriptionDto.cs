namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A user's subscription as recorded in Maxio Advanced Billing.
/// </summary>
public class UserSubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = "month";
    public DateTime? NextBillingDate { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
}
