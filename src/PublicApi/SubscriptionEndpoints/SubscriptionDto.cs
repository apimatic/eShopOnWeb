using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// One subscription held by the calling shopper.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Billing-provider identifier for this subscription.</summary>
    public int Id { get; set; }

    /// <summary>Deterministic reference this store assigns when it enrolls a shopper.</summary>
    public string Reference { get; set; }

    public string PlanHandle { get; set; }

    public string PlanName { get; set; }

    /// <summary>Recurring price in major units.</summary>
    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    /// <summary>ISO currency code, when the billing provider reports one.</summary>
    public string Currency { get; set; }

    /// <summary>Provider state, for example "active", "past_due" or "canceled".</summary>
    public string State { get; set; }

    /// <summary>True while the subscription is in a non-terminal state.</summary>
    public bool IsLive { get; set; }

    /// <summary>
    /// When the next regularly scheduled charge is expected. Null when the provider reports none.
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}
