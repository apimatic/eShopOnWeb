using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public CreateSubscriptionResponse() { }

    public int? SubscriptionId { get; set; }
    public string? PlanName { get; set; }
    public string? PlanHandle { get; set; }
    public decimal? Price { get; set; }
    public string? State { get; set; }
    public DateTime? NextBillingDate { get; set; }
}
