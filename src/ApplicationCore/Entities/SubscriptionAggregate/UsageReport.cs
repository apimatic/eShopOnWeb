namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of recording pay-as-you-go usage: the accepted event plus the running period-to-date
/// total read back from the provider (plan.md UC2, steps 2–4).
/// </summary>
public sealed record UsageReport
{
    /// <summary>
    /// The usage event that was just accepted, or <c>null</c> when this report is a read-only summary of
    /// the current period rather than the outcome of a write.
    /// </summary>
    public UsageRecord? Record { get; init; }

    /// <summary>The pay-as-you-go component the totals belong to.</summary>
    public required string ComponentHandle { get; init; }

    /// <summary>The subscription the totals belong to.</summary>
    public required int SubscriptionId { get; init; }

    /// <summary>
    /// Units accrued so far in the current billing period, or <c>null</c> when the provider could not be
    /// read back. UC2 is explicit that a failed read-back must not fail the whole operation — the usage
    /// stands and the total is reported as unavailable.
    /// </summary>
    public decimal? PeriodToDateUnits { get; init; }

    /// <summary>Price of a single unit in minor units (cents), when known.</summary>
    public long? UnitPriceInCents { get; init; }

    /// <summary>True when the period-to-date total could be read back from the provider.</summary>
    public bool PeriodToDateAvailable => PeriodToDateUnits is not null;

    /// <summary>
    /// Estimated pay-as-you-go charge accruing to the next renewal invoice, in major units (dollars).
    /// </summary>
    public decimal? EstimatedPeriodToDateCharge => PeriodToDateUnits is null || UnitPriceInCents is null
        ? null
        : PeriodToDateUnits.Value * (UnitPriceInCents.Value / 100m);
}
