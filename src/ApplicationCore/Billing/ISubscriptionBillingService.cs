using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionEnrollment> SubscribeAsync(
        BillingUser user,
        string productHandle,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserSubscription>> GetSubscriptionsAsync(
        BillingUser user,
        CancellationToken cancellationToken = default);
}

public sealed record BillingUser(string Id, string Email);

public sealed record SubscriptionPlan(
    long Id,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record UserSubscription(
    long Id,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt,
    string Currency);

public sealed record SubscriptionEnrollment(UserSubscription Subscription, bool AlreadyExisted);
