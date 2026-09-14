using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as the billing provider reports it.
/// The provider is the system of record; nothing here is persisted by eShopOnWeb.
/// </summary>
public sealed class CustomerSubscription
{
    public required int Id { get; init; }

    /// <summary>Provider subscription state as sent on the wire, e.g. <c>active</c> or <c>canceled</c>.</summary>
    public required string State { get; init; }

    /// <summary>True while the subscription entitles the shopper to the plan (or is still settling into it).</summary>
    public bool IsActive { get; init; }

    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }

    /// <summary>Recurring price for this subscription, in cents.</summary>
    public long? PriceInCents { get; init; }

    public string? Currency { get; init; }

    public int? CustomerId { get; init; }
    public string? CustomerReference { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the provider will next assess (bill) this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? TrialEndsAt { get; init; }
    public DateTimeOffset? CanceledAt { get; init; }

    /// <summary>
    /// How the provider collects this subscription's balance, as it reports it — e.g. <c>remittance</c>
    /// (invoiced) or <c>automatic</c> (charged to a payment method on file).
    /// </summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>
    /// True when this particular call enrolled the shopper; false when an existing enrollment was
    /// returned instead. Lets a caller distinguish a fresh signup from a replayed one.
    /// </summary>
    public bool WasCreatedByThisRequest { get; init; }
}
