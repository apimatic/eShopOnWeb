using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public interface IMaxioBillingClient
{
    Task<IReadOnlyList<MaxioPlan>> GetPlansAsync(CancellationToken cancellationToken);
    Task<MaxioCustomerRecord?> FindCustomerByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioCustomerRecord> CreateCustomerAsync(string reference, string firstName, string lastName, string email, CancellationToken cancellationToken);
    Task<string> GetNoPaymentCollectionMethodAsync(CancellationToken cancellationToken);
    Task<MaxioSubscriptionRecord?> FindSubscriptionByReferenceAsync(string reference, CancellationToken cancellationToken);
    Task<MaxioSubscriptionRecord> CreateSubscriptionAsync(string customerReference, string subscriptionReference, string productHandle, string paymentCollectionMethod, DateTimeOffset nextBillingAt, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaxioSubscriptionRecord>> ListCustomerSubscriptionsAsync(int customerId, CancellationToken cancellationToken);
}

public sealed record MaxioPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record MaxioCustomerRecord(int Id, string Reference, string Email);

public sealed record MaxioSubscriptionRecord(
    int Id,
    string? Reference,
    string State,
    string? ProductHandle,
    string? ProductName,
    long PriceInCents,
    System.DateTimeOffset? CurrentPeriodEndsAt,
    System.DateTimeOffset? NextAssessmentAt);
