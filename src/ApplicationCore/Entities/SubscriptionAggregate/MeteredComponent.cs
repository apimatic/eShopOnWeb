namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A billable component defined on the product family. UC2 requires the configured component to be
/// of metered kind before any usage is recorded; <see cref="IsMetered"/> carries that verdict.
/// </summary>
public class MeteredComponent
{
    public MeteredComponent(int id,
        string? handle,
        string? name,
        string? kind,
        string? pricingScheme,
        long? pricePerUnitInCents,
        string? unitName)
    {
        Id = id;
        Handle = handle;
        Name = name;
        Kind = kind;
        PricingScheme = pricingScheme;
        PricePerUnitInCents = pricePerUnitInCents;
        UnitName = unitName;
    }

    public int Id { get; }

    public string? Handle { get; }

    public string? Name { get; }

    /// <summary>The provider's component kind, e.g. <c>metered_component</c>.</summary>
    public string? Kind { get; }

    public string? PricingScheme { get; }

    /// <summary>Unit price in minor units (cents). $0.01 per unit is <c>1</c>.</summary>
    public long? PricePerUnitInCents { get; }

    public string? UnitName { get; }

    /// <summary>True only when the provider reports this component as metered.</summary>
    public bool IsMetered => string.Equals(Kind, MeteredKind, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Unit price as a currency amount, or <c>null</c> when the provider did not report one.</summary>
    public decimal? PricePerUnit => PricePerUnitInCents.HasValue ? PricePerUnitInCents.Value / 100m : null;

    /// <summary>The provider's wire value identifying a metered component.</summary>
    public const string MeteredKind = "metered_component";
}
