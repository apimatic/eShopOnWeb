namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A billable plan sourced from Maxio Advanced Billing (a Product in the configured family).
/// </summary>
public class SubscriptionPlan
{
    public int Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string? ProductFamilyHandle { get; init; }
    public bool RequireCreditCard { get; init; }
}
