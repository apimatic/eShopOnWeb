using System;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan surfaced to shoppers. Maps to a Maxio product in the configured family.
/// </summary>
public class SubscriptionPlanDto
{
    public long ProductId { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
}

/// <summary>
/// A shopper's subscription as recorded in Maxio.
/// </summary>
public class SubscriptionDto
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public string PaymentCollectionMethod { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
}

internal static class SubscriptionDtoMapper
{
    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan)
    {
        return new SubscriptionPlanDto
        {
            ProductId = plan.ProductId,
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            Price = plan.Price,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit
        };
    }

    public static SubscriptionDto ToDto(CustomerSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            State = subscription.State,
            PlanHandle = subscription.ProductHandle,
            PlanName = subscription.ProductName,
            Price = subscription.Price,
            Interval = subscription.Interval,
            IntervalUnit = subscription.IntervalUnit,
            Currency = subscription.Currency,
            Balance = subscription.Balance,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt,
            CanceledAt = subscription.CanceledAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod
        };
    }
}
