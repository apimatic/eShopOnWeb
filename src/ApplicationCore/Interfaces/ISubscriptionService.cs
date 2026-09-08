using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Subscription use-cases backed by Maxio Advanced Billing as the billing system of record.
/// </summary>
public interface ISubscriptionService
{
    Task<IReadOnlyList<MaxioPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently enrolls the user in the given plan: ensures a single Maxio customer exists for
    /// the user, and never creates a second live subscription to the same plan (double-submit safe).
    /// </summary>
    Task<SubscriptionResult> SubscribeAsync(string userId, string planHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionResult>> GetSubscriptionsForUserAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of a subscribe/list operation, sourced from Maxio.
/// </summary>
public class SubscriptionResult
{
    public int MaxioSubscriptionId { get; set; }
    public int MaxioCustomerId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>
    /// False when the enrollment already existed (idempotent replay of a previous request).
    /// </summary>
    public bool NewlyCreated { get; set; }
}
