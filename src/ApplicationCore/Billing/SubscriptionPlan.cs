using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record SubscriptionDetails(
    int Id,
    string Reference,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    string Currency,
    string State,
    DateTimeOffset? NextBillingDate,
    int? Interval,
    string? IntervalUnit);

public sealed record BillingCustomerProfile(
    string UserId,
    string FirstName,
    string LastName,
    string Email);

public sealed record SubscriptionEnrollmentResult(
    SubscriptionDetails Subscription,
    bool Created);
