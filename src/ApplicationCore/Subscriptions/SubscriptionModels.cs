using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>A plan offered for subscription. <see cref="IntervalUnit"/> is the billing system's value (e.g. "month").</summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit);

/// <summary>The offered plans. <see cref="Truncated"/> is true when the listing stopped at its page cap.</summary>
public record SubscriptionPlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool Truncated);

/// <summary>A subscription as the billing system reports it. <see cref="State"/> is the billing system's value (e.g. "active").</summary>
public record SubscriptionDetails(
    int SubscriptionId,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    string State,
    long? PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CreatedAt);

/// <summary>The shopper subscribing: the buyer id from their token plus what the billing customer needs.</summary>
public record Subscriber(string BuyerId, string Email, string FirstName, string LastName);

public enum SubscribeOutcome
{
    /// <summary>A new subscription was created.</summary>
    Created,

    /// <summary>The shopper already holds a live subscription to the requested plan; it is returned instead.</summary>
    AlreadySubscribed,

    /// <summary>The shopper already holds a live subscription to a different plan.</summary>
    SubscribedToDifferentPlan,

    /// <summary>Another subscribe request for this shopper is still running.</summary>
    InProgress,

    /// <summary>The billing system could not confirm whether the subscription was created; settled on the next request.</summary>
    OutcomeUnknown,

    /// <summary>The requested plan is not one this store offers.</summary>
    UnknownPlan
}

public record SubscribeResult(SubscribeOutcome Outcome, SubscriptionDetails? Subscription = null, string? Message = null);

/// <summary>
/// A shopper's subscriptions. <see cref="EnrollmentStatus"/> is the local enrollment's state (null when the
/// shopper never subscribed through this store); <see cref="Truncated"/> is true when the billing system's
/// list was missing a subscription this store knows about.
/// </summary>
public record CustomerSubscriptions(
    IReadOnlyList<SubscriptionDetails> Subscriptions,
    SubscriptionEnrollmentStatus? EnrollmentStatus,
    bool Truncated);
