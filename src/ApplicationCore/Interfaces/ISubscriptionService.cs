using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SubscriptionPlan(string Handle, string Name, string? Description, decimal Price, int? Interval, string? IntervalUnit);

public record SubscriptionPlans(IReadOnlyList<SubscriptionPlan> Plans, bool Truncated);

public record SubscriptionSummary(
    int SubscriptionId,
    string? PlanHandle,
    string? PlanName,
    decimal Price,
    string? State,
    System.DateTimeOffset? NextBillingDate,
    string? Reference);

public record SubscribeResult(SubscriptionSummary Subscription, bool AlreadySubscribed);

public interface ISubscriptionService
{
    Task<SubscriptionPlans> GetPlansAsync(CancellationToken ct);

    /// <param name="userName">Identity name from the JWT.</param>
    Task<SubscribeResult> SubscribeAsync(string userName, string planHandle, string? firstName, string? lastName, CancellationToken ct);

    Task<IReadOnlyList<SubscriptionSummary>> GetMySubscriptionsAsync(string userName, CancellationToken ct);
}
