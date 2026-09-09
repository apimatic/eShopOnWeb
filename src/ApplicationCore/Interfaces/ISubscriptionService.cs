using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provides recurring-subscription billing against the billing system of record
/// (Maxio Advanced Billing). All operations are idempotent per eShopOnWeb user.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans (products) available in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing-system customer exists for the given application user (idempotently,
    /// keyed by the user's id), then enrolls them in the plan identified by <paramref name="productHandle"/>.
    /// If the user already holds a live subscription to that plan, the existing subscription is returned.
    /// </summary>
    Task<SubscriptionEnrollment> SubscribeAsync(string userId, string email, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions the user holds in the billing system.
    /// </summary>
    Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsForUserAsync(string userId, string email, CancellationToken cancellationToken = default);
}
