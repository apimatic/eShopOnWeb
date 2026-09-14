using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed record SubscriptionDetails(
    long Id,
    long CustomerId,
    string Reference,
    string ProductFamilyHandle,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt);
