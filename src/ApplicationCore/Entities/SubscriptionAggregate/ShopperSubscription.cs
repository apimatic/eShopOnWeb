using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public record ShopperSubscription(
    int Id,
    string ProductHandle,
    string ProductName,
    decimal Price,
    string State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt);
