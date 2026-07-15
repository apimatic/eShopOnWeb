using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-agnostic seam onto the billing provider. eShopOnWeb's domain never references the
/// billing SDK directly; the single Infrastructure implementation of this interface is the only
/// place that does.
/// </summary>
public interface IBillingClient
{
    /// <summary>
    /// Confirms the configured product family/products/metered component resolve on the provider
    /// and are of the expected shape (in particular, that the metered component is of metered kind).
    /// Throws <see cref="Exceptions.BillingProviderException"/> when they do not.
    /// </summary>
    Task EnsureConfigurationValidAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the billing-provider customer for <paramref name="customerReference"/>, creating one
    /// if it does not already exist. Idempotent: safe to call repeatedly for the same reference.
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(string customerReference, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the billing-provider customer for <paramref name="customerReference"/> without creating
    /// one; returns null when no customer exists yet for that reference.
    /// </summary>
    Task<BillingCustomer?> FindCustomerByReferenceAsync(string customerReference, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CreateSubscriptionAsync(int billingCustomerId, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BillingSubscription>> ListCustomerSubscriptionsAsync(int billingCustomerId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>Current period-to-date running total for the configured metered component.</summary>
    Task<int> GetMeteredUsageBalanceAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Previews the prorated charge/credit of migrating to <paramref name="targetPlanHandle"/> immediately.</summary>
    Task<BillingPlanChangePreview> PreviewPlanChangeNowAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default);

    /// <summary>Commits an immediate, prorated plan change.</summary>
    Task<BillingSubscription> CommitPlanChangeNowAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default);

    /// <summary>Schedules a plan change to take effect at the next renewal, at the new plan's full price (no proration).</summary>
    Task<BillingSubscription> SchedulePlanChangeAtRenewalAsync(int subscriptionId, string targetPlanHandle, CancellationToken cancellationToken = default);

    Task<BillingSubscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<BillingSubscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, CancellationToken cancellationToken = default);

    Task<BillingSubscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
