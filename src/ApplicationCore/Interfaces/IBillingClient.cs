using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single, provider-agnostic seam onto the recurring-billing provider. Exactly one implementation
/// lives in Infrastructure; nothing else in the application talks to the provider directly.
/// Implementations surface provider failures as
/// <see cref="Exceptions.BillingProviderException"/> and never leak provider SDK types.
/// </summary>
public interface IBillingClient
{
    /// <summary>
    /// Lists the plans available in the configured product family. Returns an empty list when the family
    /// holds no plans.
    /// </summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single plan by its stable handle, or <c>null</c> when no such plan exists.
    /// </summary>
    Task<BillingPlan?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the provider customer carrying <paramref name="reference"/>, or <c>null</c> when there is none.
    /// </summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a provider customer. Callers make this idempotent by looking the reference up first.
    /// </summary>
    Task<BillingCustomer> CreateCustomerAsync(NewBillingCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls an existing customer in the plan identified by <paramref name="planHandle"/>.
    /// </summary>
    Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription belonging to a customer. Returns an empty list when there are none.
    /// </summary>
    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a subscription by id, or <c>null</c> when the provider does not know it.
    /// </summary>
    Task<BillingSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the configured metered component from the product family, or <c>null</c> when the handle does
    /// not resolve. The returned component reports whether it is actually of metered kind.
    /// </summary>
    Task<BillingComponent?> FindMeteredComponentAsync(string componentHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records metered usage against a subscription's component.
    /// </summary>
    Task<UsageReceipt> RecordUsageAsync(int subscriptionId, string componentHandle, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the accumulated period-to-date unit balance for a component on a subscription, or <c>null</c>
    /// when the provider does not report one.
    /// </summary>
    Task<decimal?> GetComponentUnitBalanceAsync(int subscriptionId, int componentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the prorated cost of moving a subscription to <paramref name="targetPlanHandle"/> without
    /// committing anything.
    /// </summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a subscription to another plan immediately, prorating the difference.
    /// </summary>
    Task<BillingSubscription> ChangePlanNowAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules a plan change for the next renewal. No proration applies.
    /// </summary>
    Task<BillingSubscription> ChangePlanAtRenewalAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default);

    /// <summary>Places a subscription on hold.</summary>
    Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Resumes a subscription that is on hold.</summary>
    Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription straight away.</summary>
    Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Schedules cancellation for the end of the current billing period.</summary>
    Task<BillingSubscription> CancelSubscriptionAtEndOfPeriodAsync(int subscriptionId, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Reactivates a cancelled subscription.</summary>
    Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
