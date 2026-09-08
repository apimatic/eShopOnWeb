using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscription owned by the calling user (a Maxio subscription).</summary>
public class SubscriptionDto
{
    /// <summary>Maxio subscription id (numeric; assigned by Maxio and not stable across re-seeds).</summary>
    public int? SubscriptionId { get; set; }

    /// <summary>Handle of the enrolled plan (e.g. "eshop-pro").</summary>
    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring price in cents.</summary>
    public long? PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount in the subscription's currency.</summary>
    public decimal? Price { get; set; }

    /// <summary>ISO-4217 currency code reported by Maxio for this subscription (e.g. "USD").</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Subscription state (e.g. "active", "past_due", "canceled").</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Next billing/assessment date.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }
}
