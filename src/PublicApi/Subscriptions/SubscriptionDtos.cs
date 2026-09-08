using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class SubscriptionPlanDto
{
    public long Id { get; set; }

    public string Handle { get; set; }

    public string Name { get; set; }

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public int Interval { get; set; } = 1;

    public string IntervalUnit { get; set; } = "month";

    public bool RequireCreditCard { get; set; }

    public bool Taxable { get; set; }
}

public class SubscriptionDto
{
    public long Id { get; set; }

    public string State { get; set; }

    public string ProductHandle { get; set; }

    public string ProductName { get; set; }

    public decimal Price { get; set; }

    public string? Currency { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? NextAssessmentAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public string? Reference { get; set; }
}

public class SubscribeResult
{
    public SubscriptionDto Subscription { get; set; }

    public bool IsNew { get; set; }
}
