using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as recorded by the billing system.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier assigned by the billing system.</summary>
    public long Id { get; set; }

    /// <summary>Identifier assigned by eShopOnWeb.</summary>
    public string? Reference { get; set; }

    /// <summary>Billing state, for example <c>active</c>, <c>past_due</c> or <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>False once the subscription has ended for good.</summary>
    public bool IsLive { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public decimal Price { get; set; }

    public long PriceInCents { get; set; }

    public string? Currency { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    /// <summary>When the subscription will next be billed.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Outstanding amount on the subscription.</summary>
    public decimal Balance { get; set; }

    public long BalanceInCents { get; set; }

    /// <summary>How the billing system collects payment, for example <c>remittance</c>.</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Identifier of the billing customer this subscription belongs to.</summary>
    public long CustomerId { get; set; }

    public string? CustomerReference { get; set; }
}
