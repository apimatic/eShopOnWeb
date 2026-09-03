using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed record ShopperSubscription(
    int Id,
    string? ProductHandle,
    string? ProductName,
    string State,
    long PriceInCents,
    decimal Price,
    DateTimeOffset? NextBillingAt,
    string? Reference);
