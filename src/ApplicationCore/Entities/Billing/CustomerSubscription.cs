using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

public sealed record CustomerSubscription(
    long Id,
    string ProductHandle,
    string ProductName,
    decimal Price,
    string State,
    DateTimeOffset? NextBillingDate);
