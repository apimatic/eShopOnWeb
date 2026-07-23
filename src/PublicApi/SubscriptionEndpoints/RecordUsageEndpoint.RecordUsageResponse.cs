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

    public string ComponentHandle { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>
    /// The running period-to-date total, or <c>null</c> when it could not be read back. The usage
    /// is recorded either way.
    /// </summary>
    public decimal? PeriodToDateTotal { get; set; }

    public string Message => "This usage will appear on your next renewal invoice.";
}
