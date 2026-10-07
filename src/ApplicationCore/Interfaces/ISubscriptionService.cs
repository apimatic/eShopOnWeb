using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing (the
/// billing system of record). The user identity is the eShopOnWeb username
/// (the JWT's name claim); it is used as the stable Maxio customer reference
/// so that a user's billing records survive application restarts.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscribable plans in the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanInfo>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Idempotently ensures a Maxio customer exists for the user, then
    /// subscribes them to the plan with the given product handle. A repeated
    /// call for the same user + plan returns the existing subscription rather
    /// than creating a second one.
    /// </summary>
    Task<SubscriptionInfo> SubscribeAsync(string username, string planHandle, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the user's Maxio subscriptions (all states). Returns an empty
    /// list when the user has never enrolled.
    /// </summary>
    Task<IReadOnlyList<SubscriptionInfo>> ListSubscriptionsForUserAsync(string username, CancellationToken cancellationToken);
}
