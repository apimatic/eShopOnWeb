using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The outcome of recording pay-as-you-go usage.
/// </summary>
/// <remarks>
/// The recorded usage always stands. <see cref="IsTotalAvailable"/> is false when the running
/// period-to-date total could not be read back — the usage was still accepted, so the caller must
/// not resend it.
/// </remarks>
public class UsageReportDto
{
    public long UsageId { get; set; }
    public int Quantity { get; set; }
    public string Memo { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }

    public bool IsTotalAvailable { get; set; }
    public int? PeriodToDateQuantity { get; set; }

    /// <summary>Period-to-date charge in whole currency units.</summary>
    public decimal? PeriodToDateCharge { get; set; }

    public string TotalUnavailableReason { get; set; }
}
