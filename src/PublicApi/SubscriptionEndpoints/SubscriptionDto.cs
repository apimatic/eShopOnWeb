using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription as returned to API clients, reflecting Maxio's current state.
/// </summary>
public class SubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string FormattedPrice { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>End of the current billing period — the next billing date for an active subscription.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
    public string PaymentCollectionMethod { get; set; } = string.Empty;

    public static SubscriptionDto FromDomain(Subscription s) => new()
    {
        Id = s.Id,
        State = s.State,
        PlanHandle = s.PlanHandle,
        PlanName = s.PlanName,
        PriceInCents = s.PriceInCents,
        FormattedPrice = s.FormattedPrice,
        Currency = s.Currency,
        Interval = s.Interval,
        IntervalUnit = s.IntervalUnit,
        NextBillingDate = s.NextBillingDate,
        CreatedAt = s.CreatedAt,
        PaymentCollectionMethod = s.PaymentCollectionMethod
    };
}
