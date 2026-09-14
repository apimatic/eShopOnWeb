using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// API view of a shopper's subscription.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }

    /// <summary>Lifecycle state (e.g. <c>active</c>).</summary>
    public string? State { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price in major units.</summary>
    public decimal Price { get; set; }

    public string? Currency { get; set; }

    /// <summary>Next billing date (end of the current billing period).</summary>
    public DateTimeOffset? NextBillingAt { get; set; }
}
