using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed record SubscriptionShopper(
    string UserId,
    string Email,
    string? FirstName = null,
    string? LastName = null);

public sealed record SubscriptionPlan(
    long MaxioProductId,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record ShopperSubscription(
    long MaxioSubscriptionId,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    string Currency,
    string State,
    DateTimeOffset? NextBillingAt);

public sealed record SubscribeResult(ShopperSubscription Subscription, bool Created);
