using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider-agnostic billing seam. Exactly one Infrastructure implementation talks to the billing
/// provider; nothing else in eShopOnWeb does. Every member normalizes provider results into
/// ApplicationCore types and reports failures as
/// <see cref="Exceptions.BillingProviderException"/> (or one of its subtypes), so no provider SDK type
/// ever crosses this boundary.
/// </summary>
public interface IBillingClient
{
    /// <summary>
    /// Lists the recurring plans available to shoppers, in the configured product family.
    /// Archived plans are excluded. Returns an empty collection when the family holds no plans.
    /// </summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a single plan by its durable handle.
    /// </summary>
    /// <exception cref="Exceptions.BillingConfigurationException">The handle does not resolve to a live plan.</exception>
    Task<SubscriptionPlan> GetPlanAsync(string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the configured metered component and verifies it is of metered kind on the configured
    /// product family. UC2 calls this before any usage is recorded.
    /// </summary>
    /// <exception cref="Exceptions.BillingConfigurationException">
    /// The handle does not resolve, or resolves to a component that is not metered.
    /// </exception>
    Task<MeteredComponent> GetMeteredComponentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the provider-side customer for this eShopOnWeb user, creating it if it does not exist.
    /// Idempotent on <paramref name="reference"/>: calling it repeatedly never creates a second customer.
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(string reference,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the customer identified by <paramref name="customerReference"/> in the plan identified by
    /// <paramref name="planHandle"/>. No payment method is captured.
    /// </summary>
    Task<CustomerSubscription> CreateSubscriptionAsync(string customerReference,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription belonging to the customer with this reference. Returns an empty
    /// collection when the customer does not exist provider-side or holds no subscriptions.
    /// </summary>
    Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(string customerReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single subscription, or returns null when no subscription has that id.
    /// </summary>
    Task<CustomerSubscription?> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records metered usage against the subscription's configured component.
    /// </summary>
    Task<UsageReceipt> RecordUsageAsync(int subscriptionId,
        int quantity,
        string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the running period-to-date billable unit balance for the subscription's metered component,
    /// or null when the subscription carries no balance for it.
    /// </summary>
    Task<int?> GetPeriodToDateUsageAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes what moving the subscription to <paramref name="targetPlanHandle"/> would cost, without
    /// committing anything.
    /// </summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the plan change with the requested timing and returns the updated subscription.
    /// </summary>
    Task<CustomerSubscription> ChangePlanAsync(int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>Places the subscription on hold indefinitely.</summary>
    Task<CustomerSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Resumes a subscription that is on hold.</summary>
    Task<CustomerSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the subscription immediately, or schedules cancellation for the end of the current
    /// billing period.
    /// </summary>
    Task<CustomerSubscription> CancelSubscriptionAsync(int subscriptionId,
        CancellationTiming timing,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reactivates a cancelled subscription. Any pending end-of-period cancellation is revoked first,
    /// so this is also how a subscription escapes a scheduled cancellation.
    /// </summary>
    Task<CustomerSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
