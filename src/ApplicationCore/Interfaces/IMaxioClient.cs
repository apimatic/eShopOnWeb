using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Client for Maxio Advanced Billing, the system of record for subscription billing.
/// </summary>
public interface IMaxioClient
{
    /// <summary>
    /// Lists the subscribable plans in the configured product family.
    /// </summary>
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="customerReference"/> and enrolls it in
    /// <paramref name="planHandle"/>. Idempotent: repeated calls for the same customer/plan combination
    /// return the existing customer/subscription instead of creating duplicates.
    /// </summary>
    Task<MaxioSubscribeResult> SubscribeAsync(
        string customerReference,
        string email,
        string firstName,
        string lastName,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the customer identified by <paramref name="customerReference"/>.
    /// Returns an empty list when no Maxio customer exists yet for that reference.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForCustomerAsync(
        string customerReference,
        CancellationToken cancellationToken = default);
}
