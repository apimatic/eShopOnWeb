using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the eShop subscription capability on top of Maxio Advanced Billing, which is the
/// billing system of record. Enforces idempotency so a repeated "subscribe" (e.g. a double-click)
/// never produces a duplicate customer or duplicate subscription.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>Plans available for subscription, scoped to the given Product Family handle.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(string productFamilyHandle, CancellationToken ct = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="customer"/> (keyed by
    /// <see cref="MaxioCustomerInput.Reference"/>), then enrolls that customer in
    /// <paramref name="planHandle"/>. Returns the active/existing subscription.
    /// </summary>
    Task<Subscription> SubscribeAsync(MaxioCustomerInput customer, string planHandle, CancellationToken ct = default);

    /// <summary>Subscriptions owned by the customer identified by <paramref name="customerReference"/>. Empty if none.</summary>
    Task<IReadOnlyList<Subscription>> GetSubscriptionsForCustomerAsync(string customerReference, CancellationToken ct = default);
}
