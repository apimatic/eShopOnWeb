using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Orchestrates the eShopOnWeb subscription-billing capability on top of <see cref="IMaxioApiClient"/>:
/// browsing plans, subscribing (ensuring a Maxio customer exists, then enrolling idempotently),
/// and listing a shopper's subscriptions.
/// </summary>
public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="userReference"/> and enrolls them
    /// in the plan identified by <paramref name="planHandle"/>. Idempotent: if the user
    /// already has a live subscription to that plan, it is returned rather than duplicated.
    /// </summary>
    Task<SubscriptionDto> SubscribeAsync(string userReference, string email, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);
}
