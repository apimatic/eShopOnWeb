using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed record SubscriptionDetails(
    long Id,
    string Reference,
    string State,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string PricePointName,
    DateTimeOffset? NextBillingAt);
