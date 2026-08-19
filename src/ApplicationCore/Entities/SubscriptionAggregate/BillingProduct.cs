namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Product payload returned by the billing gateway (maps to a Maxio Product).
/// </summary>
public sealed class BillingProduct
{
    public int Id { get; init; }
    public string? Handle { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public string? ArchivedAt { get; init; }
}
