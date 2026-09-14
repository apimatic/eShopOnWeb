using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing.
/// </summary>
public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the buyer and enrolls them in the given plan.
    /// Idempotent: calling this twice for the same buyer/plan returns the existing enrollment
    /// instead of creating a duplicate customer or subscription.
    /// </summary>
    Task<SubscriptionEnrollment> SubscribeAsync(string buyerId, string buyerEmail, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionEnrollment>> GetSubscriptionsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}
