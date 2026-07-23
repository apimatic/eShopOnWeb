using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider-agnostic seam between eShopOnWeb and the recurring-billing provider (plan.md §2.2).
/// </summary>
/// <remarks>
/// <para>
/// This is the <em>only</em> abstraction ApplicationCore knows about the billing provider. Exactly one
/// Infrastructure implementation exists, and nothing outside it talks to the provider directly.
/// </para>
/// <para>
/// Every implementation must translate transport, protocol and validation failures into
/// <see cref="Exceptions.BillingProviderException"/>, and unresolvable configuration into
/// <see cref="Exceptions.BillingConfigurationException"/>. Reads that legitimately find nothing return
/// <c>null</c> or an empty collection rather than throwing.
/// </para>
/// </remarks>
public interface IBillingClient
{
    /// <summary>Lists the recurring plans available in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a plan by its durable handle, or returns <c>null</c> when the handle does not resolve to a
    /// live plan in the configured product family.
    /// </summary>
    Task<SubscriptionPlan?> FindPlanAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the configured pay-as-you-go component and reports whether it is of metered kind
    /// (plan.md UC2 preconditions).
    /// </summary>
    Task<MeteredComponent> GetMeteredComponentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up the provider customer for an eShopOnWeb user, or returns <c>null</c> when none exists.
    /// </summary>
    Task<BillingCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider customer for an eShopOnWeb user, creating it if necessary. Idempotent on
    /// <see cref="BillingCustomerRegistration.Reference"/>.
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(
        BillingCustomerRegistration registration,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription belonging to a provider customer.</summary>
    Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(
        int customerId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one subscription, or returns <c>null</c> when the id is unknown.</summary>
    Task<Subscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Enrols an existing customer in a plan, identified by its durable handle.</summary>
    Task<Subscription> CreateSubscriptionAsync(
        int customerId,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Records metered usage against the configured pay-as-you-go component.</summary>
    Task<UsageRecord> RecordUsageAsync(
        int subscriptionId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the running period-to-date usage total, or returns <c>null</c> when the provider cannot
    /// supply it. Callers must treat <c>null</c> as "unavailable", never as zero.
    /// </summary>
    Task<decimal?> GetPeriodToDateUsageAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Computes the prorated cost of moving to another plan, without applying anything.</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(
        int subscriptionId,
        string targetPlanHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Moves the subscription to another plan now, charging or crediting the proration.</summary>
    Task<Subscription> ChangePlanImmediatelyAsync(
        int subscriptionId,
        string targetPlanHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Schedules a plan change for the next renewal; no proration is charged.</summary>
    Task<Subscription> SchedulePlanChangeAsync(
        int subscriptionId,
        string targetPlanHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Places the subscription on hold indefinitely.</summary>
    Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Resumes a subscription that is on hold.</summary>
    Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the subscription straight away.</summary>
    Task<Subscription> CancelSubscriptionAsync(
        int subscriptionId,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>Schedules cancellation for the end of the current billing period.</summary>
    Task<Subscription> CancelSubscriptionAtPeriodEndAsync(
        int subscriptionId,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>Reactivates a cancelled or expired subscription.</summary>
    Task<Subscription> ReactivateSubscriptionAsync(
        int subscriptionId,
        CancellationToken cancellationToken = default);
}
