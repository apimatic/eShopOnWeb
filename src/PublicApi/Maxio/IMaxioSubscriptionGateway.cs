using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed record MaxioPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record MaxioCustomer(int Id, string Reference);

public sealed record MaxioSubscription(
    int Id,
    string Reference,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    string? Currency,
    string? State,
    DateTimeOffset? NextBillingDate,
    int? Interval,
    string? IntervalUnit);

public interface IMaxioSubscriptionGateway
{
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomer> EnsureCustomerAsync(
        string reference,
        string firstName,
        string lastName,
        string email,
        CancellationToken cancellationToken);
    Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioSubscription> CreateSubscriptionAsync(
        string customerReference,
        string subscriptionReference,
        string productHandle,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscription>> ListCustomerSubscriptionsAsync(
        string customerReference,
        CancellationToken cancellationToken);
}
