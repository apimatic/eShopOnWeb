using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record CustomerSubscription(
    int Id,
    string ProductHandle,
    string ProductName,
    decimal Price,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt);
