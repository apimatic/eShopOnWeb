using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The outcome of a pay-as-you-go usage report, plus the running period-to-date total.
/// </summary>
public class UsageReportDto
{
    public int SubscriptionId { get; set; }

    public string ComponentHandle { get; set; } = string.Empty;

    /// <summary>Provider-assigned id of the usage event, when this report followed a write.</summary>
    public long? UsageId { get; set; }

    /// <summary>Units recorded by this report, when this report followed a write.</summary>
    public decimal? RecordedQuantity { get; set; }

    public string? Memo { get; set; }

    public DateTimeOffset? RecordedAt { get; set; }

    /// <summary>
    /// Units accrued so far this billing period, or <c>null</c> when the provider could not be read back.
    /// A <c>null</c> total means "unavailable", never zero.
    /// </summary>
    public decimal? PeriodToDateUnits { get; set; }

    /// <summary>Price of one unit in cents.</summary>
    public long? UnitPriceInCents { get; set; }

    /// <summary>Estimated pay-as-you-go charge accruing to the next renewal invoice, in dollars.</summary>
    public decimal? EstimatedPeriodToDateCharge { get; set; }
}
