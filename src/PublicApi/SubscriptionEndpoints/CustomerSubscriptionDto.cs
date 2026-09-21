using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API view of a shopper's subscription, as reflected back from the billing system of record.</summary>
public class CustomerSubscriptionDto
{
    public int Id { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public long? PriceInCents { get; set; }

    public decimal? Price { get; set; }

    /// <summary>Subscription state (e.g. <c>active</c>, <c>trialing</c>).</summary>
    public string? State { get; set; }

    /// <summary>When the next scheduled charge occurs.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public int CustomerId { get; set; }

    public string? CustomerReference { get; set; }
}
