using System;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription as tracked by Maxio Advanced Billing, confirmed back to the
/// shopper: plan, price, state and next billing date.
/// </summary>
public class SubscriptionDto
{
    public long Id { get; set; }

    /// <summary>External id this app assigned to the subscription.</summary>
    public string? Reference { get; set; }

    /// <summary>Billing-system state, e.g. "active", "trialing", "past_due".</summary>
    public string State { get; set; } = string.Empty;

    public long? ProductId { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Current recurring price in integer cents.</summary>
    public int? PriceInCents { get; set; }

    /// <summary>Price in major currency units, for display.</summary>
    public decimal? Price => PriceInCents is { } cents ? cents / 100m : null;

    public string? Currency { get; set; }

    /// <summary>When the next regularly scheduled charge will occur.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public static SubscriptionDto From(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State,
        ProductId = subscription.ProductId,
        PlanHandle = subscription.ProductHandle,
        PlanName = subscription.ProductName,
        PriceInCents = subscription.PriceInCents,
        Currency = subscription.Currency,
        NextBillingAt = subscription.NextBillingAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
    };
}
