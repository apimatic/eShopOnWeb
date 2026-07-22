using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single, provider-agnostic seam between eShopOnWeb and whichever recurring-billing platform is in
/// use. Exactly one implementation talks to the provider; nothing else in the application does.
/// </summary>
/// <remarks>
/// Every method throws <see cref="Exceptions.BillingProviderException"/> when the provider rejects the
/// call or cannot be reached, and <see cref="Exceptions.BillingConfigurationException"/> when a configured
/// handle does not resolve. Money is expressed in whole currency units throughout.
/// </remarks>
public interface IBillingClient
{
    /// <summary>Lists the recurring plans available in the configured product family.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves a plan by its durable handle, or null when no such plan exists.</summary>
    Task<SubscriptionPlan?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the configured metered component on the product family. Returns null when the configured
    /// handle does not resolve; the returned definition reports whether it is genuinely of metered kind.
    /// </summary>
    Task<MeteredComponentDefinition?> GetMeteredComponentAsync(CancellationToken cancellationToken = default);

    /// <summary>Looks up a customer by the stable eShopOnWeb reference, or null when none exists yet.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Creates a provider-side customer record for an eShopOnWeb user.</summary>
    Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>Enrolls an existing customer in a plan.</summary>
    Task<CustomerSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription the given customer holds.</summary>
    Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Reads a single subscription, or null when the id is unknown.</summary>
    Task<CustomerSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Records consumption of a metered component against a subscription.</summary>
    Task<UsageRecord> RecordUsageAsync(int subscriptionId, int componentId, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's running unit balance for a component on a subscription.</summary>
    Task<int?> GetComponentUnitBalanceAsync(int subscriptionId, int componentId, CancellationToken cancellationToken = default);

    /// <summary>Sums every usage reported for a component since the given instant.</summary>
    Task<decimal> SumUsageSinceAsync(int subscriptionId, int componentId, DateTimeOffset? since, CancellationToken cancellationToken = default);

    /// <summary>Computes, without committing anything, what moving to another plan would cost right now.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default);

    /// <summary>Moves a subscription to another plan immediately, with proration.</summary>
    Task<CustomerSubscription> ChangePlanImmediatelyAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default);

    /// <summary>Schedules a plan change for the next renewal date; no proration applies.</summary>
    Task<CustomerSubscription> ChangePlanAtRenewalAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default);

    /// <summary>Places a subscription on hold.</summary>
    Task<CustomerSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Takes a held subscription off hold.</summary>
    Task<CustomerSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription with immediate effect.</summary>
    Task<CustomerSubscription> CancelSubscriptionImmediatelyAsync(int subscriptionId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Schedules a cancellation for the end of the current billing period.</summary>
    Task<CustomerSubscription> CancelSubscriptionAtPeriodEndAsync(int subscriptionId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Reactivates a cancelled or expired subscription.</summary>
    Task<CustomerSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
