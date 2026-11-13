using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    /// <summary>Billing interval length, e.g. 1 (with <see cref="IntervalUnit"/> "month").</summary>
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }

    public static SubscriptionPlanDto From(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.PriceInCents / 100m,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    /// <summary>Maxio subscription state, e.g. "active", "past_due", "canceled".</summary>
    public string State { get; set; } = string.Empty;
    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    /// <summary>When the next charge is due (next assessment; falls back to the end of the current period).</summary>
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }

    public static SubscriptionDto From(SubscriptionDetails details) => new()
    {
        Id = details.Id,
        Reference = details.Reference,
        PlanHandle = details.PlanHandle,
        PlanName = details.PlanName,
        State = details.State,
        PriceInCents = details.PriceInCents,
        Price = details.PriceInCents / 100m,
        Currency = details.Currency,
        CurrentPeriodEndsAt = details.CurrentPeriodEndsAt,
        NextBillingAt = details.NextBillingAt,
        CreatedAt = details.CreatedAt,
        ActivatedAt = details.ActivatedAt,
        CanceledAt = details.CanceledAt
    };
}

public class PendingSubscriptionDto
{
    public string PlanHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    /// <summary>"processing" (request in flight) or "pending_confirmation" (outcome being reconciled with Maxio).</summary>
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }

    public static PendingSubscriptionDto From(PendingSubscription pending) => new()
    {
        PlanHandle = pending.PlanHandle,
        SubscriptionReference = pending.SubscriptionReference,
        Status = pending.Status,
        RequestedAt = pending.RequestedAt
    };
}
