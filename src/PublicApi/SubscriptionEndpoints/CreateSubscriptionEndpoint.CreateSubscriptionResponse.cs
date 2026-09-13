using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse() { }
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public SubscriptionDto? Subscription { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? NextBillingDate { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CanceledAt { get; set; }
}
