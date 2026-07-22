using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A billable component attached to a product family — the pay-as-you-go add-on behind UC2.
/// </summary>
public class MeteredComponent
{
    public MeteredComponent(int id,
        string handle,
        string name,
        BillingComponentKind kind,
        string? pricingScheme,
        decimal? unitPrice,
        string? unitName,
        int productFamilyId)
    {
        Guard.Against.NullOrEmpty(handle, nameof(handle));

        Id = id;
        Handle = handle;
        Name = name;
        Kind = kind;
        PricingScheme = pricingScheme;
        UnitPrice = unitPrice;
        UnitName = unitName;
        ProductFamilyId = productFamilyId;
    }

    public int Id { get; }

    /// <summary>The durable identifier for this component (e.g. <c>api-call</c>).</summary>
    public string Handle { get; }

    public string Name { get; }

    public BillingComponentKind Kind { get; }

    /// <summary>The provider's pricing scheme (e.g. <c>per_unit</c>).</summary>
    public string? PricingScheme { get; }

    /// <summary>Price per unit in major units (dollars), e.g. 0.01 for a cent per call.</summary>
    public decimal? UnitPrice { get; }

    public string? UnitName { get; }

    public int ProductFamilyId { get; }

    /// <summary>Usage may only be recorded against metered components (UC2 precondition).</summary>
    public bool IsMetered => Kind == BillingComponentKind.Metered;
}
