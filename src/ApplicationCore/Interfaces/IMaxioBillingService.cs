using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing as the system of record.
/// This is an additive capability alongside the existing one-time commerce flow.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>Lists the plans a shopper can subscribe to (the configured product family's products).</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the user in a plan. Ensures a Maxio customer exists for the user (idempotent on the
    /// user reference) and does not create a second subscription when a live one already exists for
    /// the same plan, so a double-click never duplicates customers or subscriptions.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the user identified by <paramref name="userReference"/>.
    /// Returns an empty list when no Maxio customer exists yet for that user.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);
}
