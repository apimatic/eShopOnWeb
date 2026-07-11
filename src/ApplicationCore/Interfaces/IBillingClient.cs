using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single, provider-agnostic seam between eShopOnWeb and its recurring-billing provider. Every outbound
/// call to the billing provider goes through this interface; the concrete implementation (Infrastructure)
/// is the only place that ever touches the provider's SDK.
/// </summary>
public interface IBillingClient
{
    /// <summary>Lists the recurring plans available for the configured product family (UC1 step 1).</summary>
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a provider-side customer exists for this eShopOnWeb user, idempotent on <paramref name="customerReference"/>.
    /// </summary>
    Task<BillingCustomer> EnsureCustomerAsync(string customerReference, string email, string firstName, string lastName, CancellationToken cancellationToken = default);

    /// <summary>Finds the customer's current subscription (any non-purged state), or null if they have none.</summary>
    Task<Subscription?> FindSubscriptionByCustomerReferenceAsync(string customerReference, CancellationToken cancellationToken = default);

    Task<Subscription> CreateSubscriptionAsync(string customerReference, string productHandle, CancellationToken cancellationToken = default);

    Task<Subscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Verifies the configured metered component handle resolves to a component of metered kind (UC2 precondition).</summary>
    Task<bool> IsMeteredComponentConfiguredCorrectlyAsync(CancellationToken cancellationToken = default);

    Task<UsageRecord> RecordUsageAsync(int subscriptionId, double quantity, string? memo, CancellationToken cancellationToken = default);

    Task<UsagePeriodSummary> GetUsagePeriodToDateAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool immediate, CancellationToken cancellationToken = default);

    Task<Subscription> CommitPlanChangeAsync(int subscriptionId, string targetProductHandle, bool immediate, CancellationToken cancellationToken = default);

    Task<Subscription> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default);

    Task<Subscription> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
