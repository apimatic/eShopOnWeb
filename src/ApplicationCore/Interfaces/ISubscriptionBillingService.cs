using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);
    Task<BillingSubscription> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsAsync(
        BillingUser user,
        CancellationToken cancellationToken = default);
}

public sealed record BillingUser(string UserId, string Email);

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record BillingSubscription(
    long Id,
    string Reference,
    string State,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    DateTimeOffset? NextBillingAt);
