using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    decimal Price,
    int? Interval,
    string? IntervalUnit,
    bool IsEligible);

public sealed record SubscriptionDto(
    int Id,
    string Reference,
    string? PlanHandle,
    string? PlanName,
    decimal? Price,
    string? Currency,
    string? State,
    DateTimeOffset? NextBillingDate);

public sealed record CreateSubscriptionRequest(string PlanHandle);

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> Plans);

public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);

public sealed record SubscriptionResponse(SubscriptionDto Subscription, bool Created);

public sealed record Shopper(string UserId, string Email, string UserName);

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionDto> SubscribeAsync(Shopper shopper, string planHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsAsync(string userId, CancellationToken cancellationToken);
}
