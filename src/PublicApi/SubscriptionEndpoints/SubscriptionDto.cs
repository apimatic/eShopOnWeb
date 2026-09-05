using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal? MonthlyPrice { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime ActivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal? MonthlyPrice { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public string Message { get; set; } = "Subscription created successfully";
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = [];
}

public class ListMySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = [];
}
