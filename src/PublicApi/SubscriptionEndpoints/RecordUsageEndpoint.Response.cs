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

    /// <summary>The quantity the provider accepted.</summary>
    public decimal RecordedQuantity { get; set; }

    /// <summary>The running period-to-date total, or <c>null</c> when it could not be read back.</summary>
    public UsageDto? Usage { get; set; }

    /// <summary>False when the usage was recorded but the running total was unavailable.</summary>
    public bool IsTotalAvailable { get; set; }

    /// <summary>What the customer is told about when this usage will be billed.</summary>
    public string Message { get; set; } = "The recorded usage will appear on your next renewal invoice.";
}
