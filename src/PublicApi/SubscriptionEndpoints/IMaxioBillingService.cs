using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The application-facing billing service. Keeps Maxio SDK types out of the
/// endpoint layer; endpoint signatures use only the DTOs in this namespace.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the plans available in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a Maxio customer exists for the given user (idempotent, keyed on the
    /// user id), then subscribes them to the plan identified by <paramref name="productHandle"/>.
    /// A repeated call for the same user + plan returns the existing subscription.
    /// </summary>
    Task<SubscriptionDto> SubscribeAsync(string userId, string email, string productHandle,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the user's subscriptions. Returns an empty list when the user has
    /// never been enrolled with the billing provider.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDto>> ListSubscriptionsForUserAsync(string userId,
        CancellationToken cancellationToken);
}
