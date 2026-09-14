using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Projected from the billing system's catalog;
/// eShopOnWeb never stores plan definitions of its own.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human readable identifier of the plan. This is what callers pass to subscribe.</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in the site's currency, expressed in minor units (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price rendered for display, e.g. "299.00".</summary>
    public string FormattedPrice => (PriceInCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Number of IntervalUnits in a billing period, e.g. 1 for monthly.</summary>
    public int Interval { get; init; }

    /// <summary>Unit of the billing period, e.g. "month" or "day".</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>True when the billing system requires a stored payment method before this plan can be subscribed to.</summary>
    public bool RequiresPaymentMethod { get; init; }

    /// <summary>Length of the trial, when the plan has one.</summary>
    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }

    public string ProductFamilyHandle { get; init; } = string.Empty;
}
