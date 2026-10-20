using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>The eShop identity a billing customer belongs to.</summary>
/// <param name="UserName">The authenticated user name; the stable key for the buyer.</param>
/// <param name="Email">The buyer's email address, sent to the billing provider on customer creation.</param>
public record BillingBuyer(string UserName, string Email);

public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    bool? RequiresCreditCard);

/// <param name="Plans">The plans offered.</param>
/// <param name="IsTruncated">True when the provider had more plans than were read; <paramref name="Plans"/> is then partial.</param>
public record SubscriptionPlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool IsTruncated);

public record SubscriptionSummary(
    int Id,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    string? State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset? CreatedAt,
    string? Reference);

public enum SubscribeStatus
{
    /// <summary>This call created the subscription.</summary>
    Created = 1,

    /// <summary>The buyer already holds this subscription; nothing new was created.</summary>
    AlreadySubscribed = 2,

    /// <summary>
    /// An earlier request for the same subscription is still in flight, or its outcome is being settled.
    /// Nothing new was created; repeat the call later to get the final answer.
    /// </summary>
    Pending = 3
}

/// <param name="Subscription">The subscription; null only when <paramref name="Status"/> is <see cref="SubscribeStatus.Pending"/>.</param>
public record SubscribeResult(SubscribeStatus Status, string Reference, SubscriptionSummary? Subscription);
