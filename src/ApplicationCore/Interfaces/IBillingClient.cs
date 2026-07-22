using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider-agnostic seam onto the recurring-billing provider. Exactly one implementation
/// talks to the provider; nothing else in the application does. Every failure reaching the
/// provider is surfaced as a
/// <see cref="Exceptions.BillingProviderException"/>, and every configuration mismatch as a
/// <see cref="Exceptions.BillingConfigurationException"/>.
/// </summary>
public interface IBillingClient
{
    /// <summary>The handle of the plan this deployment offers by default.</summary>
    string DefaultPlanHandle { get; }

    /// <summary>
    /// The handle of the component this deployment bills pay-as-you-go usage against (UC2).
    /// </summary>
    string MeteredComponentHandle { get; }

    /// <summary>Lists the recurring plans available to subscribe to.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads a single plan by its durable handle, or <c>null</c> when it does not resolve.</summary>
    Task<SubscriptionPlan?> GetPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider's customer record for <paramref name="userReference"/>, creating it
    /// if it does not exist. Idempotent on the reference, so a repeated subscribe never creates
    /// a second customer.
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(string userReference, string email, string? firstName,
        string? lastName, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription held by a provider customer.</summary>
    Task<IReadOnlyCollection<Subscription>> ListSubscriptionsForCustomerAsync(int providerCustomerId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a single subscription, or <c>null</c> when the id is unknown.</summary>
    Task<Subscription?> GetSubscriptionAsync(int providerSubscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>Enrols a customer in a plan.</summary>
    Task<Subscription> CreateSubscriptionAsync(int providerCustomerId, string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the pay-as-you-go component by handle, or <c>null</c> when it does not resolve on
    /// the configured product family.
    /// </summary>
    Task<MeteredComponent?> GetComponentByHandleAsync(string componentHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Records consumption against a subscription's metered component.</summary>
    Task<UsageRecord> RecordUsageAsync(int providerSubscriptionId, string componentHandle,
        decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the units accrued so far in the current billing period, or <c>null</c> when the
    /// component is not present on the subscription.
    /// </summary>
    Task<decimal?> GetPeriodToDateUsageAsync(int providerSubscriptionId, string componentHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Quotes the cost of moving a subscription to another plan, without committing it.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int providerSubscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>Commits a plan change at the requested timing.</summary>
    Task<Subscription> ChangePlanAsync(int providerSubscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>Puts a subscription on hold, optionally scheduling an automatic resumption.</summary>
    Task<Subscription> PauseAsync(int providerSubscriptionId, DateTimeOffset? automaticallyResumeAt,
        CancellationToken cancellationToken = default);

    /// <summary>Takes a subscription off hold.</summary>
    Task<Subscription> ResumeAsync(int providerSubscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription, either immediately or at the end of the current period.</summary>
    Task<Subscription> CancelAsync(int providerSubscriptionId, CancellationTiming timing, string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>Reactivates a cancelled or expired subscription.</summary>
    Task<Subscription> ReactivateAsync(int providerSubscriptionId, CancellationToken cancellationToken = default);
}
