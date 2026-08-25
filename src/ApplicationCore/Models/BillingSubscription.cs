using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

public sealed record BillingSubscription(
    int Id,
    string Reference,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingDate);
