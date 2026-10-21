using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>Who is subscribing: this application's user id (stable) and e-mail (contact only, never a key).</summary>
public record SubscriberIdentity(string UserId, string Email);

public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    long PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    bool? RequiresCreditCard);

/// <param name="IsComplete">False when the list was longer than the page ceiling and was cut short.</param>
public record SubscriptionPlanList(IReadOnlyList<SubscriptionPlan> Plans, bool IsComplete);

public record CustomerSubscription(
    int Id,
    string? PlanHandle,
    string? PlanName,
    string? ProductFamilyHandle,
    decimal? Price,
    long? PriceInCents,
    string? Currency,
    string? State,
    bool HasAccess,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodStartedAt,
    DateTimeOffset? CreatedAt,
    string? PaymentCollectionMethod,
    string? Reference);

/// <summary>A signup of ours whose outcome is not settled yet.</summary>
public record PendingSignup(string SignupId, string PlanHandle, string Status, string? ActionLink,
    DateTimeOffset CreatedAt);

/// <param name="IsComplete">False when the list was longer than the page ceiling and was cut short.</param>
public record CustomerSubscriptionList(
    IReadOnlyList<CustomerSubscription> Subscriptions,
    IReadOnlyList<PendingSignup> PendingSignups,
    bool IsComplete);

public enum SubscribeOutcome
{
    /// <summary>This request created the subscription.</summary>
    Created,

    /// <summary>The user already holds a subscription on this plan; none was created.</summary>
    AlreadySubscribed,

    /// <summary>The outcome is not known yet. Not a failure: it is settled later under the same signup.</summary>
    Pending,

    /// <summary>The customer must complete an action (3-D Secure) at <see cref="SubscribeResult.ActionLink"/>.</summary>
    ActionRequired,
}

public record SubscribeResult(
    SubscribeOutcome Outcome,
    string SignupId,
    CustomerSubscription? Subscription,
    string? ActionLink = null,
    TimeSpan? RetryAfter = null);
