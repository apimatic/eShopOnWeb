using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Domain service that turns an eShopOnWeb user into a Maxio subscriber. Maxio is the
/// system of record: subscriptions live there and are re-read from there on every call.
/// All operations are idempotent - a repeated call never creates a second customer or a
/// second subscription for the same plan.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the subscribable plans in the configured Maxio product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> GetPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the subscriptions the given user holds in Maxio (newest first). Users with
    /// no Maxio customer yet simply have none.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsAsync(ApplicationUser user, CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="user"/> and enrolls them on the
    /// plan identified by <paramref name="planHandle"/>. When an open subscription to that
    /// plan already exists it is returned instead of creating a new one.
    /// </summary>
    Task<MaxioSubscription> SubscribeAsync(ApplicationUser user, string planHandle, CancellationToken cancellationToken);
}
