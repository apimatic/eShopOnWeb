using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>A subscribable plan (a Maxio "product") that belongs to the configured product family.</summary>
public sealed class MaxioPlan
{
    public required long Id { get; init; }
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required long PriceInCents { get; init; }
    public required int Interval { get; init; }
    public required string IntervalUnit { get; init; }
    public required bool RequiresCreditCard { get; init; }
    public required bool Taxable { get; init; }
    public string? ProductPricePointName { get; init; }
    public string? ProductPricePointHandle { get; init; }
}

/// <summary>A Maxio customer that maps to an eShopOnWeb user (matched by its reference).</summary>
public sealed class MaxioCustomer
{
    public required long Id { get; init; }
    public required string Reference { get; init; }
    public required string Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}

/// <summary>
/// A Maxio subscription projected for eShopOnWeb. Carries everything the UI needs to
/// confirm plan, price, state and next billing date back to the shopper.
/// </summary>
public sealed class MaxioSubscription
{
    public required long Id { get; init; }
    public required string State { get; init; }
    public required string PlanHandle { get; init; }
    public required string PlanName { get; init; }
    public required long PriceInCents { get; init; }
    public required int Interval { get; init; }
    public required string IntervalUnit { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? CanceledAt { get; init; }
    public bool CancelAtEndOfPeriod { get; init; }
    public string? PaymentCollectionMethod { get; init; }
    public required long CustomerId { get; init; }

    /// <summary>Whether the subscription is still in force (i.e. not canceled or expired).</summary>
    public bool IsCurrent =>
        !string.Equals(State, "canceled", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(State, "expired", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The outcome of an idempotent subscribe operation.</summary>
public sealed class SubscriptionEnrollmentResult
{
    public SubscriptionEnrollmentResult(MaxioSubscription subscription, bool created)
    {
        Subscription = subscription;
        Created = created;
    }

    /// <summary>The subscription the caller now has for the requested plan.</summary>
    public MaxioSubscription Subscription { get; }

    /// <summary>True when the subscription was just created; false when the caller already had the plan.</summary>
    public bool Created { get; }
}
