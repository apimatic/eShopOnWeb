using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlanDto(string Handle, string Name, string? Description, long? PriceInCents, int? Interval, string? IntervalUnit);
public sealed record SubscribeRequest(string ProductHandle);
public sealed record MySubscriptionDto(int? Id, string? ProductHandle, string? State, DateTimeOffset? NextBillingDate, long? PriceInCents);
public sealed record SubscriptionResult(MySubscriptionDto Subscription, bool AlreadySubscribed);
public interface ISubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);
    Task<SubscriptionResult> SubscribeAsync(string userId, string userName, string email, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<MySubscriptionDto>> GetMySubscriptionsAsync(string userId, CancellationToken cancellationToken);
}
