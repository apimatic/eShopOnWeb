using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed record CustomerSubscription(
    int Id,
    string State,
    string? ProductHandle,
    string? ProductName,
    decimal Price,
    long PriceInCents,
    DateTimeOffset? NextBillingDate,
    string? Reference);
