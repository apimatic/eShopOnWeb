using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Fronts Maxio Advanced Billing for the eShopOnWeb subscription-billing capability.
/// </summary>
public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="customerReference"/> (idempotent - a
    /// repeated call never creates a second customer) and enrolls it on <paramref name="planHandle"/>.
    /// Repeating the call for a customer that already has a live subscription to the same plan
    /// returns that existing subscription instead of creating a duplicate one.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(
        string customerReference,
        string email,
        string firstName,
        string lastName,
        string planHandle,
        CancellationToken ct = default);

    /// <summary>
    /// Lists the subscriptions for the Maxio customer identified by <paramref name="customerReference"/>.
    /// Returns an empty list if no Maxio customer has been created for this reference yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(
        string customerReference,
        CancellationToken ct = default);
}
