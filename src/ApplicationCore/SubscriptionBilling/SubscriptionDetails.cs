using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

public sealed record SubscriptionDetails(
    int Id,
    string Reference,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingDate);
