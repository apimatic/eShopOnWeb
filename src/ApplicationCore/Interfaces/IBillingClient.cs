using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single, provider-agnostic seam onto the recurring-billing provider. Nothing outside the
/// Infrastructure implementation talks to the provider directly (plan.md §2.2).
/// Every method throws <see cref="Exceptions.BillingProviderException"/> when the provider fails.
/// </summary>
public interface IBillingClient
{
    /// <summary>
    /// Lists the recurring plans available on the configured product family. Returns an empty
    /// collection when the family holds no plans.
    /// </summary>
    Task<IReadOnlyCollection<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a plan by its stable handle, or <c>null</c> when no such plan exists.
    /// </summary>
    Task<BillingPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the configured metered component by handle, or <c>null</c> when it does not
    /// exist on the product family.
    /// </summary>
    Task<MeteredComponent?> FindMeteredComponentAsync(string componentHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider's customer for this eShopOnWeb user, creating it only if it does not
    /// already exist. Idempotent on <paramref name="customerReference"/>.
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(string customerReference, string email, string firstName,
        string lastName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription belonging to the given customer reference. Returns an empty
    /// collection when the customer is unknown or has none.
    /// </summary>
    Task<IReadOnlyCollection<Subscription>> ListSubscriptionsAsync(string customerReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single subscription, or <c>null</c> when the id is unknown.
    /// </summary>
    Task<Subscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrols an existing customer in a plan.
    /// </summary>
    Task<Subscription> CreateSubscriptionAsync(int customerId, string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records metered consumption against a subscription's component.
    /// </summary>
    Task<UsageRecord> RecordUsageAsync(int subscriptionId, string componentHandle, decimal quantity,
        string? memo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the period-to-date unit balance for a subscription's component, or <c>null</c> when
    /// the component is not present on the subscription.
    /// </summary>
    Task<decimal?> GetUsageBalanceAsync(int subscriptionId, string componentHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the provider what moving to <paramref name="targetPlanHandle"/> would cost, without
    /// committing anything.
    /// </summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a plan change with the chosen timing.
    /// </summary>
    Task<Subscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Temporarily stops billing, optionally scheduling an automatic resumption.
    /// </summary>
    Task<Subscription> PauseAsync(int subscriptionId, DateTimeOffset? automaticallyResumeAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a paused subscription's billing period.
    /// </summary>
    Task<Subscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a subscription, either immediately or at the end of the current billing period.
    /// </summary>
    Task<Subscription> CancelAsync(int subscriptionId, bool endOfPeriod, string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restarts a cancelled subscription.
    /// </summary>
    Task<Subscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
