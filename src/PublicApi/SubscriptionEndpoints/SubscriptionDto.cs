using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API representation of a customer's subscription, as reported by the billing system of record.</summary>
public class SubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    public long PriceInCents { get; set; }
    public decimal Price { get; set; }

    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>The next scheduled billing date confirmed back to the shopper.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public long CustomerId { get; set; }
    public string? CustomerReference { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public static SubscriptionDto From(CustomerSubscription s) => new()
    {
        Id = s.Id,
        State = s.State,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        PriceInCents = s.PriceInCents,
        Price = s.PriceInCents / 100m,
        Interval = s.Interval,
        IntervalUnit = s.IntervalUnit,
        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
        NextBillingAt = s.NextBillingAt,
        CustomerId = s.CustomerId,
        CustomerReference = s.CustomerReference,
        ActivatedAt = s.ActivatedAt,
        CreatedAt = s.CreatedAt,
    };
}
