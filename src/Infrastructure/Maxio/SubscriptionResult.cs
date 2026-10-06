namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>A subscription as surfaced to the API layer.</summary>
public class SubscriptionResult
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public string PaymentCollectionMethod { get; set; } = string.Empty;

    /// <summary>True when the subscription was created by this request; false when an existing subscription was returned (idempotent replay).</summary>
    public bool IsNew { get; set; }
}
