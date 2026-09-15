using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    int Interval,
    string IntervalUnit);

public sealed record UserSubscription(
    int Id,
    string ProductHandle,
    string ProductName,
    decimal Price,
    string State,
    DateTimeOffset? NextBillingDate);

public sealed record SubscribeToPlan(
    string UserId,
    string Email,
    string FirstName,
    string LastName,
    string ProductHandle);

public sealed record SubscribeResult(
    UserSubscription Subscription,
    bool Created);

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<SubscribeResult> SubscribeAsync(SubscribeToPlan command, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserSubscription>> ListSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken);
}
