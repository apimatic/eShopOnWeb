using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SubscriptionPlan(string Handle, string Name, string? Description, decimal Price, int? Interval, string? IntervalUnit);

public record SubscriptionPlanList(IReadOnlyList<SubscriptionPlan> Plans, bool IsTruncated);

public record SubscriptionInfo(
    int? SubscriptionId,
    string PlanHandle,
    string PlanName,
    decimal? Price,
    string State,
    DateTimeOffset? NextBillingDate);

public interface ISubscriptionService
{
    Task<SubscriptionPlanList> GetPlansAsync(CancellationToken ct);

    /// <summary>Subscribes the user to the plan. Idempotent per (user, plan).</summary>
    Task<SubscriptionInfo> SubscribeAsync(string userId, string email, string planHandle, CancellationToken ct);

    Task<IReadOnlyList<SubscriptionInfo>> GetMySubscriptionsAsync(string userId, CancellationToken ct);
}
