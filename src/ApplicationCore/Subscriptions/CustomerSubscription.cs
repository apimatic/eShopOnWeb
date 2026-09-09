using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription that belongs to an eShopOnWeb user, as reflected by the billing system of record.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Billing-system subscription id.</summary>
    public int Id { get; set; }

    /// <summary>Lifecycle state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }

    public long PriceInCents { get; set; }
    public string FormattedPrice { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the subscription will next be assessed/billed.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    /// <summary>Stable reference tying this billing customer back to the eShopOnWeb user.</summary>
    public string? CustomerReference { get; set; }

    public int CustomerId { get; set; }
}
