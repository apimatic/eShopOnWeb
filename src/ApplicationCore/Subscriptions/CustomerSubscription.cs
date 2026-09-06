using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as recorded by the billing system of record.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public int Id { get; set; }

    /// <summary>The reference eShopOnWeb assigned to the subscription. Unique within the billing site.</summary>
    public string? Reference { get; set; }

    /// <summary>Billing system state, e.g. active, trialing, past_due, canceled.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription represents a live engagement (see <see cref="SubscriptionStates"/>).</summary>
    public bool IsActive => SubscriptionStates.IsEngaged(State);

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring price of the subscription in the smallest unit of <see cref="Currency"/>.</summary>
    public long PriceInCents { get; set; }

    public decimal Price => PriceInCents / 100m;

    public string Currency { get; set; } = string.Empty;

    public int Interval { get; set; }

    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>When the billing system will next assess the subscription, i.e. the next billing date.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Outstanding balance in the smallest currency unit.</summary>
    public long BalanceInCents { get; set; }

    /// <summary>How the billing system collects payment, e.g. automatic, remittance, invoice.</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Identifier of the billing-system customer that owns the subscription.</summary>
    public int CustomerId { get; set; }

    /// <summary>The reference eShopOnWeb assigned to the billing-system customer.</summary>
    public string? CustomerReference { get; set; }
}
