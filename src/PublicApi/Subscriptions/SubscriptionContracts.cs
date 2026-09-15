using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class CreateSubscriptionRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public long PriceInCents { get; set; }

    public decimal Price { get; set; }

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;
}

public class SubscriptionDto
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public long PriceInCents { get; set; }

    public decimal Price { get; set; }

    public string State { get; set; } = string.Empty;

    public DateTimeOffset? NextBillingAt { get; set; }
}

public class SubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class MySubscriptionsResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
