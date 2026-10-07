using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A purchasable recurring plan in the billing system of record.
/// </summary>
public record BillingPlan(
    string Handle,
    string Name,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

/// <summary>
/// A customer record in the billing system of record.
/// </summary>
public record BillingCustomer(
    long Id,
    string Email,
    string Reference);

/// <summary>
/// A subscription in the billing system of record.
/// </summary>
public record BillingSubscription(
    long Id,
    long CustomerId,
    string State,
    string PlanHandle,
    string PlanName,
    long PlanPriceInCents,
    DateTime? NextBillingAt);

/// <summary>
/// Abstraction over the external subscription billing provider (Maxio
/// Advanced Billing). Implementations guarantee that customer creation is
/// idempotent for a given reference.
/// </summary>
public interface ISubscriptionBillingProvider
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<BillingSubscription> GetSubscriptionAsync(long subscriptionId, CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a billing customer exists for the given reference (idempotent),
    /// then creates a new subscription for the given plan handle.
    /// </summary>
    Task<BillingSubscription> SubscribeAsync(string customerReference, string email,
        string firstName, string lastName, string planHandle, CancellationToken cancellationToken);
}
