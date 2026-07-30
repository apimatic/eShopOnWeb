using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A customer's subscription as surfaced to the storefront: which plan, at what price, in
/// what state, and when it renews next.
/// </summary>
public class CustomerSubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public long? PriceInCents { get; set; }
    public string? FormattedPrice { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>True when an already-existing enrollment was returned (idempotent subscribe).</summary>
    public bool AlreadyExisted { get; set; }
}
