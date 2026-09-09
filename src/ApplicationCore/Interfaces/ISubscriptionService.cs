using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A subscription plan ("product" in Maxio Advanced Billing terms) available for eShopOnWeb shoppers.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public int MaxioProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }
}

/// <summary>
/// A subscription belonging to an eShopOnWeb user, backed by Maxio Advanced Billing
/// as the billing system of record.
/// </summary>
public class SubscriptionSummary
{
    public int MaxioSubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int MaxioCustomerId { get; set; }
}

/// <summary>Result of a subscribe operation; <see cref="Created"/> is false when an equivalent subscription already existed.</summary>
public class SubscribeResult
{
    public bool Created { get; set; }
    public SubscriptionSummary Subscription { get; set; } = new();
}

/// <summary>Contract for recurring-subscription billing backed by Maxio Advanced Billing.</summary>
public interface ISubscriptionService
{
    /// <summary>Lists the subscription plans available in the configured Maxio product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given eShopOnWeb user (idempotently), then
    /// subscribes them to the plan identified by <paramref name="planHandle"/>. If the user
    /// already holds an active subscription to that plan, it is returned without creating a
    /// duplicate.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(string userId, string email, string fullName, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the user's subscriptions. Returns an empty list when the user has no Maxio customer yet.</summary>
    Task<IReadOnlyList<SubscriptionSummary>> GetSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default);
}
