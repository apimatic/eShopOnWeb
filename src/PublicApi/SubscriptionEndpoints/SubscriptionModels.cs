using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string ProductHandle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record SubscriptionDto(
    int Id,
    string ProductHandle,
    string PlanName,
    long PriceInCents,
    string? Currency,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt);

public sealed record SubscriptionPlanListResponse(IReadOnlyList<SubscriptionPlanDto> Plans);

public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);

public sealed record CreateSubscriptionRequest(string ProductHandle);

public sealed record CreateSubscriptionResult(SubscriptionDto Subscription, bool Created);

public sealed record ShopperIdentity(string UserId, string Email);
