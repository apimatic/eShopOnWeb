using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record ShopperSubscription(
    int Id,
    string State,
    string ProductHandle,
    string ProductName,
    decimal Price,
    DateTimeOffset? NextBillingAt);
