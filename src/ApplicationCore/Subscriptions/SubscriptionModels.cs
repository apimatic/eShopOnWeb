using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to (provided by the billing system).
/// </summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

/// <summary>
/// A shopper's subscription as reported by the billing system.
/// </summary>
public record UserSubscription(
    string SubscriptionId,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CreatedAt);

/// <summary>
/// The authenticated eShopOnWeb user a subscription is billed to.
/// </summary>
public record Subscriber(string UserId, string Email);

/// <param name="Subscription">The resulting subscription.</param>
/// <param name="Created">False when the shopper was already subscribed to the plan and nothing new was created.</param>
public record SubscribeResult(UserSubscription Subscription, bool Created);
