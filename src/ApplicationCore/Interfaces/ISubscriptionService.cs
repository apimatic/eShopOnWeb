using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Use-case surface for the subscription feature (mirrors <see cref="IOrderService"/>):
/// orchestrates <see cref="IBillingClient"/> calls and publishes the corresponding MediatR
/// notification after a successful state change (UC1–UC4).
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>UC1: ensures a provider customer exists for <paramref name="userName"/> and enrolls
    /// them in <paramref name="productHandle"/>, or returns their already-active subscription.</summary>
    Task<Subscription> SubscribeAsync(string userName, string email, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>The active/trialing subscription for a given eShopOnWeb user, if any. Does not create a customer record.</summary>
    Task<Subscription?> FindSubscriptionForUserAsync(string userName, CancellationToken cancellationToken = default);

    Task<Subscription> GetSubscriptionAsync(int subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>UC2: records usage against the subscription's metered component.</summary>
    Task<BillingUsageBalance> RecordUsageAsync(int subscriptionId, int quantity, string? memo, CancellationToken cancellationToken = default);

    /// <summary>UC3: previews the cost of a plan change without committing it.</summary>
    Task<BillingProrationPreview> PreviewPlanChangeAsync(int subscriptionId, string targetProductHandle, bool applyNow, CancellationToken cancellationToken = default);

    /// <summary>UC3: commits a plan change, either immediately (prorated) or at next renewal (not prorated).
    /// <paramref name="expectedPreview"/> must be the preview most recently shown to the customer; the
    /// commit re-previews and rejects with <see cref="Exceptions.StalePreviewException"/> if the amounts
    /// have moved, rather than silently applying a different charge than the one shown.</summary>
    Task<Subscription> ChangePlanAsync(int subscriptionId, string targetProductHandle, bool applyNow, BillingProrationPreview expectedPreview, CancellationToken cancellationToken = default);

    Task<Subscription> PauseAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> ResumeAsync(int subscriptionId, CancellationToken cancellationToken = default);

    Task<Subscription> CancelAsync(int subscriptionId, bool endOfPeriod, string? reason, CancellationToken cancellationToken = default);

    Task<Subscription> ReactivateAsync(int subscriptionId, CancellationToken cancellationToken = default);
}
