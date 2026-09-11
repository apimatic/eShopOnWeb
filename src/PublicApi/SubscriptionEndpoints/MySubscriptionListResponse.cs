using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionListResponse
{
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
}

public class MySubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string PlanName { get; set; } = "";
    public string PlanHandle { get; set; } = "";
    public string Price { get; set; } = "";
    public string CurrentPeriodEndsAt { get; set; } = "";
    public string NextAssessmentAt { get; set; } = "";
}
