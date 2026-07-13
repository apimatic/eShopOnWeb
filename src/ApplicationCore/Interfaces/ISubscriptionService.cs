using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Use-case surface for the subscription module (mirrors IOrderService): validates input,
/// drives the billing client, and publishes MediatR notifications on state changes.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>UC1 step 1: the plans a customer can browse and subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// UC1: enrolls the eShopOnWeb user (identified by <paramref name="customerReference"/>) in
    /// <paramref name="productHandle"/>. Idempotent: an existing active/trialing subscription for
    /// the customer is returned as-is rather than creating a duplicate enrollment.
    /// </summary>
    Task<SubscriptionDetails> SubscribeAsync(string customerReference, string customerEmail, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>The customer's current active/trialing subscription, or null if they have none.</summary>
    Task<SubscriptionDetails?> GetActiveSubscriptionAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>
    /// The customer's most recent subscription regardless of state (active, trialing, on hold, past
    /// due, unpaid, trial ended, or canceled), or null if they have none. Used to resolve "my
    /// subscription" for UC2-UC4 actions when no explicit subscription id is given - broader than
    /// <see cref="GetActiveSubscriptionAsync"/> so lifecycle actions can still find a paused,
    /// past-due, or canceled subscription to resume or reactivate.
    /// </summary>
    Task<SubscriptionDetails?> GetCurrentSubscriptionAsync(string customerReference, CancellationToken cancellationToken = default);

    /// <summary>Every subscription belonging to the customer, regardless of state.</summary>
    Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);

    Task<SubscriptionDetails> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>UC2: records a unit of metered usage. Rejects zero/negative quantity and non-active subscriptions before any provider call.</summary>
    Task<ComponentUsageStatus> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>UC2: the metered component's period-to-date usage balance.</summary>
    Task<ComponentUsageStatus> GetUsageStatusAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3: previews the cost of moving to <paramref name="targetProductHandle"/>, either applied
    /// now with proration (<paramref name="applyImmediately"/> true) or at next renewal with no
    /// proration (false).
    /// </summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyImmediately, CancellationToken cancellationToken = default);

    /// <summary>
    /// UC3: commits a plan change only if a freshly computed preview still matches
    /// <paramref name="confirmedPreview"/> (protects against a stale preview being silently applied).
    /// </summary>
    Task<SubscriptionDetails> CommitPlanChangeAsync(int subscriptionId, string targetProductHandle, PlanChangePreview confirmedPreview, CancellationToken cancellationToken = default);

    /// <summary>UC4: pause/resume/cancel/reactivate. Each rejects illegal transitions using the subscription's current state before calling the provider.</summary>
    Task<SubscriptionDetails> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<SubscriptionDetails> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<SubscriptionDetails> CancelAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default);

    Task<SubscriptionDetails> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
