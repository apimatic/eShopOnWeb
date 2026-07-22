using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageResponse : BaseResponse
{
    public RecordUsageResponse(Guid correlationId) : base(correlationId) { }

    public RecordUsageResponse() { }

    public int SubscriptionId { get; set; }
    public string? ComponentHandle { get; set; }
    public decimal RecordedQuantity { get; set; }
    public string? Memo { get; set; }

    /// <summary>Units accrued this period, or <c>null</c> when the read-back was unavailable.</summary>
    public decimal? PeriodToDateTotal { get; set; }

    public decimal? UnitPrice { get; set; }

    public decimal? EstimatedPeriodToDateCharge { get; set; }
}
