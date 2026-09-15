using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionRequest
{
    public string? PlanHandle { get; set; }
}

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    decimal Price,
    string Currency,
    int Interval,
    string IntervalUnit,
    string? PricePointHandle);

public sealed record SubscriptionDto(
    int Id,
    string PlanHandle,
    string? PlanName,
    string State,
    decimal Price,
    string Currency,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? NextAssessmentDate,
    string Reference);

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> Plans);

public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);
