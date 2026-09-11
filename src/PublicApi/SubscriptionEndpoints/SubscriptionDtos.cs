using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public int Interval { get; set; }
    public bool RequireCreditCard { get; set; }
    public bool Taxable { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string ProductFamilyName { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int? CustomerId { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CanceledAt { get; set; }
    public string? CreatedAt { get; set; }
}

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionResponse
{
    public SubscriptionDto? Subscription { get; set; }
    public string? ErrorMessage { get; set; }
}

public class MySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
