using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record MaxioPlan(
    long Id,
    string Name,
    string Handle,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record MaxioCustomer(long Id, string Reference, string Email);

public sealed record MaxioSite(bool RelationshipInvoicingEnabled, bool IsTestSite);

public sealed record MaxioSubscription(
    long Id,
    string Reference,
    string State,
    long PriceInCents,
    DateTimeOffset? NextBillingAt,
    long CustomerId,
    string CustomerReference,
    long ProductId,
    string ProductName,
    string ProductHandle,
    int Interval,
    string IntervalUnit);

public sealed record CreateMaxioCustomer(
    string FirstName,
    string LastName,
    string Email,
    string Reference,
    Guid UniquenessToken);

public sealed record CreateMaxioSubscription(
    string ProductHandle,
    string CustomerReference,
    string SubscriptionReference,
    string PaymentCollectionMethod,
    Guid UniquenessToken);
