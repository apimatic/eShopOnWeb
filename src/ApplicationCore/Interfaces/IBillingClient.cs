using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single, provider-agnostic seam between eShopOnWeb and whichever recurring-billing provider
/// is in use. Exactly one implementation talks to the provider; nothing else in the application
/// does (plan.md §2.2).
/// <para>
/// Every member reports failure as
/// <see cref="Exceptions.BillingProviderException"/> (the provider refused or was unreachable) or
/// <see cref="Exceptions.BillingConfigurationException"/> (the provider's catalog does not match
/// configuration). No provider SDK type crosses this boundary, and all money is in whole currency
/// units rather than cents.
/// </para>
/// </summary>
public interface IBillingClient
{
    /// <summary>Lists the plans available in the configured product family, cheapest first.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves a configured plan handle to the live plan, or null when the handle does not resolve.</summary>
    Task<SubscriptionPlan?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the provider's catalog against configuration: the family resolves, the configured plan
    /// handles resolve, and the configured usage component exists and is metered. Never throws.
    /// </summary>
    Task<BillingCatalogValidation> ValidateCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds the provider customer for an eShopOnWeb user reference, or null if there is none yet.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider customer for an eShopOnWeb user, creating it if it does not exist.
    /// Idempotent on <paramref name="userReference"/>: calling it repeatedly yields the same customer.
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(string userReference, string email, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription held by a provider customer, newest state included.</summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Reads one subscription, or null when no subscription with that id exists.</summary>
    Task<CustomerSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Enrols an existing provider customer in a plan.</summary>
    Task<CustomerSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Records metered usage against the configured usage component on a subscription.</summary>
    Task<UsageRecord> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sums the usage recorded against the configured component since <paramref name="since"/>,
    /// following pagination to completion so the total is never silently short.
    /// </summary>
    Task<decimal> GetUsageTotalAsync(int subscriptionId, DateTimeOffset? since, CancellationToken cancellationToken = default);

    /// <summary>The per-unit price of the configured usage component, in whole currency units.</summary>
    Task<decimal?> GetUsageUnitPriceAsync(CancellationToken cancellationToken = default);

    /// <summary>Prices a plan change without committing it.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>Commits a plan change at the requested timing and returns the updated subscription.</summary>
    Task<CustomerSubscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>Pauses a subscription, optionally scheduling an automatic resumption.</summary>
    Task<CustomerSubscription> PauseSubscriptionAsync(int subscriptionId, DateTimeOffset? automaticallyResumeAt, CancellationToken cancellationToken = default);

    /// <summary>Resumes a paused subscription.</summary>
    Task<CustomerSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription immediately, or at the end of the current period.</summary>
    Task<CustomerSubscription> CancelSubscriptionAsync(int subscriptionId, CancellationTiming timing, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Reactivates a cancelled subscription.</summary>
    Task<CustomerSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
