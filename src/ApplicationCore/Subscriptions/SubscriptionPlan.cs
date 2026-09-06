using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// Projected from the billing provider's catalog - the provider stays the system of record,
/// eShopOnWeb never stores plan pricing of its own.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>
    /// Stable, human readable identifier of the plan in the billing provider (for example <c>eshop-pro</c>).
    /// This is the value callers pass to subscribe; provider numeric ids are deliberately not part of the contract.
    /// </summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price of the plan in the smallest currency unit.</summary>
    public long PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    /// <summary>Number of <see cref="IntervalUnit"/>s in a billing period (for example 1 with "month").</summary>
    public int Interval { get; init; }

    /// <summary>Billing period unit - "month" or "day".</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>
    /// True when the provider requires a stored payment method before a subscription can be created.
    /// This integration does not capture card details, so such plans cannot be subscribed to here.
    /// </summary>
    public bool RequiresPaymentMethod { get; init; }

    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }

    public long? TrialPriceInCents { get; init; }

    public bool Taxable { get; init; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }

    /// <summary>
    /// Provider-assigned numeric id. Exposed for support/diagnostics only; it is not stable across
    /// catalog re-seeds, so never persist or address plans by it.
    /// </summary>
    public int ProviderProductId { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}
