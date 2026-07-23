namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The running period-to-date usage balance for one metered component on one subscription.
/// </summary>
public class ComponentUsageSummary
{
    public ComponentUsageSummary(int? componentId,
        string? componentHandle,
        string? name,
        int unitBalance,
        long? pricePerUnitInCents)
    {
        ComponentId = componentId;
        ComponentHandle = componentHandle;
        Name = name;
        UnitBalance = unitBalance;
        PricePerUnitInCents = pricePerUnitInCents;
    }

    public int? ComponentId { get; }

    public string? ComponentHandle { get; }

    public string? Name { get; }

    /// <summary>
    /// Units accrued so far this billing period. This is a <b>unit count</b>, not money.
    /// </summary>
    public int UnitBalance { get; }

    /// <summary>Unit price in minor units (cents).</summary>
    public long? PricePerUnitInCents { get; }

    /// <summary>
    /// What the accrued units will add to the next renewal invoice, in minor units (cents), or
    /// <c>null</c> when the provider did not report a unit price.
    /// </summary>
    public long? EstimatedChargeInCents => PricePerUnitInCents.HasValue
        ? PricePerUnitInCents.Value * UnitBalance
        : null;

    /// <summary>The estimated charge as a currency amount.</summary>
    public decimal? EstimatedCharge => EstimatedChargeInCents.HasValue
        ? EstimatedChargeInCents.Value / 100m
        : null;
}
