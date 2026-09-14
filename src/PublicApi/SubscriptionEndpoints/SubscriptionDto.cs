using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription held by the signed-in shopper.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Billing state, e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>.</summary>
    public string? State { get; set; }

    public decimal Price { get; set; }

    public string? Currency { get; set; }

    /// <summary>How the provider collects this subscription's balance, e.g. <c>remittance</c>.</summary>
    public string? PaymentCollectionMethod { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the provider will next bill this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public decimal TotalRevenue { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }
}
