using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken ct = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="userReference"/> (idempotent - a repeat call
    /// never creates a second customer) and enrolls it in <paramref name="planHandle"/>. A repeat call for
    /// a plan the user is already actively subscribed to returns the existing subscription rather than
    /// creating a second one.
    /// </summary>
    Task<UserSubscription> SubscribeAsync(string userReference, string email, string planHandle, CancellationToken ct = default);

    /// <summary>
    /// Lists the subscriptions for the Maxio customer tied to <paramref name="userReference"/>, or an
    /// empty list if that user has never subscribed to anything.
    /// </summary>
    Task<IReadOnlyList<UserSubscription>> ListSubscriptionsAsync(string userReference, CancellationToken ct = default);
}
