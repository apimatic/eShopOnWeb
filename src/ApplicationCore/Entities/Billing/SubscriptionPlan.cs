namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

/// <summary>
/// A Maxio product offered as a recurring subscription plan.
/// </summary>
public sealed class SubscriptionPlan
{
    public int Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public decimal Price => PriceInCents / 100m;
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public long? TrialPriceInCents { get; init; }
    public bool RequireCreditCard { get; init; }
    public string? ProductFamilyHandle { get; init; }
}
