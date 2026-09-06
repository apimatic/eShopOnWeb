using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by an external billing provider that is the system of record.
/// Runs alongside the one-time catalog/basket/order flow and shares no state with it.
/// </summary>
/// <remarks>
/// Every method throws <see cref="Exceptions.BillingException"/> — and only that — when the provider
/// cannot be reached, rejects the request, or answers unintelligibly.
/// </remarks>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans currently offered by the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="identity"/> in a plan, creating the provider-side customer if needed.
    /// </summary>
    /// <param name="identity">The eShopOnWeb user to enroll.</param>
    /// <param name="planHandle">
    /// Plan to subscribe to. When null the configured default plan is used, falling back to the
    /// product family's only plan when it has exactly one.
    /// </param>
    /// <remarks>
    /// Idempotent: repeating the call for the same user and plan returns the existing subscription
    /// instead of enrolling twice. <see cref="CustomerSubscription.WasCreatedByThisRequest"/> says which happened.
    /// </remarks>
    Task<CustomerSubscription> SubscribeAsync(
        BillingCustomerIdentity identity,
        string? planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions the provider holds for <paramref name="identity"/>. Returns an empty list
    /// when the user has never subscribed.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        BillingCustomerIdentity identity,
        CancellationToken cancellationToken = default);
}
