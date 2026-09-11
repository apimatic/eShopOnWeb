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

    public SubscriptionDto Subscription { get; set; } = new();
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? NextBillingDate { get; set; }
    public string? ActivatedAt { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string? CanceledAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}
