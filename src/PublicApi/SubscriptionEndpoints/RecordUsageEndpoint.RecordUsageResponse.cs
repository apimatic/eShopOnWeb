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

    public int UsageId { get; set; }
    public int SubscriptionId { get; set; }
    public string? ComponentHandle { get; set; }
    public decimal Quantity { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset? RecordedAt { get; set; }

    /// <summary>The running period-to-date total, or null when it could not be read back.</summary>
    public decimal? PeriodToDateTotal { get; set; }

    /// <summary>Explains when the recorded usage will be charged.</summary>
    public string Message { get; set; } = "The recorded usage will appear on the next renewal invoice.";
}
