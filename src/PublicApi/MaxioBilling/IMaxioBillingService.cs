using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

/// <summary>
/// The eShopOnWeb user a billing operation acts on, as resolved from the
/// caller's authenticated identity.
/// </summary>
public sealed record UserBillingIdentity(string UserId, string Email, string FirstName, string LastName);

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing (system of record).
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the subscription plans (Maxio products in the configured product
    /// family) available to shoppers.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a Maxio customer exists for the user (idempotent, keyed by a
    /// reference derived from the user id) and subscribes them to the plan
    /// with the given handle (idempotent per user+plan, so a double-click
    /// never creates two subscriptions).
    /// </summary>
    Task<SubscriptionDto> SubscribeAsync(UserBillingIdentity user, string planHandle, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the Maxio subscriptions that belong to the user's Maxio
    /// customer. Returns an empty list when the user has no Maxio customer.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsForUserAsync(UserBillingIdentity user, CancellationToken cancellationToken);
}
