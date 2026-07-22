using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The outcome of recording metered usage.
/// <para>
/// <see cref="IsPeriodTotalAvailable"/> is false when the usage was accepted but the running total
/// could not be read back. The usage still stands and will still be billed; only the total is
/// missing.
/// </para>
/// </summary>
public class UsageSummaryDto
{
    public long UsageId { get; set; }
    public int SubscriptionId { get; set; }
    public decimal Quantity { get; set; }
    public string Memo { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }
    public bool IsPeriodTotalAvailable { get; set; }
    public decimal? PeriodToDateQuantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? PeriodToDateAmount { get; set; }
    public DateTimeOffset? PeriodStartedAt { get; set; }
    public DateTimeOffset? PeriodEndsAt { get; set; }
}
