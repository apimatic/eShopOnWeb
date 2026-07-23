using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A single accepted usage report against a metered component (UC2).
/// </summary>
/// <param name="Id">The provider-assigned usage record identifier.</param>
/// <param name="SubscriptionId">The subscription the usage was billed to.</param>
/// <param name="ComponentId">The metered component the usage was recorded against.</param>
/// <param name="Quantity">The number of units recorded.</param>
/// <param name="Memo">The free-text note stored alongside the record, if any.</param>
/// <param name="RecordedAt">When the provider accepted the record.</param>
public record UsageRecord(
    long Id,
    int SubscriptionId,
    int ComponentId,
    decimal Quantity,
    string? Memo,
    DateTimeOffset? RecordedAt);

/// <summary>
/// The outcome of a usage report: the accepted record plus the running period-to-date total.
/// </summary>
/// <param name="Record">The usage record the provider accepted.</param>
/// <param name="PeriodToDateQuantity">
/// Total units recorded so far in the current billing period, or <see langword="null"/> when the
/// read-back failed. A failed read-back never fails the whole operation — the usage stands.
/// </param>
/// <param name="UnitPriceInCents">Price per unit at the time of reporting, in cents.</param>
public record UsageReport(
    UsageRecord Record,
    decimal? PeriodToDateQuantity,
    long UnitPriceInCents)
{
    /// <summary>Whether the running total could be read back from the provider.</summary>
    public bool PeriodToDateAvailable => PeriodToDateQuantity.HasValue;

    /// <summary>
    /// The accrued pay-as-you-go charge for the current period in the site's currency unit,
    /// or <see langword="null"/> when the running total is unavailable.
    /// </summary>
    public decimal? PeriodToDateCharge =>
        PeriodToDateQuantity.HasValue ? PeriodToDateQuantity.Value * (UnitPriceInCents / 100m) : null;
}
