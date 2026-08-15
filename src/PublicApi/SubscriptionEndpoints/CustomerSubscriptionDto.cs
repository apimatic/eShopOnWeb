using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// API projection of a shopper's subscription — the confirmation of plan, price, state and next
/// billing date.
/// </summary>
public class CustomerSubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring price as a decimal amount.</summary>
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public string Currency { get; set; } = string.Empty;

    /// <summary>Maxio lifecycle state, e.g. <c>active</c>.</summary>
    public string State { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the subscription bills next (derived from the next assessment / current period end).</summary>
    public DateTimeOffset? NextBillingAt { get; set; }
}
