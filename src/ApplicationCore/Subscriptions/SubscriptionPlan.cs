namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring billing plan a shopper can subscribe to, projected from the Maxio
/// billing system. This is a read model owned by the application; it deliberately
/// exposes none of the underlying SDK types.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human-readable identifier used to subscribe (e.g. <c>eshop-pro</c>).</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>Display name of the plan.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional marketing description.</summary>
    public string? Description { get; init; }

    /// <summary>Recurring price, in the currency's minor units (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>ISO currency code (e.g. <c>USD</c>).</summary>
    public string Currency { get; init; } = "USD";

    /// <summary>Length of a billing period, expressed in <see cref="IntervalUnit"/>s.</summary>
    public int Interval { get; init; }

    /// <summary>Unit the <see cref="Interval"/> is measured in (e.g. <c>month</c>, <c>day</c>).</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>Handle of the product family this plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }
}
