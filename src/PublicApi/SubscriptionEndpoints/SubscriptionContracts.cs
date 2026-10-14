using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(string Handle, string Name, long PriceInCents, int? Interval, string? IntervalUnit);
public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> Plans, bool IsTruncated);
public sealed record CreateSubscriptionRequest(string PlanHandle);
public sealed record SubscriptionDto(int? Id, string? PlanHandle, string? PlanName, long? PriceInCents, string? State, DateTimeOffset? NextBillingAt);
public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);
