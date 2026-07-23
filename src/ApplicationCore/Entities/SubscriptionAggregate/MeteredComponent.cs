namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A component defined on the product family. The integration only records usage against components
/// whose <see cref="IsMetered"/> is true — that check is the UC2 precondition (plan.md UC2).
/// </summary>
public class MeteredComponent
{
    public MeteredComponent(int id, string handle, string name, string kind, bool isMetered,
        decimal? unitPrice, string? pricingScheme)
    {
        Id = id;
        Handle = handle;
        Name = name;
        Kind = kind;
        IsMetered = isMetered;
        UnitPrice = unitPrice;
        PricingScheme = pricingScheme;
    }

    public int Id { get; }

    public string Handle { get; }

    public string Name { get; }

    /// <summary>The provider's own component-kind string, kept verbatim for diagnostics.</summary>
    public string Kind { get; }

    /// <summary>True only when <see cref="Kind"/> is the provider's metered kind.</summary>
    public bool IsMetered { get; }

    /// <summary>Price per unit in dollars, when the component uses a flat per-unit scheme.</summary>
    public decimal? UnitPrice { get; }

    public string? PricingScheme { get; }
}
