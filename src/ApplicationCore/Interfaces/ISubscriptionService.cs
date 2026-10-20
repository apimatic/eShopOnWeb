using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SubscriptionPlan(string Handle, string Name, string? Description, long PriceInCents,
    int? Interval, string? IntervalUnit);

public record PlanList(IReadOnlyList<SubscriptionPlan> Plans, bool IsTruncated);

public record SubscriptionSummary(int? SubscriptionId, string? PlanHandle, string? PlanName, long? PriceInCents,
    string? State, DateTimeOffset? NextBillingDate);

public record SubscriptionList(IReadOnlyList<SubscriptionSummary> Subscriptions, bool IsTruncated);

public record SubscribeRequest(string UserKey, string Email, string? FirstName, string? LastName, string? PlanHandle);

/// <summary>Recurring-subscription billing (Maxio is the system of record).</summary>
public interface ISubscriptionService
{
    Task<PlanList> ListPlansAsync(CancellationToken cancellationToken);

    /// <exception cref="Exceptions.SubscriptionBillingException"/>
    Task<SubscriptionSummary> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken);

    /// <exception cref="Exceptions.SubscriptionBillingException"/>
    Task<SubscriptionList> ListMySubscriptionsAsync(string userKey, CancellationToken cancellationToken);
}
