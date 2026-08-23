using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed record SubscriptionDetails(
    int? Id,
    string Reference,
    string ProductHandle,
    string? ProductName,
    long? PriceInCents,
    string? State,
    DateTimeOffset? NextBillingDate,
    int? Interval,
    string? IntervalUnit,
    string? Currency);
