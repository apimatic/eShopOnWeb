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

    public long UsageId { get; set; }
    public int SubscriptionId { get; set; }
    public string? ComponentHandle { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }

    /// <summary>Units accrued so far this period, or null when the read-back was unavailable.</summary>
    public decimal? PeriodToDateTotal { get; set; }
}
