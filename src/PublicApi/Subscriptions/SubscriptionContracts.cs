using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionRequest
{
    public string? ProductHandle { get; set; }
}

public sealed class SubscriptionPlanDto
{
    public string? Name { get; init; }
    public string? Handle { get; init; }
    public long? PriceInCents { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public string? ProductPricePointName { get; init; }
    public string? ProductPricePointHandle { get; init; }
    public bool? RequestCreditCard { get; init; }
    public bool? RequireCreditCard { get; init; }
}

public sealed class SubscriptionDto
{
    public int? Id { get; init; }
    public string? Reference { get; init; }
    public string? State { get; init; }
    public SubscriptionPlanDto? Plan { get; init; }
    public long? ProductPriceInCents { get; init; }
    public long? CurrentBillingAmountInCents { get; init; }
    // Maxio exposes the next assessment timestamp rather than a response field named
    // next_billing_at. This application-facing name makes that mapping explicit.
    public DateTimeOffset? NextBillingDate { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public string? Currency { get; init; }
    public bool IsCurrent { get; init; }
}

public sealed class SubscribeResponse
{
    public SubscriptionDto Subscription { get; init; } = new();
    public bool AlreadyExisted { get; init; }
}

public sealed class MySubscriptionsResponse
{
    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}
