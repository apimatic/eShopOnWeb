using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing, which is the
/// billing system of record. The caller's identity is the eShopOnWeb username
/// (the value of the JWT <c>Name</c> claim); it is used as the deterministic
/// reference key for the corresponding Maxio customer.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available for enrollment. Name and price are
    /// hydrated live from Maxio; plans absent from the configured site are flagged
    /// as unavailable rather than dropped.
    /// </summary>
    Task<Result<IReadOnlyList<SubscriptionPlan>>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the user (idempotently, keyed on the
    /// user id) and enrolls them on the plan identified by <paramref name="planHandle"/>.
    /// A repeat call for the same user and plan returns the existing subscription
    /// instead of creating a second one.
    /// </summary>
    Task<Result<SubscribeOutcome>> SubscribeAsync(string userId, string email, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the user's Maxio subscriptions. A user who has never subscribed has no
    /// Maxio customer yet; that yields an empty list, not an error.
    /// </summary>
    Task<Result<IReadOnlyList<SubscriptionSummary>>> ListUserSubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}