using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string Handle,
    string? Name,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    string? Currency);

public sealed record SubscriptionDto(
    long? Id,
    string? Reference,
    string? ProductHandle,
    string? ProductName,
    long? PriceInCents,
    long? CurrentBillingAmountInCents,
    string? Currency,
    string? State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CurrentPeriodEndsAt);

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> Plans);
public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);
public sealed record CreateSubscriptionResponse(SubscriptionDto Subscription, bool Created);

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; init; } = string.Empty;
}

public sealed record BillingUser(string Id, string Email, string FirstName, string LastName);

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<CreateSubscriptionResponse> SubscribeAsync(string username, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListMySubscriptionsAsync(string username, CancellationToken cancellationToken);
}
