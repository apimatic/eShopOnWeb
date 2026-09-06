using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription held by the authenticated shopper.
/// </summary>
public class SubscriptionDto
{
    public long Id { get; set; }

    /// <summary>Reference assigned by eShopOnWeb; also the idempotency anchor for the subscription.</summary>
    public string? Reference { get; set; }

    /// <summary>Normalised lifecycle state, e.g. "Active", "PastDue", "Canceled".</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Lifecycle state exactly as the billing provider reported it, e.g. "active".</summary>
    public string? ProviderState { get; set; }

    /// <summary>Handle of the subscribed plan.</summary>
    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Recurring amount currently subscribed to.</summary>
    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string? Currency { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next charge is scheduled to be attempted.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Outstanding balance on the subscription.</summary>
    public decimal Balance { get; set; }

    public long BalanceInCents { get; set; }

    /// <summary>How the provider collects payment, e.g. "remittance".</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Id of the billing customer that owns this subscription.</summary>
    public long CustomerId { get; set; }

    /// <summary>Reference of the billing customer, derived from the eShopOnWeb user name.</summary>
    public string? CustomerReference { get; set; }
}
