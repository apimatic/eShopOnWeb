namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The metered add-on that pay-as-you-go usage is reported against (UC2). Lives on the product family, so
/// it is available to every subscription on any plan in that family.
/// </summary>
public class MeteredComponentDefinition
{
    public MeteredComponentDefinition(int id, string handle, string name, string? unitName, decimal unitPrice, bool isMetered)
    {
        Id = id;
        Handle = handle;
        Name = name;
        UnitName = unitName;
        UnitPrice = unitPrice;
        IsMetered = isMetered;
    }

    public int Id { get; }

    public string Handle { get; }

    public string Name { get; }

    public string? UnitName { get; }

    /// <summary>Price of a single unit, in whole currency units.</summary>
    public decimal UnitPrice { get; }

    /// <summary>False when the configured handle resolved to a component of some other kind.</summary>
    public bool IsMetered { get; }
}
