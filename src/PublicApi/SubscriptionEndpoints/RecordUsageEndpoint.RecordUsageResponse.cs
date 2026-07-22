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

    public UsageDto Usage { get; set; }

    /// <summary>
    /// Units accrued so far this billing period, or null when the provider could not be read back. The
    /// usage itself is recorded either way.
    /// </summary>
    public decimal? PeriodToDateTotal { get; set; }

    public bool IsPeriodToDateTotalAvailable { get; set; }
}
