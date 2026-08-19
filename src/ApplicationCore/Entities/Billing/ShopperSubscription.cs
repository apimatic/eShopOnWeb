using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

public sealed record ShopperSubscription(
    int Id,
    string ProductHandle,
    string ProductName,
    string State,
    decimal Price,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextBillingAt,
    string? Reference);
