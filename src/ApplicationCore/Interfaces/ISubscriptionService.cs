using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The subscription use-case surface for eShopOnWeb — the one thing the storefront and the
/// PublicApi orchestrate against. Mirrors <see cref="IOrderService"/>: it validates, drives the
/// billing client, and publishes the in-process notification for the change it made.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the plans available to subscribe to (UC1, step 1).</summary>
    Task<IReadOnlyCollection<BillingPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls an eShopOnWeb user in a plan (UC1). Idempotent: when the user already has a live
    /// subscription, that subscription is returned rather than a second enrollment being created.
    /// </summary>
    Task<BillingSubscription> SubscribeAsync(string userReference,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the user's subscriptions. Returns empty when the user has never subscribed.</summary>
    Task<IReadOnlyCollection<BillingSubscription>> GetSubscriptionsForUserAsync(string userReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records metered usage against the user's live subscription (UC2), returning the record
    /// and the period-to-date total.
    /// </summary>
    Task<UsageRecord> RecordUsageAsync(string userReference,
        decimal quantity,
        string? memo,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the period-to-date metered total for the user's live subscription (UC2).</summary>
    Task<decimal?> GetPeriodToDateUsageAsync(string userReference,
        CancellationToken cancellationToken = default);

    /// <summary>Quotes a plan change without committing it (UC3).</summary>
    Task<PlanChangePreview> PreviewPlanChangeAsync(string userReference,
        string targetPlanHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits a plan change (UC3). When <paramref name="expectedPaymentDue"/> is supplied it is
    /// re-quoted first and the change is rejected if the amount moved, so the customer is never
    /// charged an amount other than the one they confirmed.
    /// </summary>
    Task<BillingSubscription> ChangePlanAsync(string userReference,
        string targetPlanHandle,
        PlanChangeTiming timing,
        decimal? expectedPaymentDue = null,
        CancellationToken cancellationToken = default);

    /// <summary>Holds the user's subscription (UC4), optionally scheduling an automatic resume.</summary>
    Task<BillingSubscription> PauseAsync(string userReference,
        DateTimeOffset? automaticallyResumeAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the user's held subscription to active (UC4).</summary>
    Task<BillingSubscription> ResumeAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>Cancels the user's subscription now or at the period end (UC4).</summary>
    Task<BillingSubscription> CancelAsync(string userReference,
        CancellationTiming timing,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reactivates the user's cancelled subscription (UC4).</summary>
    Task<BillingSubscription> ReactivateAsync(string userReference, CancellationToken cancellationToken = default);
}
