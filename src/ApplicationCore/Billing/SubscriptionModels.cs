using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>A plan the shopper can subscribe to, as offered by the billing system's product family.</summary>
public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    long? InitialChargeInCents,
    long? TrialPriceInCents,
    int? TrialInterval,
    string? TrialIntervalUnit);

/// <param name="IsTruncated">True when the plan list stopped at its page cap, so more plans may exist than are listed.</param>
public sealed record SubscriptionPlanList(IReadOnlyList<SubscriptionPlan> Plans, bool IsTruncated);

/// <summary>A subscription as reported by the billing system of record.</summary>
public sealed record SubscriptionDetails(
    int SubscriptionId,
    string? PlanHandle,
    string? PlanName,
    string? State,
    long? PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CreatedAt);

public enum SubscribeOutcome
{
    /// <summary>A new subscription was created.</summary>
    Created,

    /// <summary>The user already holds a subscription to this plan; it is returned instead of creating another.</summary>
    AlreadySubscribed,

    /// <summary>The user already holds a subscription to a different plan of this family.</summary>
    SubscribedToOtherPlan,

    /// <summary>A subscribe request for this user is in flight, or its outcome is still being confirmed with the billing system.</summary>
    Pending,

    /// <summary>The plan handle is not one of the plans this store offers.</summary>
    UnknownPlan,

    /// <summary>The billing system rejected the subscription (e.g. validation).</summary>
    Rejected,

    /// <summary>The authenticated user does not exist in the identity store.</summary>
    UserNotFound
}

public sealed record SubscribeResult(
    SubscribeOutcome Outcome,
    SubscriptionDetails? Subscription = null,
    IReadOnlyList<string>? Errors = null,
    string? Message = null);

public sealed record MySubscriptionsResult(bool UserFound, IReadOnlyList<SubscriptionDetails> Subscriptions);
