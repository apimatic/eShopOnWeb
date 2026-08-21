using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record BillingSubscription(
    long Id,
    long CustomerId,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset CreatedAt);

public sealed record BillingCustomerIdentity(
    string UserId,
    string Email,
    string FirstName,
    string LastName);

public sealed record BillingCustomer(long Id, string Reference);

public sealed record SubscriptionResult(BillingSubscription Subscription, bool Created);
