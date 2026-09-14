using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A shopper's enrollment on a <see cref="SubscriptionPlan"/>, as held by the billing system of record.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Identifier of the subscription in the billing provider.</summary>
    public int Id { get; init; }

    /// <summary>Provider lifecycle state, for example "active" or "canceled".</summary>
    public string State { get; init; } = string.Empty;

    public string? PlanHandle { get; init; }

    public string? PlanName { get; init; }

    /// <summary>Price the subscriber is billed each period, in the site's currency.</summary>
    public decimal Price { get; init; }

    public int Interval { get; init; }

    public string IntervalUnit { get; init; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>
    /// When the provider will next assess (bill) this subscription. Null once the subscription
    /// no longer bills, for example after cancellation.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// How the provider collects payment for this subscription, for example "remittance" (invoiced, no
    /// payment method captured) or "automatic" (charged to a payment method on file).
    /// </summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>The reference this application uses to key the subscriber in the billing provider.</summary>
    public string? CustomerReference { get; init; }

    public string? CustomerEmail { get; init; }

    public bool IsActive => string.Equals(State, "active", StringComparison.OrdinalIgnoreCase);

    public string BillingPeriod => $"{Interval} {IntervalUnit}".Trim();
}
