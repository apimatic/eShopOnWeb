using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Application-level operations for recurring subscriptions billed by Maxio Advanced Billing.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available for purchase (active Maxio products
    /// inside the configured product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently enrolls the user into the plan identified by <paramref name="productHandle"/>.
    /// Ensures a Maxio customer exists for the user (reference = user id), creates the
    /// subscription if it does not exist yet, and returns the subscription's authoritative state.
    /// </summary>
    Task<SubscriptionResult> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the calling user's subscriptions with live state from Maxio.
    /// </summary>
    Task<IReadOnlyList<SubscriptionResult>> ListForUserAsync(string userName, CancellationToken cancellationToken = default);
}