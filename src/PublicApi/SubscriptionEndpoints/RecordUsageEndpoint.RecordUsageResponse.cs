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

    /// <summary>
    /// Units accrued this period, which will be billed on the next renewal invoice.
    /// </summary>
    public decimal? PeriodToDateBalance { get; set; }

    /// <summary>
    /// True when the usage was recorded but the running total could not be read back.
    /// </summary>
    public bool BalanceUnavailable { get; set; }
}
