using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string Currency);

public sealed record SubscriptionDto(
    int Id,
    string Reference,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    string Currency,
    string State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CurrentPeriodEndsAt);

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> Plans);
public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);
public sealed record SubscribeResponse(SubscriptionDto Subscription, bool Created);

public sealed class SubscribeRequest
{
    [Required]
    [StringLength(255)]
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed record BillingUser(string Id, string Email, string FirstName, string LastName);
public sealed record SubscribeResult(SubscriptionDto Subscription, bool Created);
