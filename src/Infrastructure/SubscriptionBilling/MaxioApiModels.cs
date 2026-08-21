using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.SubscriptionBilling;

public sealed record MaxioSite(string Currency, bool RelationshipInvoicingEnabled);

public sealed record MaxioProduct(
    long Id,
    string Name,
    string? Handle,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequireCreditCard,
    DateTimeOffset? ArchivedAt,
    MaxioProductFamily ProductFamily);

public sealed record MaxioProductFamily(long Id, string Name, string Handle);

public sealed record MaxioCustomer(long Id, string Email, string? Reference);

public sealed record MaxioSubscription(
    long Id,
    string State,
    long ProductPriceInCents,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt,
    MaxioCustomer Customer,
    MaxioProduct? Product);

public interface IMaxioBillingClient
{
    Task<MaxioSite> GetSiteAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaxioProduct>> GetProductsAsync(string productFamilyHandle, CancellationToken cancellationToken = default);
    Task<MaxioCustomer?> FindCustomerAsync(string reference, CancellationToken cancellationToken = default);
    Task<MaxioCustomer> CreateCustomerAsync(string firstName, string lastName, string email, string reference, string uniquenessToken, CancellationToken cancellationToken = default);
    Task<MaxioSubscription?> FindSubscriptionAsync(string reference, CancellationToken cancellationToken = default);
    Task<MaxioSubscription> CreateSubscriptionAsync(string productHandle, long customerId, string reference, string uniquenessToken, string paymentCollectionMethod, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(long customerId, CancellationToken cancellationToken = default);
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
