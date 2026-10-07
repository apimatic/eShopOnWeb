using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The state of one of the current user's subscriptions, as recorded by the
/// billing system.
/// </summary>
public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string? Reference { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    /// <summary>Recurring plan price in cents.</summary>
    public long? ProductPriceInCents { get; set; }
    /// <summary>Recurring plan price in the account currency (e.g. USD).</summary>
    public decimal? ProductPrice { get; set; }
    /// <summary>Provider subscription state, e.g. "active", "canceled", "trialing".</summary>
    public string State { get; set; } = string.Empty;
    /// <summary>When the next billing event occurs.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
}
