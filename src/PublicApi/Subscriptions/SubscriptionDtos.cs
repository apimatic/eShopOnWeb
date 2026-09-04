using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }
    public bool Taxable { get; set; }
}

public sealed class SubscriptionPlansResponse
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; set; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed class MySubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
}

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<MySubscriptionDto> Subscriptions { get; set; } = Array.Empty<MySubscriptionDto>();
}
