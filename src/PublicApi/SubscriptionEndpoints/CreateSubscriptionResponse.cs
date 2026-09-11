using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public string Message { get; set; } = string.Empty;
}
