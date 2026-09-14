using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record MaxioPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record MaxioCustomer(int Id, string Reference);

public sealed record MaxioSubscription(
    int Id,
    string? Reference,
    string ProductHandle,
    string ProductName,
    long? ProductPriceInCents,
    long? CurrentBillingAmountInCents,
    string State,
    DateTimeOffset? NextBillingDate);

public sealed record MaxioSubscriptionCreateResult(MaxioSubscription Subscription, bool Created);

public sealed record MaxioCustomerProfile(string FirstName, string LastName, string Email);
