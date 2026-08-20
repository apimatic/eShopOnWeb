using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed record SubscriptionUser(string Id, string Email, string UserName);

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record SubscriptionDetails(
    int Id,
    string ProductHandle,
    string PlanName,
    long PriceInCents,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingAt);

public sealed record SubscribeResult(SubscriptionDetails Subscription, bool AlreadyExisted);
