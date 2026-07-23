using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A pay-as-you-go component defined on the product family and therefore available to every
/// subscription within it. Usage may only be recorded against a component of metered kind.
/// </summary>
public class MeteredComponent
{
    /// <summary>The provider's kind discriminator for a metered (usage-reported) component.</summary>
    public const string MeteredKind = "metered_component";

    public MeteredComponent(int id, string? handle, string name, string kind, string? pricingScheme, decimal unitPrice, int productFamilyId)
    {
        Guard.Against.NullOrEmpty(name, nameof(name));
        Guard.Against.NullOrEmpty(kind, nameof(kind));

        Id = id;
        Handle = handle;
        Name = name;
        Kind = kind;
        PricingScheme = pricingScheme;
        UnitPrice = unitPrice;
        ProductFamilyId = productFamilyId;
    }

    public int Id { get; }
    public string? Handle { get; }
    public string Name { get; }

    /// <summary>The verbatim provider kind, e.g. "metered_component" or "quantity_based_component".</summary>
    public string Kind { get; }

    public string? PricingScheme { get; }

    /// <summary>The price of one unit in major units (the provider reports components in decimal currency, not minor units).</summary>
    public decimal UnitPrice { get; }

    public int ProductFamilyId { get; }

    public bool IsMetered => string.Equals(Kind, MeteredKind, System.StringComparison.OrdinalIgnoreCase);
}
