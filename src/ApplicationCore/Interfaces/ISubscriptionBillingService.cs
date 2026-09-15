using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record BillingBuyer(string Id, string Email, string FirstName, string LastName);

public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    int Interval,
    string IntervalUnit);

public record CustomerSubscription(
    int Id,
    string? ProductHandle,
    string? ProductName,
    decimal? Price,
    string State,
    DateTimeOffset? NextBillingAt);

public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<CustomerSubscription> SubscribeAsync(BillingBuyer buyer, string productHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomerSubscription>> ListMySubscriptionsAsync(BillingBuyer buyer, CancellationToken cancellationToken);
}
