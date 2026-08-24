using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing capability backed by the external billing system of record.
/// Customer identity is correlated by <c>customerReference</c>, the caller's application user id.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscription plans available for signup.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for <paramref name="customerReference"/> and enrolls them in
    /// the plan identified by <paramref name="productHandle"/>. Idempotent: if a live subscription to
    /// the same plan already exists, it is returned with <see cref="SubscribeResult.AlreadyExisted"/> set.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string customerReference, string email, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists all subscriptions the billing customer for <paramref name="customerReference"/> holds.</summary>
    Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
