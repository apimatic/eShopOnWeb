using System;
using Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public int? BillingInterval { get; set; }
    public string? BillingIntervalUnit { get; set; }
    public bool? RequiresPaymentMethod { get; set; }

    public static SubscriptionPlanDto From(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.PriceInCents / 100m,
        PriceInCents = plan.PriceInCents,
        BillingInterval = plan.Interval,
        BillingIntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequireCreditCard
    };
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public int? BillingInterval { get; set; }
    public string? BillingIntervalUnit { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }

    public static SubscriptionDto From(SubscriptionDetails details) => new()
    {
        Id = details.SubscriptionId,
        Reference = details.Reference,
        PlanHandle = details.PlanHandle,
        PlanName = details.PlanName,
        Price = details.PriceInCents / 100m,
        Currency = details.Currency,
        BillingInterval = details.Interval,
        BillingIntervalUnit = details.IntervalUnit,
        State = details.State,
        NextBillingDate = details.NextBillingAt,
        NextAssessmentAt = details.NextAssessmentAt,
        CreatedAt = details.CreatedAt,
        ActivatedAt = details.ActivatedAt
    };
}
