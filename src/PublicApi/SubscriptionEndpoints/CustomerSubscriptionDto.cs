using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlanDto"/>.
/// </summary>
public class CustomerSubscriptionDto
{
    /// <summary>Billing-system subscription id.</summary>
    public int Id { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public decimal? Price { get; set; }

    public string? Currency { get; set; }

    /// <summary>Lifecycle state reported by the billing system, e.g. "active".</summary>
    public string? State { get; set; }

    /// <summary>How the billing system collects for this subscription, e.g. "remittance" (invoiced).</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>True while the subscription still entitles the shopper.</summary>
    public bool IsLive { get; set; }

    /// <summary>End of the current billing period — the date the subscription next bills.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}
