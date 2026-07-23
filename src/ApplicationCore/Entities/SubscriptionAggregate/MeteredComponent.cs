namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A pay-as-you-go component available to subscriptions on a product family.
/// UC2 refuses to record usage unless the configured handle resolves to one of these with
/// <see cref="IsMetered"/> true.
/// </summary>
public class MeteredComponent
{
    public MeteredComponent(int id,
        string handle,
        string name,
        string kind,
        string? pricingScheme,
        decimal? unitPrice,
        bool archived)
    {
        Id = id;
        Handle = handle;
        Name = name;
        Kind = kind;
        PricingScheme = pricingScheme;
        UnitPrice = unitPrice;
        Archived = archived;
    }

    public int Id { get; private set; }
    public string Handle { get; private set; }
    public string Name { get; private set; }

    /// <summary>The provider's component kind, e.g. "metered_component".</summary>
    public string Kind { get; private set; }

    public string? PricingScheme { get; private set; }

    /// <summary>Price per unit in whole currency units (e.g. 0.01), not minor units.</summary>
    public decimal? UnitPrice { get; private set; }

    public bool Archived { get; private set; }

    public bool IsMetered => string.Equals(Kind, "metered_component", System.StringComparison.OrdinalIgnoreCase);
}
