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
    public decimal Price { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
}
