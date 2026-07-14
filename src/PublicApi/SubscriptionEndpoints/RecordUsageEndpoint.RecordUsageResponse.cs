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
    public int RecordedQuantity { get; set; }

    /// <summary>Null if the period-to-date read-back failed after a successfully recorded usage
    /// (UC2 failure scenario: report success with the total marked unavailable).</summary>
    public int? PeriodToDateUnitBalance { get; set; }
}
