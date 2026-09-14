using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application-facing abstraction over the recurring-subscription billing system
/// (Maxio Advanced Billing). Keeps the domain and API layers free of any Maxio wire
/// concerns; the concrete implementation lives in the Infrastructure layer.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Returns the subscription plans a shopper can subscribe to, sourced from the
    /// configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls an eShopOnWeb user into a plan. Ensures a Maxio customer exists for the
    /// user (idempotent by reference) and creates the subscription. Safe to call more
    /// than once (e.g. a double-click) without producing duplicate customers or
    /// subscriptions for the same plan.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subscriptions currently on file for the given user reference.
    /// If no Maxio customer has been created yet for the user, an empty list is returned.
    /// </summary>
    Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default);
}
