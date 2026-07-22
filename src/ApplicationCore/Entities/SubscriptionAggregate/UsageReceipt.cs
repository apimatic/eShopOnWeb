namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of reporting usage: the accepted event plus the running period-to-date balance.
/// </summary>
/// <remarks>
/// Reading the running total back is a best-effort follow-up. If that read fails the usage still
/// stands, so <see cref="PeriodToDateUnits"/> is null and <see cref="PeriodToDateAvailable"/> is
/// false rather than the whole operation failing (UC2).
/// </remarks>
public sealed record UsageReceipt
{
    public required UsageRecord Recorded { get; init; }

    /// <summary>The accumulated unit balance for the current billing period, when it could be read.</summary>
    public int? PeriodToDateUnits { get; init; }

    /// <summary>False when the running total could not be read back after a successful record.</summary>
    public bool PeriodToDateAvailable => PeriodToDateUnits.HasValue;
}
