using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A usage-billed add-on offered on a product family. Unlike plan prices, the provider reports
/// component unit prices in major units (dollars), so <see cref="UnitPrice"/> is not a cents value.
/// </summary>
public class MeteredComponent
{
    public MeteredComponent(int id, string handle, string name, string kind, string? unitName,
        string? pricingScheme, decimal? unitPrice)
    {
        Id = id;
        Handle = handle;
        Name = name;
        Kind = kind;
        UnitName = unitName;
        PricingScheme = pricingScheme;
        UnitPrice = unitPrice;
    }

    public int Id { get; }
    public string Handle { get; }
    public string Name { get; }

    /// <summary>
    /// The provider's component kind. Usage can only be recorded against a metered component.
    /// </summary>
    public string Kind { get; }

    public string? UnitName { get; }
    public string? PricingScheme { get; }

    /// <summary>
    /// Price per unit in major units (dollars), e.g. 0.01 for a one-cent-per-call component.
    /// </summary>
    public decimal? UnitPrice { get; }

    /// <summary>
    /// True when this component bills by reported usage, which is what UC2 requires.
    /// </summary>
    public bool IsMetered => string.Equals(Kind, MeteredKind, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The provider's identifier for the metered component kind.
    /// </summary>
    public const string MeteredKind = "metered_component";
}
