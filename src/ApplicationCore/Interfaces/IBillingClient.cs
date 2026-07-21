using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-agnostic seam onto the recurring-billing provider. This is the single interface
/// ApplicationCore is allowed to depend on for billing; the one concrete implementation (Maxio)
/// lives in Infrastructure. No Maxio SDK type may appear on this interface.
/// </summary>
public interface IBillingClient
{
    /// <summary>Lists the recurring plans available for subscription.</summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves and validates the configured metered component (must be of "metered" kind).</summary>
    Task<BillingComponentInfo> GetMeteredComponentAsync(CancellationToken cancellationToken = default);

    /// <summary>Idempotently ensures a provider-side customer record exists for this eShopOnWeb user.</summary>
    Task EnsureCustomerAsync(string customerReference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription (any state) belonging to the customer identified by <paramref name="customerReference"/>; empty if the customer does not exist yet.</summary>
    Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>Enrolls an existing customer in the named plan.</summary>
    Task<Subscription> CreateSubscriptionAsync(string customerReference, string planHandle, CancellationToken cancellationToken = default);

    Task<Subscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Records usage against the subscription's metered component and reads back the period-to-date total.</summary>
    Task<UsageRecordResult> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, bool applyNow, CancellationToken cancellationToken = default);

    Task<Subscription> CommitPlanChangeAsync(int subscriptionId, string targetPlanHandle, bool applyNow, CancellationToken cancellationToken = default);

    Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default);

    Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
