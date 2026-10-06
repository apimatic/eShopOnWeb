using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>A subscribable plan (a Maxio product under the configured family).</summary>
public class SubscriptionPlanDto
{
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public long? PriceInCents { get; set; }
    public decimal? Price { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public long? TrialPriceInCents { get; set; }
    public bool? RequiresPaymentMethod { get; set; }
}

/// <summary>A subscription as reflected back to the shopper.</summary>
public class SubscriptionDto
{
    public int? Id { get; set; }
    public string? Reference { get; set; }
    public string? State { get; set; }
    public bool IsLive { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

/// <summary>Outcome of a subscribe request.</summary>
public class SubscribeOutcome
{
    public required SubscriptionDto Subscription { get; init; }

    /// <summary>True when an existing live subscription was returned instead of creating a new one.</summary>
    public required bool AlreadySubscribed { get; init; }
}

/// <summary>
/// Subscription billing backed by Maxio Advanced Billing. The email identifies the
/// eShopOnWeb shopper; all implementations are idempotent per (shopper, plan).
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeOutcome> SubscribeAsync(string email, string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionDto>> GetSubscriptionsForShopperAsync(string email, CancellationToken cancellationToken);
}
