using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Recurring-subscription billing, delegated to an external billing system of record.
/// This capability sits alongside the one-time Catalog/Basket/Order flow; it does not replace it.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the plans a shopper may subscribe to.</summary>
    /// <exception cref="BillingConfigurationException">The billing provider is not configured.</exception>
    /// <exception cref="BillingProviderException">The billing system could not be reached or answered unexpectedly.</exception>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the subscriber in a plan, creating the billing-system customer first if needed.
    /// The operation is idempotent: repeating it with the same subscriber, plan and idempotency key
    /// returns the existing subscription instead of creating a second one.
    /// </summary>
    /// <exception cref="BillingConfigurationException">The billing provider is not configured.</exception>
    /// <exception cref="PlanNotFoundException">The plan handle is not offered by the configured catalog.</exception>
    /// <exception cref="SubscriptionConflictException">A prior, no longer live subscription occupies the same idempotency scope.</exception>
    /// <exception cref="BillingValidationException">The billing system rejected the enrollment.</exception>
    /// <exception cref="BillingProviderException">The billing system could not be reached or answered unexpectedly.</exception>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription held by the subscriber, newest first. Returns an empty list when the
    /// subscriber has never been enrolled.
    /// </summary>
    /// <exception cref="BillingConfigurationException">The billing provider is not configured.</exception>
    /// <exception cref="BillingProviderException">The billing system could not be reached or answered unexpectedly.</exception>
    Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
