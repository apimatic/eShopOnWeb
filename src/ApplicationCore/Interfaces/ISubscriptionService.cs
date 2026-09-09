using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing with Maxio Advanced Billing as the system of record.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available in the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the user (idempotently, keyed on the user id) and
    /// enrolls them in the plan identified by <paramref name="productHandle"/>.
    /// Returns null when the plan handle is unknown.
    /// </summary>
    Task<SubscriptionSummary?> SubscribeAsync(string userId, string userName, string email, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the user's subscriptions, reconciled with their live Maxio state.
    /// </summary>
    Task<IReadOnlyList<SubscriptionSummary>> ListForUserAsync(string userId, CancellationToken cancellationToken = default);
}
