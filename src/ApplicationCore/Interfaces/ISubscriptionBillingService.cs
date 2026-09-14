using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing. Maxio is the system of
/// record: no local copy of customer/subscription state is kept, so every call reflects
/// Maxio's live state and every operation is safe to retry (e.g. a double-click "Subscribe").
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans (Maxio products) available for subscription in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="customerReference"/> and enrolls them in
    /// the plan identified by <paramref name="planHandle"/>. If the customer already has a live
    /// (non-canceled, non-expired) subscription to that plan, that subscription is returned instead
    /// of creating a duplicate.
    /// </summary>
    /// <param name="customerReference">A stable, unique identifier for the shopper (their eShopOnWeb username/email).</param>
    /// <param name="customerEmail">The shopper's email, used only if a new Maxio customer needs to be created.</param>
    /// <param name="planHandle">The handle of the plan (Maxio product) to subscribe to.</param>
    Task<SubscriptionEnrollmentResult> SubscribeAsync(string customerReference, string customerEmail, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription (any state) belonging to the Maxio customer identified by
    /// <paramref name="customerReference"/>. Returns an empty list if no Maxio customer exists yet
    /// for that reference (i.e. the shopper has never subscribed).
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default);
}
