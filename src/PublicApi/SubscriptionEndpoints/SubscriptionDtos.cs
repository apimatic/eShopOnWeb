using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscribable plan surfaced by <c>GET /api/subscription-plans</c>.</summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }
    public bool Taxable { get; set; }
}

/// <summary>A subscription surfaced by the subscription endpoints.</summary>
public class SubscriptionDto
{
    public long Id { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
    public string? PaymentCollectionMethod { get; set; }
}

/// <summary>Shared mapping between Maxio models and the PublicApi DTOs.</summary>
internal static class SubscriptionDtoMapper
{
    public static SubscriptionPlanDto ToPlanDto(MaxioPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = CentsToAmount(plan.PriceInCents),
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresCreditCard,
        Taxable = plan.Taxable
    };

    public static SubscriptionDto ToSubscriptionDto(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = CentsToAmount(subscription.PriceInCents),
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        Currency = subscription.Currency,
        State = subscription.State,
        NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod
    };

    public static List<SubscriptionPlanDto> ToPlanDtos(IEnumerable<MaxioPlan> plans)
    {
        var result = new List<SubscriptionPlanDto>();
        foreach (var plan in plans)
        {
            result.Add(ToPlanDto(plan));
        }

        return result;
    }

    public static List<SubscriptionDto> ToSubscriptionDtos(IEnumerable<MaxioSubscription> subscriptions)
    {
        var result = new List<SubscriptionDto>();
        foreach (var subscription in subscriptions)
        {
            result.Add(ToSubscriptionDto(subscription));
        }

        return result;
    }

    private static decimal CentsToAmount(long cents) => cents / 100m;
}
