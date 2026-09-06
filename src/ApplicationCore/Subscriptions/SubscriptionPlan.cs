namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, as published by the billing provider.
/// </summary>
public record SubscriptionPlan
{
    /// <summary>Stable, human readable identifier of the plan. This is what callers subscribe to.</summary>
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest unit of <see cref="Currency"/>.</summary>
    public required long PriceInCents { get; init; }

    /// <summary>ISO currency code of the billing site, when it could be determined.</summary>
    public string? Currency { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period (e.g. 1 month).</summary>
    public required int Interval { get; init; }

    /// <summary>Unit of the billing period, either <c>month</c> or <c>day</c>.</summary>
    public required string IntervalUnit { get; init; }

    /// <summary>True when the shopper must have a payment method on file before subscribing.</summary>
    public required bool RequiresPaymentMethod { get; init; }

    public string? ProductFamilyHandle { get; init; }

    /// <summary>Recurring price expressed in major currency units (e.g. 299.00).</summary>
    public decimal Price => PriceInCents / 100m;
}
