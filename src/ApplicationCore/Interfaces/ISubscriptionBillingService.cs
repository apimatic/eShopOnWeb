using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SubscriptionPlan(
    string Name,
    string Handle,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public record CustomerSubscription(
    int Id,
    string PlanName,
    string PlanHandle,
    string State,
    long? PriceInCents,
    DateTimeOffset? NextBillingDate);

/// <summary>
/// Recurring-subscription billing operations against the billing system of record.
/// The customerReference argument is the stable key that links an eShopOnWeb user
/// to a billing customer; implementations must be idempotent on it.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<CustomerSubscription> SubscribeAsync(string customerReference, string email, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
