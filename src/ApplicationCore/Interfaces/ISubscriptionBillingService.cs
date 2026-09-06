using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, with the billing system as the system of record. eShopOnWeb stores
/// no subscription state of its own — every read here goes to the provider.
/// </summary>
/// <remarks>
/// Every member throws <see cref="Exceptions.BillingException"/> and nothing else: implementations are
/// responsible for translating provider exceptions, transport failures and unreadable payloads at
/// their own boundary.
/// </remarks>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans sellable from the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists the add-on components offered alongside those plans.</summary>
    Task<IReadOnlyList<PlanComponent>> GetPlanComponentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrols <paramref name="subscriber"/> on <paramref name="planHandle"/>, creating the billing
    /// customer first if this is their first subscription.
    /// </summary>
    /// <remarks>
    /// Idempotent: repeating the call returns the existing enrolment
    /// (<see cref="SubscribeToPlanResult.AlreadySubscribed"/>) instead of creating a second one.
    /// </remarks>
    Task<SubscribeToPlanResult> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to <paramref name="subscriber"/>, newest first. Returns an
    /// empty list — not an error — when the subscriber has no billing customer yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
