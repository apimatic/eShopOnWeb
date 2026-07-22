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

    public UsageRecordDto? Usage { get; set; }

    /// <summary>Units accrued in the current billing period, or null when the read back failed.</summary>
    public decimal? PeriodToDateQuantity { get; set; }

    /// <summary>Price of one unit in major currency units.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// What the period-to-date usage will add to the next renewal invoice, in major currency units.
    /// </summary>
    public decimal? EstimatedPeriodToDateAmount { get; set; }

    /// <summary>Whether the running total could be read back. The usage is recorded either way.</summary>
    public bool PeriodToDateAvailable { get; set; }

    public string Message { get; set; } = string.Empty;
}
