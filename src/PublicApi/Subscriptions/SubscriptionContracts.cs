using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    int? PriceInCents,
    int? Interval,
    string? IntervalUnit);

public sealed record SubscriptionDto(
    int? Id,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    int? PriceInCents,
    string? State,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt);

public sealed record CreateSubscriptionRequest(string PlanHandle);

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> Plans);

public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);
