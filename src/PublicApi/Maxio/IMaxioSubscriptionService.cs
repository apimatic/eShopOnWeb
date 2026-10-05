using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// The current state of a shopper's subscription as returned by this API.
/// </summary>
public record SubscriptionSummary(
    int MaxioSubscriptionId,
    string State,
    string? PlanHandle,
    string? PlanName,
    long PriceInCents,
    string? Currency,
    string? IntervalUnit,
    int? Interval,
    DateTime? NextBillingDate,
    DateTime CreatedAt);

public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<SubscriptionPlanSummary>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionSummary> SubscribeAsync(string username, string productHandle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionSummary>> GetMySubscriptionsAsync(string username, CancellationToken cancellationToken = default);
}

public record SubscriptionPlanSummary(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string? IntervalUnit,
    bool RequiresPaymentMethod);