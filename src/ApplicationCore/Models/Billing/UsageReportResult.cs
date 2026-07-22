namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// The outcome of reporting metered usage (UC2): what was recorded, plus the running
/// period-to-date total when it could be read back.
/// </summary>
public class UsageReportResult
{
    public int SubscriptionId { get; set; }
    public string ComponentHandle { get; set; } = string.Empty;
    public long UsageRecordId { get; set; }
    public decimal QuantityRecorded { get; set; }
    public string? Memo { get; set; }

    /// <summary>
    /// The billable unit balance accrued so far this period, or null when the read-back failed.
    /// The usage itself still stands in that case.
    /// </summary>
    public decimal? PeriodToDateUnits { get; set; }

    /// <summary>
    /// The period-to-date units priced in the site currency, or null when the total is unavailable.
    /// </summary>
    public decimal? PeriodToDateAmount { get; set; }

    public bool PeriodToDateAvailable => PeriodToDateUnits.HasValue;
}
