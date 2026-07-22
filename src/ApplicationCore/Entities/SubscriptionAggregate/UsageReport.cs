namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The outcome of reporting usage: the accepted record plus the running period-to-date figures.
/// The read back of the running total is best effort — when it fails the usage still stands and
/// <see cref="PeriodToDateAvailable"/> is <c>false</c>.
/// </summary>
/// <param name="UnitPrice">Price of a single unit in major currency units.</param>
/// <param name="EstimatedPeriodToDateAmount">
/// <see cref="PeriodToDateQuantity"/> × <see cref="UnitPrice"/> in major currency units; the amount
/// that will accrue to the next renewal invoice. Null when the running total is unavailable.
/// </param>
public sealed record UsageReport(
    UsageRecord Record,
    decimal? PeriodToDateQuantity,
    decimal UnitPrice,
    decimal? EstimatedPeriodToDateAmount,
    bool PeriodToDateAvailable);
