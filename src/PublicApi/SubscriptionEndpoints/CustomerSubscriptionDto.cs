using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API projection of a shopper's subscription, as reported by the billing system.</summary>
public class CustomerSubscriptionDto
{
    /// <summary>The billing-system subscription id.</summary>
    public int Id { get; set; }

    /// <summary>Lifecycle state (e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>).</summary>
    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public long PriceInCents { get; set; }

    public decimal Price { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// End of the current billing period — i.e. the next billing date when the next charge is
    /// scheduled. Null for states with no scheduled renewal.
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}
