using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Enrolls eShopOnWeb users into recurring-billing plans backed by Maxio Advanced Billing.
/// This is additive to, and independent of, the existing Catalog/Basket/Order checkout flow.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="buyerId"/> and enrolls them in the given plan.
    /// Idempotent: repeated calls for the same buyer/plan return the buyer's existing customer and
    /// subscription instead of creating duplicates.
    /// </summary>
    Task<SubscriptionDto> SubscribeAsync(string buyerId, string email, string? firstName, string? lastName, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}
