using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Subscription-billing capability backed by Maxio Advanced Billing (the billing system of record).
/// Implementations are idempotent per (user, plan): repeated subscribe calls never create
/// duplicate Maxio customers or subscriptions.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the subscribable plans in the configured product family.
    /// </summary>
    Task<IReadOnlyList<MaxioPlanInfo>> ListPlansAsync(CancellationToken ct = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the user (idempotent by reference) and enrolls
    /// them in the given plan without a payment method. A repeated call for the same
    /// user + plan returns the existing subscription instead of creating a new one.
    /// </summary>
    Task<MaxioSubscriptionResult> SubscribeAsync(MaxioSubscriber subscriber, string productHandle, CancellationToken ct = default);

    /// <summary>
    /// Lists the user's subscriptions as recorded in Maxio; empty when the user has no Maxio customer.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscriptionInfo>> GetSubscriptionsForUserAsync(MaxioSubscriber subscriber, CancellationToken ct = default);
}
