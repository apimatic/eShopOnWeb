using System;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal PriceMonthly { get; set; }
    public DateTime? NextBillingDate { get; set; }
}
