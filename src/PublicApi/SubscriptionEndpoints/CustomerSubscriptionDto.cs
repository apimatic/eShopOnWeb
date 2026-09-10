using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API representation of a shopper's subscription.</summary>
public class CustomerSubscriptionDto
{
    public long Id { get; set; }

    /// <summary>The billing-system state (e.g. active, trialing, canceled).</summary>
    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public string? Currency { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next billing attempt is scheduled (the next billing date).</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Whether the subscription is currently providing service.</summary>
    public bool IsLive { get; set; }

    public static CustomerSubscriptionDto FromDomain(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.Price,
        Currency = subscription.Currency,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
        IsLive = subscription.IsLive,
    };
}
