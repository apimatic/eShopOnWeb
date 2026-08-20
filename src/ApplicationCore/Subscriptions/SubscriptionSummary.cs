using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed record SubscriptionSummary(
    int Id,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    string? Currency,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt);
