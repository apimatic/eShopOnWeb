using System;

namespace Microsoft.eShopWeb.PublicApi.Services.Subscriptions;

public sealed record SubscriptionPlan(
    long Id,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    decimal Price,
    int? Interval,
    string? IntervalUnit);

public sealed record CurrentSubscription(
    long SubscriptionId,
    string State,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CreatedAt);

public sealed record SubscribeResult(CurrentSubscription Subscription, bool AlreadySubscribed);
