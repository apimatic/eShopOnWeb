using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Integrates with Maxio Advanced Billing (the system of record for recurring-subscription billing)
/// per the contract published in the maxio-spec OpenAPI document.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the active, subscribable plans in the site's configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="customerReference"/> and enrolls them in
    /// <paramref name="planHandle"/>. Idempotent: repeating the call with the same reference and plan
    /// returns the existing customer/subscription instead of creating duplicates.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(string customerReference, string customerEmail, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the customer identified by <paramref name="customerReference"/>.
    /// Returns an empty list if no Maxio customer has been created for that reference yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default);
}
