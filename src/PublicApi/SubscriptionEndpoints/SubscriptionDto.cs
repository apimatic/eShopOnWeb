using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A shopper's subscription, as the billing system currently reports it.</summary>
public class SubscriptionDto
{
    /// <summary>The billing system's subscription id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Billing-system state, e.g. "active", "trialing", "past_due", "canceled".
    /// </summary>
    public string State { get; set; } = string.Empty;

    /// <summary>False once the subscription has been cancelled, expired or otherwise ended.</summary>
    public bool IsCurrent { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string? Currency { get; set; }

    public int Interval { get; set; }

    public string? IntervalUnit { get; set; }

    /// <summary>
    /// How the billing system collects payment: "automatic" charges a stored payment method,
    /// "remittance"/"invoice" issues an invoice for the shopper to pay.
    /// </summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Outstanding amount on the subscription.</summary>
    public decimal Balance { get; set; }

    public long BalanceInCents { get; set; }

    /// <summary>When the billing system will next attempt to collect payment.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>The billing system's customer id for the signed-in shopper.</summary>
    public string? CustomerId { get; set; }
}
