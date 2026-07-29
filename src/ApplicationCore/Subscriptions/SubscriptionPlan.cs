namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, projected from the billing system of record.
/// </summary>
public record SubscriptionPlan
{
    /// <summary>Stable API handle of the plan (e.g. <c>eshop-pro</c>). Used as the subscribe target.</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Numeric billing interval (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Billing interval unit, e.g. <c>month</c> or <c>day</c>.</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>Handle of the product family this plan belongs to.</summary>
    public string ProductFamilyHandle { get; init; } = string.Empty;
}
