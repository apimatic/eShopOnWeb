using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan (Maxio "Product"), as surfaced to eShopOnWeb shoppers.
/// </summary>
public record MaxioProduct(
    int Id,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    DateTimeOffset? ArchivedAt);
