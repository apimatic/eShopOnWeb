using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The pay-as-you-go component defined on the product family, as the provider reports it.
/// The integration refuses to record usage unless <see cref="IsMetered"/> is true (UC2).
/// </summary>
public class MeteredComponentDefinition
{
    public MeteredComponentDefinition(int id,
        string handle,
        string name,
        string kind,
        bool isMetered,
        string? unitName,
        decimal? unitPrice,
        string? pricingScheme,
        int? productFamilyId,
        string? productFamilyHandle)
    {
        Guard.Against.NullOrWhiteSpace(handle, nameof(handle));
        Guard.Against.NullOrWhiteSpace(name, nameof(name));

        Id = id;
        Handle = handle;
        Name = name;
        Kind = kind;
        IsMetered = isMetered;
        UnitName = unitName;
        UnitPrice = unitPrice;
        PricingScheme = pricingScheme;
        ProductFamilyId = productFamilyId;
        ProductFamilyHandle = productFamilyHandle;
    }

    public int Id { get; }

    public string Handle { get; }

    public string Name { get; }

    /// <summary>The provider's component kind, verbatim (e.g. <c>metered_component</c>).</summary>
    public string Kind { get; }

    public bool IsMetered { get; }

    public string? UnitName { get; }

    /// <summary>Price per unit in major currency units (e.g. 0.01).</summary>
    public decimal? UnitPrice { get; }

    public string? PricingScheme { get; }

    public int? ProductFamilyId { get; }

    public string? ProductFamilyHandle { get; }
}
