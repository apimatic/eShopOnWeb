namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A plan a shopper can subscribe to, projected from the Maxio product family catalog.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int IntervalCount { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool HasTrial { get; set; }
    public long? TrialPriceInCents { get; set; }
    public int? TrialIntervalCount { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public long? SetupFeeInCents { get; set; }
    public bool RequiresPaymentMethod { get; set; }
    public bool Taxable { get; set; }
    public bool ExpiresNever { get; set; }
}
