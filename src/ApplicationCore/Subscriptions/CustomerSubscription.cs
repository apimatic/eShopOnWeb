using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A customer's subscription as reported by Maxio, projected into the fields the
/// storefront cares about (plan, price, state, next billing date).
/// </summary>
public class CustomerSubscription
{
    /// <summary>The Maxio subscription id.</summary>
    public long Id { get; init; }

    /// <summary>The subscription lifecycle state (e.g. active, trialing, canceled).</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Handle of the subscribed plan/product.</summary>
    public string? PlanHandle { get; init; }

    /// <summary>Name of the subscribed plan/product.</summary>
    public string? PlanName { get; init; }

    /// <summary>The subscribed recurring price in cents.</summary>
    public long PriceInCents { get; init; }

    /// <summary>The subscribed recurring price as a decimal amount.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO currency code (e.g. "USD").</summary>
    public string Currency { get; init; } = "USD";

    /// <summary>
    /// When the current billing period ends — i.e. the next scheduled billing date.
    /// </summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    /// <summary>
    /// When Maxio will next attempt to capture payment. Usually tracks
    /// <see cref="NextBillingAt"/> but can diverge after a failed renewal.
    /// </summary>
    public DateTimeOffset? NextAssessmentAt { get; init; }

    /// <summary>When the current billing period started.</summary>
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    /// <summary>When the subscription was created.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>How payment is collected (e.g. automatic, remittance).</summary>
    public string? PaymentCollectionMethod { get; init; }
}
