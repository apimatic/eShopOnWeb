using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit);

public sealed record SubscriptionDetails(
    string Reference,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingAt);

public sealed record SubscriptionCustomer(
    string Reference,
    string FirstName,
    string LastName,
    string Email);

