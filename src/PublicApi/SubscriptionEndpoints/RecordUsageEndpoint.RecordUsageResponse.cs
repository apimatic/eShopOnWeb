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
    public string ComponentHandle { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }

    /// <summary>The running period-to-date total; null when the read-back was unavailable.</summary>
    public decimal? PeriodToDateTotal { get; set; }

    public bool IsPeriodToDateTotalAvailable { get; set; }

    /// <summary>These units bill on the next renewal invoice.</summary>
    public string BillingNote => "Recorded usage will appear on your next renewal invoice.";
}
