using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Projected from the billing provider's
/// product catalog; eShopOnWeb never stores plans locally.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human-readable identifier of the plan (e.g. <c>eshop-pro</c>).</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>Provider-assigned numeric id. Not stable across catalog re-seeds; never persist it.</summary>
    public long ProviderPlanId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (cents for USD).</summary>
    public long PriceInCents { get; init; }

    /// <summary>ISO 4217 currency code of the billing site.</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Unit of the billing period, as reported by the provider (e.g. <c>month</c>).</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>True when the provider will refuse the signup unless a payment method is captured first.</summary>
    public bool RequiresPaymentMethod { get; init; }

    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>Recurring price as a decimal amount (e.g. 299.00).</summary>
    public decimal Price => PriceInCents / 100m;
}
