using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record ShopperSubscription(
    int? Id,
    string ProductHandle,
    string ProductName,
    decimal Price,
    string State,
    DateTimeOffset? NextBillingDate);
