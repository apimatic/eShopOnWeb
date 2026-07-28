using System;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// API projection of a shopper's subscription, as recorded by Maxio.
/// </summary>
public class CustomerSubscriptionDto
{
    public int Id { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }

    /// <summary>Lifecycle state, e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the subscription will next be billed (Maxio's current-period end / next assessment).</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public int CustomerId { get; set; }
    public string? CustomerReference { get; set; }

    public static CustomerSubscriptionDto FromDomain(CustomerSubscription subscription)
    {
        return new CustomerSubscriptionDto
        {
            Id = subscription.Id,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PriceInCents = subscription.PriceInCents,
            Price = subscription.PriceInCents.HasValue ? subscription.PriceInCents.Value / 100m : null,
            Currency = subscription.Currency,
            State = subscription.State,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingDate = subscription.NextBillingAt,
            CustomerId = subscription.CustomerId,
            CustomerReference = subscription.CustomerReference
        };
    }
}
