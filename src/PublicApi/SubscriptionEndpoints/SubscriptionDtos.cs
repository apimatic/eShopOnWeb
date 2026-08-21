using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    int Id,
    string Handle,
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
    string State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt);

public sealed record SubscribeRequest(string ProductHandle);

public sealed record SubscribeResponse(bool Created, SubscriptionDto Subscription);

public sealed record SubscriptionUser(string Id, string UserName, string Email);

public interface ISubscriptionService
{
    System.Threading.Tasks.Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(
        System.Threading.CancellationToken cancellationToken);

    System.Threading.Tasks.Task<SubscribeResponse?> SubscribeAsync(
        SubscriptionUser user,
        string productHandle,
        System.Threading.CancellationToken cancellationToken);

    System.Threading.Tasks.Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(
        SubscriptionUser user,
        System.Threading.CancellationToken cancellationToken);
}
