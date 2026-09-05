using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Fronts the Maxio Advanced Billing SDK for eShopOnWeb's recurring-subscription capability.
/// </summary>
public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanModel>> ListPlansAsync(CancellationToken ct);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="customerReference"/> and enrolls it in
    /// <paramref name="planHandle"/>. Idempotent: a repeat call for a customer already subscribed to
    /// that plan throws <see cref="Exceptions.DuplicateException"/> rather than creating a second
    /// subscription.
    /// </summary>
    Task<CustomerSubscriptionModel> SubscribeAsync(
        string customerReference,
        string customerEmail,
        string customerFirstName,
        string customerLastName,
        string planHandle,
        CancellationToken ct);

    Task<IReadOnlyList<CustomerSubscriptionModel>> ListSubscriptionsForCustomerAsync(string customerReference, CancellationToken ct);
}
