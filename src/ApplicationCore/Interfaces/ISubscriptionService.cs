using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface: everything the storefront and the public API can ask for.
/// Customer-scoped members take the eShopOnWeb user reference and can therefore only ever reach
/// that user's own subscriptions; the subscription-scoped members are the administrative surface.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the plans a customer can subscribe to (UC1, step 1).</summary>
    Task<IReadOnlyCollection<BillingPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the user in a plan (UC1). Idempotent: if the user already holds a live subscription
    /// it is returned unchanged instead of a second enrollment being created.
    /// </summary>
    Task<Subscription> SubscribeAsync(string userReference, string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription the user holds, in any state.</summary>
    Task<IReadOnlyCollection<Subscription>> GetSubscriptionsAsync(string userReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subscription the user's management actions apply to — a live one where possible —
    /// or <c>null</c> when the user has none.
    /// </summary>
    Task<Subscription?> GetCurrentSubscriptionAsync(string userReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the configured metered component and verifies it is of metered kind on the
    /// configured family. Throws <see cref="Exceptions.BillingConfigurationException"/> otherwise.
    /// </summary>
    Task<BillingComponent> GetMeteredComponentAsync(CancellationToken cancellationToken = default);

    /// <summary>Reports metered usage on the user's own subscription (UC2).</summary>
    Task<UsageReport> RecordUsageAsync(string userReference, decimal quantity, string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>Reports metered usage on any subscription. Administrative.</summary>
    Task<UsageReport> RecordUsageForSubscriptionAsync(int providerSubscriptionId, decimal quantity, string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the units already accrued in the current period on the user's subscription.</summary>
    Task<UsageReport?> GetUsageSummaryAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>Computes the cost of moving the user's subscription to another plan (UC3).</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(string userReference, string targetPlanHandle,
        PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a previewed plan change (UC3). <paramref name="previewFingerprint"/> is the
    /// <see cref="PlanChangePreview.Fingerprint"/> the customer confirmed; the change is rejected if
    /// the provider's numbers have moved since, so the amount charged always matches the one shown.
    /// </summary>
    Task<Subscription> ChangePlanAsync(string userReference, string targetPlanHandle, PlanChangeTiming timing,
        string previewFingerprint, CancellationToken cancellationToken = default);

    /// <summary>Applies a lifecycle transition to the user's own subscription (UC4).</summary>
    Task<Subscription> ExecuteLifecycleActionAsync(string userReference, SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Applies a lifecycle transition to any subscription (UC4). Administrative.</summary>
    Task<Subscription> ExecuteLifecycleActionForSubscriptionAsync(int providerSubscriptionId,
        SubscriptionLifecycleAction action, CancellationTiming cancellationTiming, string? reason,
        CancellationToken cancellationToken = default);
}
