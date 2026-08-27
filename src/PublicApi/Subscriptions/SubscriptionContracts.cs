using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlanDto(
    string ProductHandle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record SubscriptionDto(
    int Id,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingDate);

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public sealed record CreateSubscriptionResult(SubscriptionDto Subscription, bool Created);

public sealed record BillingUserIdentity(
    string UserId,
    string Email,
    string FirstName,
    string LastName);

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);
    Task<CreateSubscriptionResult> SubscribeAsync(
        BillingUserIdentity user,
        string productHandle,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsAsync(
        BillingUserIdentity user,
        CancellationToken cancellationToken);
}
