using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateResponse : BaseResponse
{
    public SubscriptionCreateResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionCreateResponse()
    {
    }

    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
