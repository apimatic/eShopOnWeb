using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed record ShopperSubscription(
    long Id,
    string ProductHandle,
    string ProductName,
    decimal Price,
    string State,
    DateTimeOffset? NextBillingDate);
