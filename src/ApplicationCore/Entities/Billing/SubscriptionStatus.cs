using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

public sealed record SubscriptionStatus(
    int Id,
    string State,
    string ProductHandle,
    string ProductName,
    long PriceInCents,
    DateTimeOffset? NextBillingDate);
