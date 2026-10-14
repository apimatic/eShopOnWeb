using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscribeRequest(string ProductHandle);
public sealed record SubscriptionPlanDto(string Handle, string Name, string? Description, long? PriceInCents, int? Interval, string? IntervalUnit);
public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> Plans, bool IsTruncated);
public sealed record SubscriptionDto(string? Id, string? Reference, string? ProductHandle, string? ProductName, long? PriceInCents, string? State, DateTimeOffset? NextBillingAt);
public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);
