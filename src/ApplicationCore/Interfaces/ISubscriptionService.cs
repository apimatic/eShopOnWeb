using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Enrolls eShopOnWeb shoppers in recurring plans held by the billing system of record.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Ensures a billing customer exists for <paramref name="subscriber"/> and enrolls them in
    /// <paramref name="planHandle"/>.
    /// </summary>
    /// <param name="idempotencyKey">
    /// Optional caller-supplied key that collapses retries of the same logical request. When omitted a
    /// deterministic key derived from the subscriber and plan is used instead.
    /// </param>
    /// <remarks>
    /// The operation is idempotent: repeating it for a shopper who already holds a live subscription to
    /// the plan returns that subscription instead of creating a second one.
    /// </remarks>
    Task<SubscribeResult> SubscribeAsync(
        Subscriber subscriber,
        string? planHandle,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription held by <paramref name="subscriber"/>, newest first. Returns an empty
    /// list when the shopper has never been enrolled.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        Subscriber subscriber,
        CancellationToken cancellationToken = default);
}
