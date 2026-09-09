using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// A subscribable plan (a Maxio Advanced Billing product) offered by the shop.
/// </summary>
public sealed class SubscriptionPlanInfo
{
    /// <summary>
    /// Stable API handle of the underlying Maxio product. Use this to subscribe.
    /// </summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price per billing period, in cents.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price per billing period as a decimal amount (e.g. 299.00).</summary>
    public decimal Price => PriceInCents / 100m;

    public int Interval { get; init; }

    /// <summary>Billing interval unit as defined by Maxio (e.g. "month", "day").</summary>
    public string IntervalUnit { get; init; } = "month";

    /// <summary>Handle of the product family (plan catalog) the plan belongs to.</summary>
    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>True when Maxio requires a payment method at signup.</summary>
    public bool PaymentMethodRequired { get; init; }
}
