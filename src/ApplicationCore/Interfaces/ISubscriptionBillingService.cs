using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, abstracted over the billing system of record
/// (Maxio Advanced Billing). Lives in ApplicationCore so endpoints depend on the capability,
/// not on a specific vendor SDK; the implementation lives in Infrastructure.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper can subscribe to (the configured product family).</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="subscriber"/> in the plan identified by <paramref name="planHandle"/>.
    /// Idempotent: ensures a single Maxio customer exists for the user and does not create a
    /// second subscription when a live one to the same plan already exists (so a double-click is safe).
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions belonging to <paramref name="subscriber"/>.</summary>
    Task<IReadOnlyCollection<SubscriptionSummary>> ListSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
