using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    int? PricePointId,
    string? PricePointHandle,
    bool RequiresPaymentMethod);

public sealed record ShopperSubscriptionDto(
    int Id,
    string Reference,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    string? Currency);

public sealed record CreateSubscriptionRequest(string ProductHandle);

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<ShopperSubscriptionDto> SubscribeAsync(
        string username,
        string productHandle,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ShopperSubscriptionDto>> ListForUserAsync(
        string username,
        CancellationToken cancellationToken);
}
