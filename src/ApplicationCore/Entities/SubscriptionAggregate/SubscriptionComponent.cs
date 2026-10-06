namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A billable component (e.g. a metered add-on) that can be attached to a subscription plan.
/// </summary>
public class SubscriptionComponent
{
    public SubscriptionComponent(int id, string handle, string name, string kind, string? unitName, decimal? unitPrice, int? pricePerUnitInCents)
    {
        Id = id;
        Handle = handle;
        Name = name;
        Kind = kind;
        UnitName = unitName;
        UnitPrice = unitPrice;
        PricePerUnitInCents = pricePerUnitInCents;
    }

    public int Id { get; }
    public string Handle { get; }
    public string Name { get; }
    public string Kind { get; }
    public string? UnitName { get; }
    public decimal? UnitPrice { get; }
    public int? PricePerUnitInCents { get; }
}
