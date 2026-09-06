namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper may subscribe to.
/// </summary>
public class SubscriptionPlan
{
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Recurring price in the minor unit of the site's currency (e.g. cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price expressed in the major unit (e.g. dollars).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals (e.g. 1 with "month").</summary>
    public int Interval { get; init; }

    public string? IntervalUnit { get; init; }

    /// <summary>Human readable billing cadence, e.g. "$299.00 / month".</summary>
    public string BillingPeriod => Interval == 1
        ? IntervalUnit ?? string.Empty
        : $"{Interval} {IntervalUnit}s";

    public string? PricePointName { get; init; }
    public string? PricePointHandle { get; init; }

    public bool RequiresPaymentMethod { get; init; }
    public bool Taxable { get; init; }

    public int? TrialInterval { get; init; }
    public string? TrialIntervalUnit { get; init; }
    public long? TrialPriceInCents { get; init; }
    public long? SetupFeeInCents { get; init; }

    public string? ProductFamilyHandle { get; init; }
    public string? ProductFamilyName { get; init; }

    /// <summary>Billing system identifier. Not stable across catalog re-seeds - prefer <see cref="Handle"/>.</summary>
    public long ProviderPlanId { get; init; }
}
