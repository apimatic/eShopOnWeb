using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface consumed by the storefront and the PublicApi. Mirrors the
/// role <see cref="IOrderService"/> plays for the one-time purchase flow: it validates input,
/// drives the billing provider through <see cref="IBillingClient"/>, and announces lifecycle
/// changes in-process.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the plans a shopper can subscribe to (UC1, step 1).</summary>
    Task<IReadOnlyCollection<BillingPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrols an eShopOnWeb user in a plan (UC1). Ensures a provider-side customer exists for the
    /// user first, and returns the existing live subscription instead of creating a second one if
    /// the user is already subscribed.
    /// </summary>
    Task<Subscription> SubscribeAsync(
        SubscriptionActor actor,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to an eShopOnWeb user. Returns an empty collection when
    /// the user has no provider-side customer record at all.
    /// </summary>
    Task<IReadOnlyCollection<Subscription>> GetSubscriptionsAsync(
        string userName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one subscription, enforcing that <paramref name="actor"/> is allowed to see it.
    /// Returns <see langword="null"/> when the id is unknown to the provider.
    /// </summary>
    Task<Subscription?> GetSubscriptionAsync(
        SubscriptionActor actor,
        int subscriptionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records pay-as-you-go usage against a subscription's configured metered component and
    /// reads back the running period-to-date total (UC2).
    /// </summary>
    Task<UsageReport> RecordUsageAsync(
        SubscriptionActor actor,
        int subscriptionId,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records usage against whichever subscription of the user's is currently live. Used by the
    /// automatic "one order placed, one billable unit" hook. Returns <see langword="null"/> when
    /// the user has no live subscription — that is an expected, non-exceptional outcome.
    /// </summary>
    Task<UsageReport?> RecordUsageForUserAsync(
        string userName,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>Computes the cost of a plan change without applying it (UC3, step 2).</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(
        SubscriptionActor actor,
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a plan change (UC3, step 4). When
    /// <paramref name="expectedPaymentDueInCents"/> is supplied, the previewed amount is
    /// re-verified immediately before committing and the change is rejected if it has moved — the
    /// customer is never charged an amount other than the one they were shown.
    /// </summary>
    Task<Subscription> ChangePlanAsync(
        SubscriptionActor actor,
        int subscriptionId,
        string targetPlanHandle,
        PlanChangeTiming timing,
        long? expectedPaymentDueInCents,
        CancellationToken cancellationToken = default);

    /// <summary>Applies a lifecycle transition to a subscription (UC4).</summary>
    Task<Subscription> ApplyLifecycleActionAsync(
        SubscriptionActor actor,
        int subscriptionId,
        SubscriptionLifecycleAction action,
        CancellationTiming cancellationTiming,
        string? reason,
        CancellationToken cancellationToken = default);
}
