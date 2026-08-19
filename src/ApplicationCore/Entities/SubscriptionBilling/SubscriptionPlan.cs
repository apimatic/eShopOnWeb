namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

/// <summary>
/// A billable plan sourced from Maxio (a Product in a Product Family).
/// </summary>
public sealed class SubscriptionPlan
{
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public int Interval { get; init; }
    public required string IntervalUnit { get; init; }
    public bool RequiresPaymentMethod { get; init; }
    public string? ProductFamilyHandle { get; init; }
}
