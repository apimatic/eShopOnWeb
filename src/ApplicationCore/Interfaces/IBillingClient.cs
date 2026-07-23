using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single, provider-agnostic seam between eShopOnWeb and whichever recurring-billing platform
/// runs the billing. Exactly one implementation touches the provider; nothing else in the
/// application talks to it directly.
/// </summary>
/// <remarks>
/// <para>
/// Every monetary value crossing this seam is expressed in decimal currency units (dollars).
/// Implementations are responsible for converting the provider's minor units (cents).
/// </para>
/// <para>
/// Provider failures surface as <see cref="Exceptions.BillingProviderException"/>; entities that are
/// configured but missing or of the wrong shape surface as
/// <see cref="Exceptions.BillingConfigurationException"/>. Implementations never leak the
/// provider's own SDK or transport types.
/// </para>
/// </remarks>
public interface IBillingClient
{
    /// <summary>
    /// Lists the recurring plans available to subscribe to, within the configured product family.
    /// </summary>
    /// <returns>The available plans; an empty list when the family holds none.</returns>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a single plan by its durable handle.
    /// </summary>
    /// <returns>The plan, or null when no plan with that handle exists.</returns>
    Task<BillingPlan?> FindPlanByHandleAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider customer for this eShopOnWeb user, creating one if it does not exist.
    /// Idempotent on <see cref="SubscriberIdentity.Reference"/>: calling it repeatedly for the same
    /// user yields the same provider customer and never a duplicate.
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the provider customer for a user reference without creating one.
    /// </summary>
    /// <returns>The customer, or null when the user has no provider record yet.</returns>
    Task<BillingCustomer?> FindCustomerAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription held by a user, in any state.
    /// </summary>
    /// <returns>The user's subscriptions; an empty list when they have none or no provider record.</returns>
    Task<IReadOnlyList<BillingSubscription>> ListSubscriptionsAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single subscription by its provider id.
    /// </summary>
    /// <returns>The subscription, or null when no subscription with that id exists.</returns>
    Task<BillingSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a customer in a plan.
    /// </summary>
    Task<BillingSubscription> CreateSubscriptionAsync(int customerId, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the configured metered component and verifies it is genuinely of metered kind.
    /// </summary>
    /// <remarks>
    /// Which component the integration meters against is provider configuration, so it is owned by
    /// the implementation rather than passed in by callers. This is the validation the pay-as-you-go
    /// use case runs before the first usage call.
    /// </remarks>
    /// <exception cref="Exceptions.BillingConfigurationException">
    /// The configured handle does not resolve, or resolves to a component that is not metered.
    /// </exception>
    Task<MeteredComponentInfo> GetMeteredComponentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records consumed units against a subscription's configured metered component and reads back
    /// the running period-to-date balance.
    /// </summary>
    /// <remarks>
    /// The read-back is best-effort: when the units were recorded but the balance could not be read,
    /// the result is returned with <see cref="UsageRecordResult.PeriodToDateUnits"/> null rather than
    /// failing an operation that already succeeded.
    /// </remarks>
    Task<UsageRecordResult> RecordUsageAsync(
        int subscriptionId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the running period-to-date unit balance for a subscription's configured metered component.
    /// </summary>
    /// <returns>The unit balance, or null when the subscription has no line item for the component.</returns>
    Task<int?> GetPeriodToDateUsageAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes what moving a subscription to another plan would cost, without committing anything.
    /// </summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a plan change at the requested timing.
    /// </summary>
    Task<BillingSubscription> ChangePlanAsync(
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>Suspends billing on a subscription.</summary>
    Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Returns a paused subscription to active billing.</summary>
    Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a subscription, immediately or at the end of the current billing period.
    /// </summary>
    Task<BillingSubscription> CancelSubscriptionAsync(
        int subscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a cancelled subscription to active billing.</summary>
    Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
