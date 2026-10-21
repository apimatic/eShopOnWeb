using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing for a shopper. The billing system is the system of record; this
/// application keeps only its own claim of each signup.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>The plans this shop offers (the configured product family, archived plans excluded).</summary>
    Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the user to the plan, at most once: the billing customer is found or created, a
    /// subscription the user already holds on the plan is returned instead of a second one, and an
    /// outcome that cannot be settled yet is reported as pending, never as a failure.
    /// </summary>
    /// <exception cref="Exceptions.SubscriptionBillingException">The request cannot be served.</exception>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>The user's subscriptions in the billing system, plus signups of ours that are not settled yet.</summary>
    Task<CustomerSubscriptionList> ListSubscriptionsAsync(SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
