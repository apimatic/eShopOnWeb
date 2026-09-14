using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public sealed record Shopper(string UserId, string Email);

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    decimal Price,
    string Currency,
    int BillingInterval,
    string BillingIntervalUnit);

public sealed record ShopperSubscription(
    long Id,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    decimal Price,
    string Currency,
    string State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt);

public sealed record SubscribeResult(ShopperSubscription Subscription, bool Created);
