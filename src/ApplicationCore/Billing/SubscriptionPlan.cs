namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A subscribable plan, projected from a Maxio product (a product version inside a product family).
/// Prices are carried in the minor unit (cents) exactly as the Maxio API reports them.
/// </summary>
public record SubscriptionPlan
{
    public long Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }

    /// <summary>Recurring price in cents (Maxio <c>price_in_cents</c>).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/> units between renewals.</summary>
    public int Interval { get; init; }

    /// <summary>"month" or "day" (Maxio <c>interval_unit</c>).</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>True when a payment profile must be captured before enrolling (Maxio <c>require_credit_card</c>).</summary>
    public bool RequiresPaymentMethod { get; init; }

    public bool Taxable { get; init; }

    public bool Archived { get; init; }

    public decimal Price => PriceInCents / 100m;

    /// <summary>Human readable billing cadence, e.g. "1 month".</summary>
    public string BillingPeriod => Interval > 0 ? $"{Interval} {(Interval == 1 ? IntervalUnit : IntervalUnit + "s")}" : "unspecified";
}
