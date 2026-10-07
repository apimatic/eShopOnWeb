using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public record SubscriberIdentity(string UserId, string Email, string? FirstName = null, string? LastName = null);

public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    int Interval,
    string IntervalUnit);

public record SubscriptionDetails(
    long SubscriptionId,
    long CustomerId,
    string State,
    string PlanHandle,
    string PlanName,
    decimal Price,
    string? Currency,
    int Interval,
    string IntervalUnit,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CreatedAt,
    string? PaymentCollectionMethod);

public record SubscribeResult(SubscriptionDetails Subscription, bool Created);
