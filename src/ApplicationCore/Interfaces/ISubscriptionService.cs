using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Read model of a subscription plan (a Maxio product) offered by the shop.
/// </summary>
public class SubscriptionPlanInfo
{
    /// <summary>Maxio product id.</summary>
    public long ProductId { get; set; }

    /// <summary>Stable API handle of the plan — the value used to subscribe.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price per billing period.</summary>
    public decimal Price { get; set; }

    public int Interval { get; set; }

    /// <summary>Billing period unit, e.g. "month".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    public decimal? TrialPrice { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>True when Maxio requires a payment method at signup.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;
}

/// <summary>
/// Read model of a user's subscription as recorded in Maxio Advanced Billing.
/// </summary>
public class SubscriptionInfo
{
    /// <summary>Maxio subscription id.</summary>
    public long SubscriptionId { get; set; }

    public string Reference { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring price per billing period.</summary>
    public decimal Price { get; set; }

    /// <summary>Subscription state as reported by Maxio (active, trialing, canceled, ...).</summary>
    public string State { get; set; } = string.Empty;

    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }
}

/// <summary>
/// Result of a subscribe operation.
/// </summary>
public class SubscribeResult
{
    /// <summary>True when a new subscription was created; false when an existing one was returned (idempotent replay).</summary>
    public bool Created { get; set; }

    public SubscriptionInfo Subscription { get; set; } = new();
}

/// <summary>
/// Subscription billing service backed by Maxio Advanced Billing.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans (products) available in the configured Maxio product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlanInfo>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes a shop user to a plan. Idempotent: ensures the Maxio customer exists
    /// (one per shop user) and never creates a duplicate active subscription for the
    /// same user + plan, no matter how often the call is repeated or clicked.
    /// </summary>
    /// <param name="planHandle">The handle of the plan (Maxio product) to subscribe to.</param>
    /// <param name="userId">The shop user's id.</param>
    /// <param name="userName">The shop user's user name / email used for display purposes.</param>
    /// <param name="email">The shop user's email address.</param>
    Task<SubscribeResult> SubscribeAsync(string planHandle, string userId, string userName, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the shop user's subscriptions as recorded in Maxio.
    /// </summary>
    Task<IReadOnlyList<SubscriptionInfo>> GetMySubscriptionsAsync(string userId, CancellationToken cancellationToken = default);
}