namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// A Maxio product offered as a recurring subscription plan.
/// </summary>
public sealed class SubscriptionPlan
{
    public int ProductId { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal Price { get; init; }
    public string Currency { get; init; } = "USD";
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = "month";
    public string ProductFamilyHandle { get; init; } = string.Empty;
}
