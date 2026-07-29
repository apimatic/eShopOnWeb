using System.Globalization;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Projected from a Maxio product that
/// belongs to the configured product family. The <see cref="Handle"/> is the stable,
/// human-readable key used to subscribe (numeric ids are not stable across re-seeds).
/// </summary>
public sealed class SubscriptionPlan
{
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price in the site's default currency, expressed in cents.</summary>
    public int PriceInCents { get; init; }

    /// <summary>Numeric part of the billing period, e.g. the <c>1</c> in "every 1 month".</summary>
    public int Interval { get; init; }

    /// <summary>Unit of the billing period, e.g. <c>month</c> or <c>day</c>.</summary>
    public required string IntervalUnit { get; init; }

    public required string ProductFamilyHandle { get; init; }

    /// <summary>Human-friendly recurring price, e.g. <c>$299.00 / month</c> (assumes the site's default currency).</summary>
    public string FormattedPrice =>
        $"{(PriceInCents / 100m).ToString("C2", CultureInfo.GetCultureInfo("en-US"))} / {(Interval == 1 ? IntervalUnit : $"{Interval} {IntervalUnit}s")}";
}
