using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing. The billing provider is the system of record for plans,
/// customers and subscriptions; this application only keeps the claims that make enrolment idempotent.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans currently offered in the configured product family.</summary>
    Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a billing customer exists for <paramref name="subscriber"/> and enrols them in
    /// <paramref name="planHandle"/>. Safe to call repeatedly: a repeat for the same plan returns the
    /// existing subscription instead of creating another one.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriber's subscriptions in the configured product family.</summary>
    Task<SubscriptionDetailsList> ListSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);
}
