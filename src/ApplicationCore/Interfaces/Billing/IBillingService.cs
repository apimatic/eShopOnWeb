using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

/// <summary>
/// Abstraction over the recurring-billing provider (Maxio Advanced Billing). Keeps ApplicationCore
/// and PublicApi free of any provider-specific vocabulary; see Infrastructure for the implementation.
/// </summary>
public interface IBillingService
{
    Task<IReadOnlyList<BillingPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for <paramref name="customerReference"/> and enrolls it in the
    /// plan identified by <paramref name="planHandle"/>. Safe to call more than once for the same
    /// customer/plan pair: neither the customer nor an active subscription will be duplicated.
    /// </summary>
    Task<BillingSubscription> SubscribeAsync(string customerReference, string customerEmail, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subscriptions belonging to <paramref name="customerReference"/>, or an empty list if
    /// no billing customer has been created for them yet.
    /// </summary>
    Task<IReadOnlyList<BillingSubscription>> GetSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default);
}
