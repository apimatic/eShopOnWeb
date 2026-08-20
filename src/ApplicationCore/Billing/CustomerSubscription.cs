using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record CustomerSubscription(
    long Id,
    string State,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt);
