using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription the calling user holds, as Maxio Advanced Billing records it.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Maxio subscription id.</summary>
    public int? Id { get; set; }

    /// <summary>Maxio subscription state, e.g. <c>active</c>.</summary>
    public string? State { get; set; }

    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }

    /// <summary>Recurring price in major units.</summary>
    public decimal? Price { get; set; }

    public long? PriceInCents { get; set; }
    public string? Currency { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>The next billing date, as Maxio's next assessment date.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }

    /// <summary>The Maxio customer id this subscription belongs to.</summary>
    public int? CustomerId { get; set; }

    /// <summary>The eShopOnWeb-owned reference that ties the Maxio customer to the signed-in user.</summary>
    public string? CustomerReference { get; set; }

    /// <summary>The reference eShopOnWeb stamped on the subscription, for traceability.</summary>
    public string? Reference { get; set; }
}
