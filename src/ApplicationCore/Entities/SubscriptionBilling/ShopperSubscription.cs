using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

public sealed record ShopperSubscription(
    int Id,
    string ProductHandle,
    string ProductName,
    decimal Price,
    string State,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CurrentPeriodEndsAt,
    string? Interval);
