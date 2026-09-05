using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse() { }
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }

    public SubscriptionDetailsDto? Subscription { get; set; }
}

public class SubscriptionDetailsDto
{
    public string Id { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
}
