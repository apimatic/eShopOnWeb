using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SubscriptionPlanInfo(
    string Handle,
    string? Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit);

public record SubscriptionPlanList(IReadOnlyList<SubscriptionPlanInfo> Plans, bool Truncated);

public record SubscriptionInfo(
    int? SubscriptionId,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? State,
    DateTimeOffset? NextBillingDate);

public record SubscribeResult(SubscriptionInfo Subscription, bool AlreadySubscribed);

/// <summary>Recurring-subscription billing, with Maxio Advanced Billing as the system of record.</summary>
public interface IMaxioSubscriptionService
{
    Task<SubscriptionPlanList> ListPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(string userId, string email, string? planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionInfo>> GetMySubscriptionsAsync(string userId, CancellationToken cancellationToken);
}
