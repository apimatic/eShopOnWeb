using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API representation of a customer's subscription.</summary>
public class CustomerSubscriptionDto
{
    public int Id { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }

    /// <summary>Maxio subscription state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True when the subscription is in a live state (the customer has access).</summary>
    public bool IsLive { get; set; }

    public long? PriceInCents { get; set; }
    public decimal? Price => PriceInCents.HasValue ? PriceInCents.Value / 100m : null;

    /// <summary>The next billing date (end of the current billing period).</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public string? Reference { get; set; }

    public static CustomerSubscriptionDto FromModel(CustomerSubscription s) => new()
    {
        Id = s.Id,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        State = s.State,
        IsLive = s.IsLive,
        PriceInCents = s.PriceInCents,
        NextBillingAt = s.NextBillingAt,
        Reference = s.Reference,
    };
}
