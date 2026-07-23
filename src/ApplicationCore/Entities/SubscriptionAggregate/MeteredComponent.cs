using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A usage-billed component living on a product family, and therefore available to every
/// subscription on any plan in that family.
/// </summary>
public class MeteredComponent
{
    public MeteredComponent(int id, string handle, string name, string kind, string? pricingScheme,
        decimal? unitPrice, int productFamilyId)
    {
        Guard.Against.NullOrEmpty(handle, nameof(handle));

        Id = id;
        Handle = handle;
        Name = name;
        Kind = kind;
        PricingScheme = pricingScheme;
        UnitPrice = unitPrice;
        ProductFamilyId = productFamilyId;
    }

    public int Id { get; private set; }

    /// <summary>The durable API handle of the component, e.g. <c>api-call</c>.</summary>
    public string Handle { get; private set; }

    public string Name { get; private set; }

    /// <summary>The provider's component kind. Usage can only be recorded against a metered kind.</summary>
    public string Kind { get; private set; }

    public string? PricingScheme { get; private set; }

    /// <summary>The price charged per unit in major units (e.g. 0.01 dollars), when priced per unit.</summary>
    public decimal? UnitPrice { get; private set; }

    public int ProductFamilyId { get; private set; }

    /// <summary>The provider's kind discriminator for a metered component.</summary>
    public const string METERED_KIND = "metered_component";

    public bool IsMetered => string.Equals(Kind, METERED_KIND, System.StringComparison.OrdinalIgnoreCase);
}
