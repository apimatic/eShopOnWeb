using System;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    public MySubscriptionsRequest() {}
}

public class MySubscriptionsResponse : BaseResponse
{
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
    public MySubscriptionsResponse(Guid correlationId)  { }
}

public class MySubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string ProductHandle { get; set; } = "";
    public decimal Price { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? ActivatedAt { get; set; }
}
