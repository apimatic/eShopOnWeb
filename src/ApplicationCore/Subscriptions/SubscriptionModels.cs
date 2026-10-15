using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>The eShopOnWeb user enrolling in a subscription, resolved from the authenticated identity.</summary>
/// <param name="UserId">Identity user id; keys the user's enrollment record.</param>
/// <param name="NormalizedUserName">Identity's normalized (unique) user name; keys the user's billing customer.</param>
/// <param name="Email">Contact e-mail sent to the billing provider.</param>
public sealed record Subscriber(string UserId, string NormalizedUserName, string Email);

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit,
    bool RequiresPaymentMethod);

/// <param name="IsTruncated">True when the provider had more plans than the listing cap; the list is then incomplete.</param>
public sealed record SubscriptionPlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool IsTruncated);

public sealed record SubscriptionDetails(
    int Id,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset? CreatedAt);

public enum SubscribeOutcome
{
    /// <summary>A new subscription was created by this request.</summary>
    Created,

    /// <summary>The user already holds a live subscription to the requested plan (repeat or double-submit).</summary>
    AlreadySubscribed,

    /// <summary>The provider could not be reached after the create was sent; the outcome is settled on a later request.</summary>
    Unconfirmed
}

public sealed record SubscribeResult(SubscribeOutcome Outcome, SubscriptionDetails? Subscription);

/// <param name="HasUnconfirmedEnrollment">True while a subscribe attempt for this user is still in flight or unsettled.</param>
public sealed record CustomerSubscriptions(IReadOnlyList<SubscriptionDetails> Subscriptions, bool HasUnconfirmedEnrollment);
