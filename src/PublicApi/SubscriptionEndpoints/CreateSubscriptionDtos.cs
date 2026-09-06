using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest
{
    public string PlanHandle { get; set; } = "";
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = "";
    public int CustomerId { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public DateTime CurrentPeriodStartsAt { get; set; }
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime NextAssessmentAt { get; set; }
    public decimal ProductPricePerMonth { get; set; }
    public string ProductName { get; set; } = "";
    public string ProductHandle { get; set; } = "";
}

public class CreateSubscriptionResponse
{
    public SubscriptionDto Subscription { get; set; } = new();
}

public class ListSubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
