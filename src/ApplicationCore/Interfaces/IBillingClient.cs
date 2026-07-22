using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single seam onto the recurring-billing provider (plan.md §2.2). Nothing outside the one
/// Infrastructure implementation talks to the provider, and nothing in this contract names one:
/// implementations translate provider vocabulary into the SubscriptionAggregate types and surface
/// every failure as a
/// <see cref="Exceptions.BillingProviderException"/>.
/// </summary>
public interface IBillingClient
{
    /// <summary>
    /// The handle of the metered component this integration bills pay-as-you-go usage against
    /// (UC2). It is provider configuration, so it is surfaced here rather than duplicated into
    /// ApplicationCore.
    /// </summary>
    string MeteredComponentHandle { get; }

    /// <summary>Lists the plans available to subscribe to (UC1 step 1).</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves a plan by its durable handle. Returns null when the handle does not exist.</summary>
    Task<SubscriptionPlan?> GetPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Resolves a billable component by its durable handle. Returns null when it does not exist.</summary>
    Task<MeteredComponent?> GetComponentByHandleAsync(string componentHandle, CancellationToken cancellationToken = default);

    /// <summary>Finds the provider's customer for an eShopOnWeb user reference. Returns null when absent.</summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider's customer for an eShopOnWeb user, creating it if needed. Idempotent on
    /// <paramref name="customerReference"/>, so retrying a failed subscribe is safe (UC1).
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(string customerReference,
        string email,
        string? firstName,
        string? lastName,
        CancellationToken cancellationToken = default);

    /// <summary>Enrols a customer in a plan (UC1 step 4).</summary>
    Task<Subscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to a provider customer.</summary>
    Task<IReadOnlyCollection<Subscription>> ListSubscriptionsForCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>Reads a single subscription. Returns null when the id is unknown.</summary>
    Task<Subscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Records metered consumption against a subscription's component (UC2 step 2).</summary>
    Task<UsageRecord> RecordUsageAsync(int subscriptionId,
        string componentHandle,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>Lists usage recorded against a component, optionally from a given date (UC2 step 3).</summary>
    Task<IReadOnlyCollection<UsageRecord>> ListUsageAsync(int subscriptionId,
        string componentHandle,
        DateTimeOffset? since,
        CancellationToken cancellationToken = default);

    /// <summary>Computes the prorated cost of moving to another plan, without committing (UC3 step 2).</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default);

    /// <summary>Commits a plan change with the chosen timing (UC3 step 4).</summary>
    Task<Subscription> ChangePlanAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>Puts a subscription on hold, optionally scheduling an automatic resume (UC4).</summary>
    Task<Subscription> PauseAsync(int subscriptionId, DateTimeOffset? automaticallyResumeAt, CancellationToken cancellationToken = default);

    /// <summary>Resumes a held subscription (UC4).</summary>
    Task<Subscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription now or at the end of the current period (UC4).</summary>
    Task<Subscription> CancelAsync(int subscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>Reactivates a cancelled or expired subscription (UC4).</summary>
    Task<Subscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
