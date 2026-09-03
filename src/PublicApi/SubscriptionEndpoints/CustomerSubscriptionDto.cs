using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscription belonging to the current user, as returned to API callers.</summary>
public class CustomerSubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string? Reference { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public string? State { get; set; }
    public decimal? Price { get; set; }
    public long? PriceInCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>True when the subscribe call returned an existing subscription (idempotent hit) rather than creating one.</summary>
    public bool AlreadyExisted { get; set; }
}
