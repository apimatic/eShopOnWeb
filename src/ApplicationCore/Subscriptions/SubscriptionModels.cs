using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>A plan a shopper can subscribe to.</summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    int IntervalCount,
    string IntervalUnit,
    bool RequiresPaymentMethod);

/// <summary>A shopper's subscription as held by the billing system.</summary>
public record CustomerSubscription(
    string SubscriptionId,
    string PlanHandle,
    string PlanName,
    decimal Price,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CreatedAt);

/// <summary>The eShopOnWeb identity a Maxio customer is keyed on.</summary>
public record Subscriber(string UserId, string Email);

/// <summary>Outcome of a subscribe call; <see cref="Created"/> is false when the shopper already had this plan.</summary>
public record SubscribeResult(CustomerSubscription Subscription, bool Created);
