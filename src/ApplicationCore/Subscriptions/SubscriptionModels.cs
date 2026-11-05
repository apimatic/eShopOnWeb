using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    string? Currency,
    int? Interval,
    string? IntervalUnit);

public record UserSubscription(
    string SubscriptionId,
    string PlanHandle,
    string PlanName,
    decimal Price,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    string State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CreatedAt);

public record SubscribeResult(UserSubscription Subscription, bool AlreadySubscribed);

/// <summary>The authenticated eShopOnWeb user a subscription belongs to.</summary>
public record SubscriberIdentity(string UserId, string Email, string? FirstName, string? LastName);
