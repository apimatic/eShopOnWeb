using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A shopper's subscription as recorded in the billing system of record.
/// </summary>
public record SubscriptionDetails(
    long Id,
    string State,
    string? ProductHandle,
    string? ProductName,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset CreatedAt);
