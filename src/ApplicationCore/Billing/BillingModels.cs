using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record BillingPlan(
    long Id,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

public sealed record BillingCustomer(long Id, string Reference, string Email);

public sealed record BillingSubscription(
    long Id,
    string Reference,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt,
    long CustomerId,
    string? ProductFamilyHandle);

public sealed record SubscribeResult(BillingSubscription Subscription, bool WasCreated);
