using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageResponse : BaseResponse
{
    public RecordUsageResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RecordUsageResponse()
    {
    }

    public int SubscriptionId { get; set; }
    public string ComponentHandle { get; set; } = string.Empty;
    public long UsageRecordId { get; set; }
    public decimal QuantityRecorded { get; set; }
    public string? Memo { get; set; }

    /// <summary>The billable units accrued this period, or null when the total could not be read back.</summary>
    public decimal? PeriodToDateUnits { get; set; }

    /// <summary>The period-to-date units priced in the site currency, e.g. 1.25.</summary>
    public decimal? PeriodToDateAmount { get; set; }

    public bool PeriodToDateAvailable { get; set; }

    /// <summary>Reminds the caller that metered usage is invoiced at the next renewal, not now.</summary>
    public string Message { get; set; } = "Usage recorded; it will appear on the next renewal invoice.";
}
