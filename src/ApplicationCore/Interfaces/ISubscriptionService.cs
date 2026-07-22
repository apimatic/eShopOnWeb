using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface. Mirrors <see cref="IOrderService"/>: hosts orchestrate,
/// this service holds the rules, and the billing provider is reached only through
/// <see cref="IBillingClient"/>.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the plans a customer may subscribe to (UC1, step 1).</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the eShopOnWeb user identified by <paramref name="userName"/> in a plan (UC1).
    /// Creates the provider-side customer if needed, and returns any existing active subscription
    /// on that plan instead of enrolling twice.
    /// </summary>
    Task<Subscription> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the subscriptions belonging to an eShopOnWeb user.</summary>
    Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the user's first currently-billing subscription, or null when they have none.
    /// </summary>
    Task<Subscription?> FindActiveSubscriptionAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records metered usage against a subscription and reads back the running period-to-date
    /// total (UC2). Validates that the configured component is genuinely metered first.
    /// </summary>
    Task<UsageReceipt> RecordUsageAsync(int subscriptionId, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records one unit of usage for the user's active subscription, if they have one. Returns
    /// null when the user has no active subscription. Never throws for the automatic
    /// order-placed hook's benefit — see the caller for failure handling.
    /// </summary>
    Task<UsageReceipt?> RecordUsageForUserAsync(string userName, decimal quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>Reads the period-to-date metered balance for a subscription (UC2).</summary>
    Task<int?> GetPeriodToDateUnitsAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>Quotes a plan change without applying it (UC3).</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a plan change (UC3). <paramref name="previewToken"/> must be the token of a preview
    /// taken for this exact change; the commit is refused when the quote has moved since.
    /// </summary>
    Task<Subscription> ChangePlanAsync(int subscriptionId, string targetPlanHandle, PlanChangeTiming timing, string previewToken, CancellationToken cancellationToken = default);

    /// <summary>Applies a lifecycle transition to a subscription (UC4).</summary>
    Task<Subscription> ApplyLifecycleActionAsync(int subscriptionId, SubscriptionLifecycleAction action, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies that the configured metered component resolves to a metered-kind component on the
    /// configured product family, and returns it. Throws
    /// <see cref="Exceptions.BillingConfigurationException"/> when it does not (UC2 precondition).
    /// </summary>
    Task<MeteredComponent> GetVerifiedMeteredComponentAsync(CancellationToken cancellationToken = default);
}
