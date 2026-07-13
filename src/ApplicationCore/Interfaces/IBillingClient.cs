using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-agnostic seam for recurring billing. The single concrete implementation (Maxio)
/// lives in Infrastructure, behind a typed HttpClient; ApplicationCore depends only on this
/// interface and never on the provider's SDK or HttpClient directly (plan.md §2.2).
/// </summary>
public interface IBillingClient
{
    /// <summary>Lists the recurring plans available for subscription (UC1 step 1).</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the configured metered-component handle resolves to a metered-kind component
    /// on the configured product family (UC2 precondition / startup validation).
    /// </summary>
    Task<bool> IsMeteredComponentConfiguredCorrectlyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the customer's current active/trialing subscription, or null if they have none.
    /// Used to make Subscribe idempotent (UC1).
    /// </summary>
    Task<SubscriptionDetails?> FindActiveSubscriptionAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the customer's most recent subscription regardless of state (active, trialing, on
    /// hold, past due, unpaid, trial ended, or canceled), or null if they have none. This is "my
    /// subscription" for UC2-UC4 default resolution - broader than
    /// <see cref="FindActiveSubscriptionAsync"/> because lifecycle actions (e.g. Resume, Reactivate)
    /// must be able to find a subscription that is not currently active.
    /// </summary>
    Task<SubscriptionDetails?> GetCurrentSubscriptionAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>Lists every subscription the customer has, regardless of state.</summary>
    Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a provider-side customer exists for this eShopOnWeb user (idempotent on
    /// <paramref name="customerReference"/>) and enrolls them in <paramref name="productHandle"/> (UC1).
    /// </summary>
    Task<SubscriptionDetails> CreateSubscriptionAsync(string customerReference, string customerEmail, string productHandle, CancellationToken cancellationToken = default);

    Task<SubscriptionDetails> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Records a unit of metered usage against the configured component (UC2).</summary>
    Task<ComponentUsageStatus> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>Reads back the period-to-date usage balance for the configured metered component (UC2).</summary>
    Task<ComponentUsageStatus> GetComponentUsageStatusAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Previews the cost of moving a subscription to a different plan (UC3). When
    /// <paramref name="applyImmediately"/> is true, this is the prorated charge/credit applied
    /// now; when false, it is the new plan price effective from the next period (no proration).
    /// </summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken cancellationToken = default);

    /// <summary>Commits a previously previewed plan change, with the same timing that was previewed (UC3).</summary>
    Task<SubscriptionDetails> CommitPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken cancellationToken = default);

    Task<SubscriptionDetails> PauseSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<SubscriptionDetails> ResumeSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a subscription, either immediately or at the end of the current period (UC4).</summary>
    Task<SubscriptionDetails> CancelSubscriptionAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default);

    Task<SubscriptionDetails> ReactivateSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
